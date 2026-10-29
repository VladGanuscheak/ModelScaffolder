using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ModelScaffolder.VisualStudio
{
    /// <summary>Writes a run to the "ModelScaffolder" Output pane and its problems to the Error List.</summary>
    internal sealed class ScaffoldOutput
    {
        private static readonly Guid PaneGuid = new("9a08f06f-2778-4b0e-aded-39c244712320");
        private static readonly Guid ErrorProviderGuid = new("333a48e2-1ded-45dc-b43d-b11367c0f3ea");

        private readonly AsyncPackage _package;
        private readonly ErrorListProvider _errors;
        private IVsOutputWindowPane? _pane;

        public ScaffoldOutput(AsyncPackage package)
        {
            _package = package;
            _errors = new ErrorListProvider(package) { ProviderName = "ModelScaffolder", ProviderGuid = ErrorProviderGuid };
        }

        public void Begin(ScaffoldRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _errors.Tasks.Clear();
            var pane = Pane();
            pane.Clear();
            pane.Activate();
            Write($"Model:  {request.ModelPath}");
            Write($"Output: {request.OutputDirectory}");
            if (request.TemplateDirectory is not null)
                Write($"Templates: {request.TemplateDirectory}");
            Write($"Framework: {request.TargetFramework}, system user: {request.AdminUser}{(request.Overwrite ? ", overwrite" : string.Empty)}");
            Write(string.Empty);
        }

        public void Report(CliResult result, ScaffoldRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (result.FatalError is not null)
            {
                Write(result.FatalError);
                AddTask(TaskErrorCategory.Error, result.FatalError, request.ModelPath, null, null);
                _errors.Show();
                return;
            }

            foreach (var file in result.Files.Where(f => f.Action != "unchanged"))
                Write($"{file.Action,-10}{file.Path}");

            var unchanged = result.Files.Count(f => f.Action == "unchanged");
            if (unchanged > 0)
                Write($"{"",-10}({unchanged} file(s) unchanged)");

            foreach (var diagnostic in result.Diagnostics)
            {
                Write(diagnostic.ToString());

                // Diagnostics point either at a generated file (e.g. MS090 conflicts) or into the model JSON.
                var document = GeneratedFile(request.OutputDirectory, diagnostic.Path) ?? request.ModelPath;
                AddTask(diagnostic.IsError ? TaskErrorCategory.Error : TaskErrorCategory.Warning,
                    $"{diagnostic.Code}: {diagnostic.Message} ({diagnostic.Path})", document, diagnostic.Line, diagnostic.Column);
            }

            Write(string.Empty);
            Write(result.Succeeded
                ? $"Done: {result.Files.Count(f => f.Action == "add")} added, {result.Files.Count(f => f.Action == "update")} updated, {unchanged} unchanged."
                : "Failed: nothing was written.");

            if (result.Diagnostics.Count > 0)
                _errors.Show();
        }

        /// <param name="line">1-based, like the CLI; the Error List itself is 0-based.</param>
        private void AddTask(TaskErrorCategory category, string text, string document, int? line, int? column)
        {
            var task = new ErrorTask
            {
                Category = TaskCategory.BuildCompile,
                ErrorCategory = category,
                Text = text,
                Document = document,
                Line = (line ?? 1) - 1,
                Column = (column ?? 1) - 1,
                HierarchyItem = null
            };
            task.Navigate += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Open(task.Document, task.Line, task.Column);
            };
            _errors.Tasks.Add(task);
        }

        /// <summary>Opens the document and puts the caret on the diagnostic's position.</summary>
        private void Open(string document, int line, int column)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!File.Exists(document))
                return;

            VsShellUtilities.OpenDocument(_package, document, Guid.Empty, out _, out _, out var frame);
            if (frame is null)
                return;

            frame.Show();
            var view = VsShellUtilities.GetTextView(frame);
            if (view is null)
                return;

            // Out-of-range positions (file edited since) are rejected by the editor; fall back to the line start.
            if (ErrorHandler.Failed(view.SetCaretPos(line, column)))
                view.SetCaretPos(line, 0);
            view.CenterLines(line, 1);
        }

        /// <summary>The generated file a diagnostic path refers to, or null when the path points into the model ("schemas[0].entities[1]...").</summary>
        private static string? GeneratedFile(string outputDirectory, string path)
        {
            if (path.IndexOfAny(['[', '$']) >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.HasExtension(path))
                return null;

            var full = Path.Combine(outputDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? full : null;
        }

        private void Write(string line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Pane().OutputStringThreadSafe(line + Environment.NewLine);
        }

        private IVsOutputWindowPane Pane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pane is not null)
                return _pane;

            var outputWindow = (IVsOutputWindow)Package.GetGlobalService(typeof(SVsOutputWindow));
            var guid = PaneGuid;
            ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(ref guid, "ModelScaffolder", 1, 1));
            ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref guid, out var pane));
            return _pane = pane;
        }
    }
}
