using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace ModelScaffolder.VisualStudio
{
    /// <summary>
    ///     Solution Explorer > right-click a *.model.json > "Scaffold Solution...":
    ///     dialog -> bundled CLI -> Output pane + Error List -> offer to open the generated solution.
    /// </summary>
    internal sealed class ScaffoldSolutionCommand
    {
        public static readonly Guid CommandSet = new("8a1bab11-7afc-4c93-b6a9-06cb3f05ed9d");
        public const int CommandId = 0x0100;

        private const string ModelFileSuffix = ".model.json";
        private const string SettingsCollection = "ModelScaffolder";

        private readonly AsyncPackage _package;
        private readonly ScaffoldOutput _output;

        /// <summary>One generation at a time: two runs into the same folder would interleave their writes.</summary>
        private bool _running;

        private ScaffoldSolutionCommand(AsyncPackage package, OleMenuCommandService commands)
        {
            _package = package;
            _output = new ScaffoldOutput(package);

            var command = new OleMenuCommand((_, _) => _package.JoinableTaskFactory.RunAsync(ExecuteSafelyAsync).FileAndForget("ModelScaffolder/Scaffold"),
                new CommandID(CommandSet, CommandId));
            command.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                command.Visible = GetSelectedModelPath() is not null;
                command.Enabled = command.Visible && !_running;
            };
            commands.AddCommand(command);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commands = (OleMenuCommandService)(await package.GetServiceAsync(typeof(IMenuCommandService)))!;
            _ = new ScaffoldSolutionCommand(package, commands);
        }

        private async Task ExecuteSafelyAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            if (_running)
                return;

            _running = true;
            try
            {
                await ExecuteAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(_package, ex.Message, "ModelScaffolder", OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            finally
            {
                _running = false;
            }
        }

        private async Task ExecuteAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            var modelPath = GetSelectedModelPath();
            if (modelPath is null)
                return;

            var settings = new ShellSettingsManager(_package).GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!settings.CollectionExists(SettingsCollection))
                settings.CreateCollection(SettingsCollection);

            var request = new ScaffoldRequest
            {
                ModelPath = modelPath,
                OutputDirectory = settings.GetString(SettingsCollection, Key(modelPath, "output"), DefaultOutputDirectory(modelPath)),
                TemplateDirectory = NullIfEmpty(settings.GetString(SettingsCollection, Key(modelPath, "templates"), string.Empty)),
                TargetFramework = settings.GetString(SettingsCollection, Key(modelPath, "framework"), "net10.0"),
                AdminUser = settings.GetString(SettingsCollection, Key(modelPath, "user"), "admin")
            };

            if (new ScaffoldDialog(request).ShowModal() != true)
                return;

            // The generator reads the file from disk: unsaved edits in the editor would be silently ignored.
            new RunningDocumentTable(_package).SaveFileIfDirty(modelPath);

            // Remember everything except the password.
            settings.SetString(SettingsCollection, Key(modelPath, "output"), request.OutputDirectory);
            settings.SetString(SettingsCollection, Key(modelPath, "templates"), request.TemplateDirectory ?? string.Empty);
            settings.SetString(SettingsCollection, Key(modelPath, "framework"), request.TargetFramework);
            settings.SetString(SettingsCollection, Key(modelPath, "user"), request.AdminUser);

            var result = await GenerateAsync(request);

            // Only file conflicts? Offer to overwrite instead of making the user reopen the dialog.
            if (!result.Succeeded && result.FatalError is null && !request.Overwrite &&
                result.Diagnostics.Where(d => d.IsError).All(d => d.Code == CliRunner.FileConflictCode))
            {
                var conflicts = result.Diagnostics.Count(d => d.Code == CliRunner.FileConflictCode);
                if (Ask($"{conflicts} existing file(s) differ from the generated version (listed in the Error List).\n\n" +
                        "Overwrite them? Your edits in those files will be lost.", OLEMSGICON.OLEMSGICON_WARNING))
                {
                    request.Overwrite = true;
                    result = await GenerateAsync(request);
                }
            }

            request.AdminPassword = string.Empty;

            if (!result.Succeeded)
                return;

            var solution = Directory.EnumerateFiles(result.OutputDirectory, "*.slnx")
                .Concat(Directory.EnumerateFiles(result.OutputDirectory, "*.sln"))
                .FirstOrDefault();

            if (solution is not null && Ask($"The solution was generated in:\n{result.OutputDirectory}\n\nOpen {Path.GetFileName(solution)} now?", OLEMSGICON.OLEMSGICON_QUERY))
            {
                var vsSolution = (IVsSolution)(await _package.GetServiceAsync(typeof(SVsSolution)))!;
                vsSolution.OpenSolutionFile(0, solution);
            }
        }

        private async Task<CliResult> GenerateAsync(ScaffoldRequest request)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            _output.Begin(request);

            var statusBar = (IVsStatusbar)(await _package.GetServiceAsync(typeof(SVsStatusbar)))!;
            object icon = (short)Constants.SBAI_General;
            statusBar.Animation(1, ref icon);
            statusBar.SetText("ModelScaffolder: generating...");

            CliResult result;
            try
            {
                await TaskScheduler.Default;
                result = await CliRunner.RunAsync(request, _package.DisposalToken);
            }
            finally
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                statusBar.Animation(0, ref icon);
            }

            _output.Report(result, request);
            statusBar.SetText(result.Succeeded ? "ModelScaffolder: done" : "ModelScaffolder: failed (see the Error List)");
            return result;
        }

        /// <summary>Full path of the selected file when exactly one *.model.json is selected; otherwise null.</summary>
        private string? GetSelectedModelPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection monitor)
                return null;

            IntPtr hierarchyPtr = IntPtr.Zero, containerPtr = IntPtr.Zero;
            try
            {
                if (ErrorHandler.Failed(monitor.GetCurrentSelection(out hierarchyPtr, out var itemId, out var multiSelect, out containerPtr)) ||
                    multiSelect is not null || hierarchyPtr == IntPtr.Zero)
                    return null;

                var hierarchy = (IVsHierarchy)Marshal.GetObjectForIUnknown(hierarchyPtr);
                return ErrorHandler.Succeeded(hierarchy.GetCanonicalName(itemId, out var path)) &&
                       path.EndsWith(ModelFileSuffix, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                    ? path
                    : null;
            }
            finally
            {
                if (hierarchyPtr != IntPtr.Zero) Marshal.Release(hierarchyPtr);
                if (containerPtr != IntPtr.Zero) Marshal.Release(containerPtr);
            }
        }

        /// <summary>
        ///     Next to the open solution's folder (not inside it, so the generated Directory.*.props stay separate):
        ///     e.g. solution in E:\Github\Tools\Tools.slnx and project "Store" -> E:\Github\Store.
        /// </summary>
        private string DefaultOutputDirectory(string modelPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var baseDirectory = Path.GetDirectoryName(modelPath)!;
            if (Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution &&
                ErrorHandler.Succeeded(solution.GetSolutionInfo(out var solutionDirectory, out _, out _)) &&
                !string.IsNullOrEmpty(solutionDirectory))
                baseDirectory = solutionDirectory;

            var parent = Path.GetDirectoryName(baseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? baseDirectory;
            return Path.Combine(parent, ReadProjectName(modelPath));
        }

        /// <summary>project.name from the model (regex: the model allows comments and trailing commas); falls back to the file name.</summary>
        private static string ReadProjectName(string modelPath)
        {
            try
            {
                var match = Regex.Match(File.ReadAllText(modelPath), "\"project\"\\s*:\\s*\\{[^}]*?\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                // Only a usable folder name; the generator reports an invalid project name itself.
                var name = match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
                if (name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    return name;
            }
            catch (IOException)
            {
                // Fall back to the file name.
            }

            var fileName = Path.GetFileName(modelPath);
            return fileName.Substring(0, fileName.Length - ModelFileSuffix.Length);
        }

        private bool Ask(string message, OLEMSGICON icon) =>
            VsShellUtilities.ShowMessageBox(_package, message, "ModelScaffolder", icon,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == (int)VSConstants.MessageBoxResult.IDYES;

        private static string Key(string modelPath, string name) => Path.GetFullPath(modelPath).ToLowerInvariant().Replace('\\', '/') + "|" + name;

        private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    }
}
