using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModelScaffolder.VisualStudio
{
    public sealed class ScaffoldRequest
    {
        public string ModelPath { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>Optional custom template folder (a copy of the shipped Templates folder).</summary>
        public string? TemplateDirectory { get; set; }

        public string TargetFramework { get; set; } = "net10.0";
        public string AdminUser { get; set; } = "admin";

        /// <summary>Sent to the CLI through standard input, never on the command line.</summary>
        public string AdminPassword { get; set; } = string.Empty;

        public bool Overwrite { get; set; }
    }

    [DataContract]
    public sealed class CliResult
    {
        [DataMember(Name = "succeeded")] public bool Succeeded { get; set; }
        [DataMember(Name = "dryRun")] public bool DryRun { get; set; }
        [DataMember(Name = "outputDirectory")] public string OutputDirectory { get; set; } = string.Empty;
        [DataMember(Name = "files")] public List<CliFile> Files { get; set; } = [];
        [DataMember(Name = "diagnostics")] public List<CliDiagnostic> Diagnostics { get; set; } = [];

        /// <summary>Exit code 2 / unparsable output: the CLI's own message (standard error).</summary>
        public string? FatalError { get; set; }

        public static CliResult Fatal(string message) => new() { FatalError = message };
    }

    [DataContract]
    public sealed class CliFile
    {
        /// <summary>"add", "update" or "unchanged".</summary>
        [DataMember(Name = "action")] public string Action { get; set; } = string.Empty;
        [DataMember(Name = "path")] public string Path { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class CliDiagnostic
    {
        /// <summary>"error" or "warning".</summary>
        [DataMember(Name = "severity")] public string Severity { get; set; } = string.Empty;
        [DataMember(Name = "code")] public string Code { get; set; } = string.Empty;
        [DataMember(Name = "path")] public string Path { get; set; } = string.Empty;
        [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;

        /// <summary>1-based position in the model file, when the diagnostic points into the model.</summary>
        [DataMember(Name = "line")] public int? Line { get; set; }
        [DataMember(Name = "column")] public int? Column { get; set; }

        public bool IsError => Severity == "error";

        public override string ToString() =>
            $"{Severity} {Code}: {Path}{(Line.HasValue ? $" (line {Line}, column {Column})" : string.Empty)}: {Message}";
    }

    /// <summary>
    ///     Runs the bundled ModelScaffolder.Cli ("cli\modelscaffold.dll" next to this assembly) with <c>dotnet</c>.
    ///     Out of process on purpose: the generator's dependencies never meet Visual Studio's own.
    /// </summary>
    public static class CliRunner
    {
        /// <summary>Overwrite conflicts: the only errors that a re-run with "overwrite" fixes.</summary>
        public const string FileConflictCode = "MS090";

        public static string CliPath =>
            Path.Combine(Path.GetDirectoryName(typeof(CliRunner).Assembly.Location)!, "cli", "modelscaffold.dll");

        public static async Task<CliResult> RunAsync(ScaffoldRequest request, CancellationToken cancellationToken)
        {
            if (!File.Exists(CliPath))
                return CliResult.Fatal($"The bundled generator was not found at '{CliPath}'. Reinstall the extension.");

            var arguments = new List<string>
            {
                CliPath,
                request.ModelPath,
                "-o", request.OutputDirectory,
                "--framework", request.TargetFramework,
                "--admin-user", request.AdminUser,
                "--admin-password-stdin",
                "--json"
            };
            if (!string.IsNullOrWhiteSpace(request.TemplateDirectory))
                arguments.AddRange(["--templates", request.TemplateDirectory!]);
            if (request.Overwrite)
                arguments.Add("--force");

            var startInfo = new ProcessStartInfo("dotnet", string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(request.ModelPath)!
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult(true);

            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                return CliResult.Fatal("'dotnet' was not found. Install the .NET 10 SDK (the generated solution needs it too) and restart Visual Studio.");
            }

            using (cancellationToken.Register(() => Kill(process)))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                // The password goes through stdin as raw UTF-8 (net472 cannot set the stdin encoding of a child process).
                var password = new UTF8Encoding(false).GetBytes(request.AdminPassword + "\n");
                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(password, 0, password.Length, cancellationToken);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The CLI exited before reading stdin (e.g. bad arguments); its stderr explains why.
                }

                await Task.WhenAll(stdout, stderr, exited.Task);
                cancellationToken.ThrowIfCancellationRequested();

                var output = (await stdout).Trim();
                var errors = (await stderr).Trim();

                if (process.ExitCode is 0 or 1 && output.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(output));
                        var result = (CliResult)new DataContractJsonSerializer(typeof(CliResult)).ReadObject(stream);
                        result.Files ??= [];
                        result.Diagnostics ??= [];
                        return result;
                    }
                    catch (SerializationException ex)
                    {
                        return CliResult.Fatal($"Unexpected generator output: {ex.Message}{Environment.NewLine}{output}");
                    }
                }

                return CliResult.Fatal(errors.Length > 0 ? errors : $"The generator exited with code {process.ExitCode}.{Environment.NewLine}{output}");
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        /// <summary>Windows command-line quoting: inner quotes escaped, trailing backslashes doubled so they cannot escape the closing quote.</summary>
        private static string Quote(string value)
        {
            var escaped = value.Replace("\"", "\\\"");
            var trailingBackslashes = escaped.Length - escaped.TrimEnd('\\').Length;
            return "\"" + escaped + new string('\\', trailingBackslashes) + "\"";
        }
    }
}
