using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelScaffolder.Core.Context;
using ModelScaffolder.Core.Model;
using ModelScaffolder.Core.Output;
using ModelScaffolder.Core.Templating;

namespace ModelScaffolder.Core
{
    public sealed class ScaffoldOptions
    {
        public string ModelPath { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>Folder containing manifest.json. Defaults to the Templates folder shipped next to this assembly.</summary>
        public string? TemplateDirectory { get; set; }

        public GenerationSettings Settings { get; set; } = new GenerationSettings();

        /// <summary>Plan only; write nothing.</summary>
        public bool DryRun { get; set; }

        /// <summary>Overwrite existing files whose content differs.</summary>
        public bool Force { get; set; }
    }

    public sealed class ScaffoldResult
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = Array.Empty<Diagnostic>();
        public IReadOnlyList<PlannedFile> Files { get; set; } = Array.Empty<PlannedFile>();
        public bool Succeeded { get; set; }
    }

    /// <summary>Entry point: model file in, files out. Pure orchestration; no Visual Studio dependencies.</summary>
    public static class Scaffolder
    {
        private static readonly JsonSerializerOptions ModelJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow // catches typos such as "requird"
        };

        public static string DefaultTemplateDirectory =>
            Path.Combine(Path.GetDirectoryName(typeof(Scaffolder).Assembly.Location)!, "Templates");

        /// <summary>Never throws: every problem, including unexpected ones, ends up in <see cref="ScaffoldResult.Diagnostics"/>.</summary>
        public static ScaffoldResult Run(ScaffoldOptions options)
        {
            var d = new DiagnosticBag();
            var result = new ScaffoldResult();
            string? json = null;

            try
            {
                var model = Load(options.ModelPath, d, out json);
                if (model != null)
                    ModelValidator.Validate(model, d);

                if (string.IsNullOrWhiteSpace(options.Settings.SystemUserName) || string.IsNullOrWhiteSpace(options.Settings.SystemUserPasswordHash))
                    d.Error("MS007", "settings", "System user credentials are required (the CLI asks for them, or pass --admin-user/--admin-password).");

                var outputDirectory = FullPathOrNull(options.OutputDirectory);
                if (outputDirectory is null)
                    d.Error("MS092", options.OutputDirectory, "The output folder is not a valid path.");
                else if (File.Exists(outputDirectory))
                    d.Error("MS092", options.OutputDirectory, "The output folder is an existing file.");

                if (model != null && outputDirectory != null && !d.HasErrors)
                {
                    var context = ContextBuilder.Build(model, options.Settings, d);
                    if (!d.HasErrors)
                    {
                        var files = TemplateRenderer.Render(options.TemplateDirectory ?? DefaultTemplateDirectory, context, d);
                        if (!d.HasErrors)
                        {
                            var plan = FileWriter.Plan(outputDirectory, files);
                            result.Files = plan;
                            if (!options.DryRun)
                                FileWriter.Write(plan, options.Force, d);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // A bug in the generator must not crash the CLI or the Visual Studio extension.
                d.Error("MS099", "internal", $"Unexpected error, please report it: {ex}");
            }

            if (json != null)
            {
                var locator = JsonLocator.Create(json);
                foreach (var diagnostic in d.Items)
                {
                    if (diagnostic.Line.HasValue || !IsModelPath(diagnostic.Path))
                        continue;
                    var position = locator.Find(diagnostic.Path);
                    diagnostic.Line = position?.Line;
                    diagnostic.Column = position?.Column;
                }
            }

            result.Diagnostics = d.Items;
            result.Succeeded = !d.HasErrors;
            return result;
        }

        private static ModelDocument? Load(string path, DiagnosticBag d, out string? json)
        {
            json = null;
            if (!File.Exists(path))
            {
                d.Error("MS000", path, "Model file not found.");
                return null;
            }

            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                d.Error("MS000", path, $"Cannot read the model file: {ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                d.Error("MS000", "$", "The model file is empty.");
                return null;
            }

            try
            {
                var model = JsonSerializer.Deserialize<ModelDocument>(json, ModelJson);
                if (model is null)
                    d.Error("MS000", "$", "The model must be a JSON object, not null.");
                return model;
            }
            catch (JsonException ex)
            {
                d.Error("MS000", string.IsNullOrEmpty(ex.Path) ? "$" : ex.Path!, $"Invalid model JSON: {FirstSentence(ex.Message)}");
                var diagnostic = d.Items[d.Items.Count - 1];
                diagnostic.Line = (int?)ex.LineNumber + 1;
                diagnostic.Column = (int?)ex.BytePositionInLine + 1;
                return null;
            }
        }

        /// <summary>Diagnostics about the model use paths rooted at its top-level properties; others (files, settings) are not located.</summary>
        private static bool IsModelPath(string path) =>
            path == "version" || path.StartsWith("project", StringComparison.Ordinal) || path.StartsWith("schemas", StringComparison.Ordinal);

        private static string? FullPathOrNull(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        // System.Text.Json appends "Path: $.x | LineNumber: n | BytePositionInLine: m." - the position is reported separately.
        private static string FirstSentence(string message)
        {
            var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
            return cut > 0 ? message.Substring(0, cut) : message;
        }
    }
}
