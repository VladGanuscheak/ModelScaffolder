namespace ModelScaffolder.Core.Tests;

/// <summary>Files that are not a usable model at all: every case must end in a diagnostic, never an exception.</summary>
public class InvalidJsonTests
{
    [Fact]
    public void SyntaxError_IsReportedWithItsLine()
    {
        var result = Scaffold.Run("""
            {
              "version": 1,
              "project": { "name": "Shop" },,
              "schemas": []
            }
            """);

        var error = result.Error("MS000");
        Assert.Equal(3, error.Line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{ \"version\": 1, ")]
    public void NotAModel_IsReported(string json) => Scaffold.Run(json).Error("MS000");

    [Fact]
    public void UnknownProperty_IsReportedWithItsLine()
    {
        var result = Scaffold.Run(Scaffold.ModelJson([Scaffold.Entity("Product", """{ "name": "Price", "type": "decimal", "requird": true }""")]));

        var error = result.Error("MS000");
        Assert.Contains("requird", error.Message);
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void WrongValueType_IsReported() =>
        Scaffold.Run(Scaffold.ModelJson([Scaffold.Entity("Product", """{ "name": "Stock", "type": "int", "required": "yes" }""")])).Error("MS000");

    [Fact]
    public void NumberTooLarge_IsReported() =>
        Scaffold.Run(Scaffold.ModelJson([Scaffold.Entity("Product", """{ "name": "Code", "type": "string", "maxLength": 99999999999 }""")])).Error("MS000");

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted() =>
        Scaffold.Run("""
            {
              // the model
              "version": 1,
              "project": { "name": "Shop", },
              "schemas": [
                { "name": "Sales", "entities": [ { "name": "Product", "properties": [ { "name": "Id", "type": "int", "key": true, "generated": true }, ] } ], },
              ],
            }
            """).Succeeds();

    [Fact]
    public void MissingFile_IsReported()
    {
        var options = new ScaffoldOptions { ModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".model.json"), OutputDirectory = Path.GetTempPath() };
        options.Settings.SystemUserName = "admin";
        options.Settings.SystemUserPasswordHash = "x";

        var result = Scaffolder.Run(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "MS000");
    }
}
