using System.Text.RegularExpressions;

namespace ModelScaffolder.Core.Tests;

/// <summary>
///     Runs every file in the repository's samples folder. Each file states its expected outcome in its first line:
///     "expect: success" or "expect: MS0xx". Files without a marker (empty, binary) must fail with MS000.
/// </summary>
public class SampleFilesTests
{
    private static readonly string SamplesFolder = Path.Combine(AppContext.BaseDirectory, "samples");

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SamplesFolder, "*.model.*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(SamplesFolder, file).Replace('\\', '/'));
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_HasTheExpectedOutcome(string sample)
    {
        var path = Path.Combine(SamplesFolder, sample);
        var expected = Expectation(path);

        var result = Scaffold.RunFile(path);

        if (expected == "success")
        {
            result.Succeeds();
            Assert.NotEmpty(result.Files);
        }
        else
        {
            var error = result.Error(expected);
            // Model problems point at a line (syntax errors and validation alike); file-level problems may not.
            if (expected != "MS000" || File.ReadAllText(path).TrimStart().StartsWith("{", StringComparison.Ordinal))
                Assert.True(error.Line > 0, $"{expected} should point at a line in {sample}: {error}");
        }
    }

    [Fact]
    public void SamplesAreCopiedToTheTestOutput() =>
        Assert.True(Samples().Count >= 30, $"Expected the samples folder next to the tests: {SamplesFolder}");

    private static string Expectation(string path)
    {
        var match = Regex.Match(File.ReadAllText(path), @"expect:\s*(success|MS\d{3})");
        return match.Success ? match.Groups[1].Value : "MS000";
    }
}
