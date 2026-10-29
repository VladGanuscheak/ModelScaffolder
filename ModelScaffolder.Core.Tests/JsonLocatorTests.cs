namespace ModelScaffolder.Core.Tests;

public class JsonLocatorTests
{
    private const string Json = """
        {
          // comment
          "version": 1,
          "Schemas": [
            {
              "name": "Sales",
              "entities": [
                { "name": "A" },
                { "name": "B", },
              ],
            }
          ]
        }
        """;

    private static readonly JsonLocator Locator = JsonLocator.Create(Json);

    [Fact]
    public void FindsProperties() => Assert.Equal<(int, int)?>((3, 3), Locator.Find("version"));

    [Fact]
    public void FindsNestedArrayElements_IgnoringCaseAndComments() => Assert.Equal<(int, int)?>((9, 9), Locator.Find("schemas[0].entities[1]"));

    [Fact]
    public void FindsPropertiesInsideArrayElements() => Assert.Equal<(int, int)?>((9, 11), Locator.Find("schemas[0].entities[1].name"));

    [Fact]
    public void MissingPath_FallsBackToItsParent() => Assert.Equal<(int, int)?>((8, 9), Locator.Find("schemas[0].entities[0].properties[3].type"));

    [Fact]
    public void PathOutsideTheModel_IsNotFound() => Assert.Null(Locator.Find("src/Sales/Program.cs"));

    [Fact]
    public void InvalidJson_FindsNothing() => Assert.Null(JsonLocator.Create("{ \"version\": ").Find("version"));
}
