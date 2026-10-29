using ModelScaffolder.Core;

namespace ModelScaffolder.Core.Tests;

/// <summary>Runs the real generator (dry run by default) on an in-memory model.</summary>
internal static class Scaffold
{
    public const string Category = """
        { "name": "Category", "plural": "Categories", "properties": [
            { "name": "Id", "type": "int", "key": true, "generated": true },
            { "name": "Name", "type": "string", "required": true, "maxLength": 100, "unique": true } ] }
        """;

    public const string Product = """
        { "name": "Product", "properties": [
            { "name": "Id", "type": "int", "key": true, "generated": true },
            { "name": "Name", "type": "string", "required": true, "maxLength": 200 } ] }
        """;

    /// <summary>A one-schema model ("Shop" / "Sales") with the given entity objects and relationship objects.</summary>
    public static string ModelJson(string[] entities, params string[] relationships) => $$"""
        {
          "version": 1,
          "project": { "name": "Shop" },
          "schemas": [
            {
              "name": "Sales",
              "entities": [ {{string.Join(",\n", entities)}} ],
              "relationships": [ {{string.Join(",\n", relationships)}} ]
            }
          ]
        }
        """;

    /// <summary>An entity with an int key plus the given property objects.</summary>
    public static string Entity(string name, params string[] properties) => $$"""
        { "name": "{{name}}", "properties": [
            { "name": "Id", "type": "int", "key": true, "generated": true }{{string.Concat(properties.Select(p => ",\n    " + p))}} ] }
        """;

    public static ScaffoldResult Run(string json, bool dryRun = true, string? outputDirectory = null)
    {
        var folder = Directory.CreateTempSubdirectory("modelscaffold-tests-").FullName;
        var modelPath = Path.Combine(folder, "test.model.json");
        File.WriteAllText(modelPath, json);
        return RunFile(modelPath, dryRun, outputDirectory ?? Path.Combine(folder, "out"));
    }

    /// <summary>Runs an existing model file (e.g. one of the samples).</summary>
    public static ScaffoldResult RunFile(string modelPath, bool dryRun = true, string? outputDirectory = null)
    {
        var options = new ScaffoldOptions
        {
            ModelPath = modelPath,
            OutputDirectory = outputDirectory ?? Directory.CreateTempSubdirectory("modelscaffold-tests-").FullName,
            DryRun = dryRun
        };
        options.Settings.SystemUserName = "admin";
        options.Settings.SystemUserPasswordHash = "PBKDF2-SHA256$1$c2FsdA==$aGFzaA==";
        return Scaffolder.Run(options);
    }

    public static Diagnostic Error(this ScaffoldResult result, string code)
    {
        Assert.False(result.Succeeded, $"Expected error {code}, but generation succeeded.");
        var match = result.Diagnostics.FirstOrDefault(d => d.Code == code && d.Severity == Severity.Error);
        Assert.True(match is not null, $"Expected error {code}; got:\n" + string.Join("\n", result.Diagnostics));
        return match!;
    }

    public static void Succeeds(this ScaffoldResult result) =>
        Assert.True(result.Succeeded, "Generation failed:\n" + string.Join("\n", result.Diagnostics));

    public static string GeneratedFile(this ScaffoldResult result, string fileName) =>
        result.Files.Single(f => f.File.RelativePath.EndsWith("/" + fileName, StringComparison.Ordinal) || f.File.RelativePath == fileName).File.Content;
}
