using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using HandlebarsDotNet;
using ModelScaffolder.Core.Context;

namespace ModelScaffolder.Core.Templating
{
    /// <summary>manifest.json: which template produces which file. Adding a file = adding a template + one line here.</summary>
    public sealed class TemplateManifest
    {
        public List<TemplateEntry> Files { get; set; } = new List<TemplateEntry>();
    }

    public sealed class TemplateEntry
    {
        /// <summary>Template path relative to the templates folder.</summary>
        public string Template { get; set; } = string.Empty;

        /// <summary>Output path, itself a Handlebars template, e.g. "src/{{schema.name}}/{{schema.name}}.Domain/Entities/{{name}}.cs".</summary>
        public string Output { get; set; } = string.Empty;

        /// <summary>
        ///     "solution": once; root { project, schemas }.
        ///     "schema":   per schema; root { project, schemas, schema }.
        ///     "entity":   per entity; the entity's fields at the root, plus project and schema.
        /// </summary>
        public string Scope { get; set; } = "solution";

        /// <summary>
        ///     Optional extra root values, each itself a Handlebars template rendered against the scope first.
        ///     Lets one template produce several files, e.g. one policy file per action:
        ///     { "action": "Create", "model": "{{schema.namespace}}.Application.{{plural}}.Create{{name}}Request" }.
        /// </summary>
        public Dictionary<string, string>? Variables { get; set; }
    }

    public sealed class GeneratedFile
    {
        public GeneratedFile(string relativePath, string content)
        {
            RelativePath = relativePath;
            Content = content;
        }

        public string RelativePath { get; }
        public string Content { get; }
    }

    /// <summary>
    ///     Renders the template set. Templates get plain dictionaries/lists (camelCase), no HTML escaping.
    ///     Files in "Partials/" are registered as partials by file name, e.g. {{> header}}.
    ///     Helpers: {{json value}} writes a JSON literal (for .json outputs); {{pm "a" "b"}} writes a literal Postman variable {{ab}}.
    /// </summary>
    public static class TemplateRenderer
    {
        private static readonly JsonSerializerOptions ContextJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions LiteralJson = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions ManifestJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static List<GeneratedFile> Render(string templateRoot, SolutionContext context, DiagnosticBag d)
        {
            var files = new List<GeneratedFile>();
            var manifestPath = Path.Combine(templateRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                d.Error("MS060", manifestPath, "Template manifest not found.");
                return files;
            }

            var manifest = JsonSerializer.Deserialize<TemplateManifest>(File.ReadAllText(manifestPath), ManifestJson)!;
            var hb = CreateEnvironment(templateRoot);

            var root = (Dictionary<string, object?>)ToPlain(JsonSerializer.SerializeToElement(context, ContextJson))!;
            var schemas = ((List<object?>)root["schemas"]!).Cast<Dictionary<string, object?>>().ToList();

            foreach (var entry in manifest.Files)
            {
                var templatePath = Path.Combine(templateRoot, entry.Template);
                if (!File.Exists(templatePath))
                {
                    d.Error("MS061", entry.Template, "Template file not found.");
                    continue;
                }

                Func<object, string> body, output;
                var variables = new List<KeyValuePair<string, Func<object, string>>>();
                try
                {
                    var compiledBody = hb.Compile(File.ReadAllText(templatePath));
                    var compiledOutput = hb.Compile(entry.Output);
                    body = x => compiledBody(x, null);
                    output = x => compiledOutput(x, null);

                    foreach (var variable in entry.Variables ?? new Dictionary<string, string>())
                    {
                        var compiledVariable = hb.Compile(variable.Value);
                        variables.Add(new KeyValuePair<string, Func<object, string>>(variable.Key, x => compiledVariable(x, null)));
                    }
                }
                catch (Exception ex)
                {
                    d.Error("MS062", entry.Template, "Template does not compile: " + ex.Message);
                    continue;
                }

                IEnumerable<Dictionary<string, object?>> scopes = entry.Scope switch
                {
                    "solution" => new[] { root },
                    "schema" => schemas.Select(schema => new Dictionary<string, object?>
                    {
                        ["project"] = root["project"],
                        ["schemas"] = root["schemas"],
                        ["schema"] = schema
                    }),
                    "entity" => schemas.SelectMany(schema => ((List<object?>)schema["entities"]!)
                        .Cast<Dictionary<string, object?>>()
                        .Select(e => new Dictionary<string, object?>(e)
                        {
                            ["project"] = root["project"],
                            ["schema"] = schema
                        })),
                    _ => Array.Empty<Dictionary<string, object?>>()
                };
                if (entry.Scope != "solution" && entry.Scope != "schema" && entry.Scope != "entity")
                    d.Error("MS063", entry.Template, $"Unknown scope '{entry.Scope}'. Use 'solution', 'schema' or 'entity'.");

                foreach (var baseScope in scopes)
                {
                    try
                    {
                        var scope = new Dictionary<string, object?>(baseScope);
                        foreach (var variable in variables)
                            scope[variable.Key] = variable.Value(baseScope);

                        var file = new GeneratedFile(output(scope).Trim().Replace('\\', '/'), Tidy(body(scope)));
                        // "\{{" is Handlebars' escape for a literal "{{", so a Windows path like "..\{{x}}" is never rendered.
                        if (file.RelativePath.Contains("{{") || file.Content.Contains("{{schema.") || file.Content.Contains("{{name}}"))
                            d.Warning("MS066", file.RelativePath, "Output still contains unrendered '{{...}}'. Check for '\\{{' in the template; use '/' in paths.");
                        files.Add(file);
                    }
                    catch (Exception ex)
                    {
                        d.Error("MS064", entry.Template, "Template failed to render: " + ex.Message);
                    }
                }
            }

            foreach (var dup in files.GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                d.Error("MS065", dup.Key, "More than one template writes this path.");

            return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
        }

        private static IHandlebars CreateEnvironment(string templateRoot)
        {
            var hb = Handlebars.Create(new HandlebarsConfiguration { NoEscape = true });

            hb.RegisterHelper("json", (context, arguments) => JsonSerializer.Serialize(arguments[0], LiteralJson));

            // {{pm "category" "Id"}} -> {{categoryId}}: a literal Postman variable (Handlebars would otherwise consume the braces).
            hb.RegisterHelper("pm", (context, arguments) =>
            {
                var name = new System.Text.StringBuilder();
                for (var i = 0; i < arguments.Length; i++)
                    name.Append(arguments[i]);
                return "{{" + name + "}}";
            });

            var partials = Path.Combine(templateRoot, "Partials");
            if (Directory.Exists(partials))
                foreach (var file in Directory.GetFiles(partials, "*.hbs"))
                    hb.RegisterTemplate(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));

            return hb;
        }

        /// <summary>LF endings, no trailing spaces, at most one blank line in a row, one final newline. Keeps templates forgiving and output deterministic.</summary>
        private static string Tidy(string text)
        {
            text = text.Replace("\r\n", "\n");
            text = Regex.Replace(text, "[ \t]+\n", "\n");
            text = Regex.Replace(text, "\n{3,}", "\n\n");
            text = Regex.Replace(text, "\\{\n\n", "{\n");      // no blank line right after an opening brace
            text = Regex.Replace(text, "\n\n(\\s*})", "\n$1"); // ...or right before a closing one
            return text.Trim('\n') + "\n";
        }

        private static object? ToPlain(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToPlain(p.Value)),
            JsonValueKind.Array => e.EnumerateArray().Select(ToPlain).ToList(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : (object)e.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
