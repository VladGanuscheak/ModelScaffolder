using static ModelScaffolder.Core.Tests.Scaffold;

namespace ModelScaffolder.Core.Tests;

/// <summary>Valid but unusual models: the generated tests, Postman collection and .http files must still work.</summary>
public class SampleDataTests
{
    [Fact]
    public void OptionalSelfReference_IsLeftNull_SoTestDataDoesNotRecurse()
    {
        var result = Run(ModelJson([Entity("Node", """{ "name": "Title", "type": "string", "required": true }""")],
            """{ "type": "many-to-one", "from": "Node", "to": "Node", "foreignKey": "ParentId", "navigation": "Parent", "inverseNavigation": "Children" }"""));

        result.Succeeds();
        var testData = result.GeneratedFile("TestData.cs");
        Assert.DoesNotContain("ParentId = (await CreateNodeAsync", testData);
        Assert.Contains("\\\"parentId\\\": null", result.GeneratedFile("Shop.postman_collection.json"));
    }

    [Fact]
    public void OptionalCycle_FillsOnlyOneDirection()
    {
        var result = Run(ModelJson([Entity("Left"), Entity("Right")],
            """{ "type": "many-to-one", "from": "Left", "to": "Right" }""",
            """{ "type": "many-to-one", "from": "Right", "to": "Left" }"""));

        result.Succeeds();
        var testData = result.GeneratedFile("TestData.cs");
        var leftUsesRight = testData.Contains("RightId = (await CreateRightAsync(client))");
        var rightUsesLeft = testData.Contains("LeftId = (await CreateLeftAsync(client))");
        Assert.True(leftUsesRight ^ rightUsesLeft, "Exactly one direction may create its parent first.");
    }

    [Fact]
    public void RequiredParent_IsCreatedFirst()
    {
        var result = Run(ModelJson([Product, Category], """{ "type": "many-to-one", "from": "Product", "to": "Category", "required": true }"""));

        result.Succeeds();
        var testData = result.GeneratedFile("TestData.cs");
        Assert.Contains("CategoryId = (await CreateCategoryAsync(client)).Id", testData);
        Assert.True(testData.IndexOf("---------- Category", StringComparison.Ordinal) < testData.IndexOf("---------- Product", StringComparison.Ordinal),
            "Parents are emitted (and created in Postman) before children.");
    }

    [Fact]
    public void ClientAssignedKey_GetsAFreshValuePerTestItem()
    {
        var result = Run(ModelJson(["""{ "name": "Tag", "properties": [ { "name": "Id", "type": "long", "key": true } ] }"""]));

        result.Succeeds();
        Assert.Contains("Id = NextNumber()", result.GeneratedFile("TestData.cs"));
        Assert.Contains("{{$timestamp}}", result.GeneratedFile("Shop.postman_collection.json"));
    }

    [Theory]
    [InlineData("""{ "name": "Number", "type": "int", "unique": true }""", "Number = NextNumber()")]
    [InlineData("""{ "name": "Number", "type": "int", "unique": true, "minimum": 1, "maximum": 100 }""", "Number = 1 + NextNumber() % 100")]
    [InlineData("""{ "name": "Level", "type": "short", "unique": true }""", "Level = (short)(NextNumber())")]
    [InlineData("""{ "name": "Token", "type": "Guid", "unique": true }""", "Token = Guid.NewGuid()")]
    [InlineData("""{ "name": "Day", "type": "DateOnly", "unique": true }""", "Day = new DateOnly(2026, 1, 1).AddDays(NextNumber())")]
    [InlineData("""{ "name": "Code", "type": "string", "unique": true, "maxLength": 8 }""", "Code = Unique(\"xxxxxxxx\", 8)")]
    public void UniqueValues_DifferPerTestItem(string property, string expected)
    {
        var result = Run(ModelJson([Entity("Product", property)]));

        result.Succeeds();
        Assert.Contains(expected, result.GeneratedFile("TestData.cs"));
    }

    [Theory]
    [InlineData("""{ "name": "Price", "type": "decimal", "precision": 3, "scale": 2, "minimum": 5 }""", "Price = 9.99m")]
    [InlineData("""{ "name": "Price", "type": "decimal", "precision": 4, "scale": 0 }""", "Price = 10m")]
    [InlineData("""{ "name": "Price", "type": "decimal", "precision": 3, "scale": 1, "minimum": 50 }""", "Price = 60.0m")]
    public void DecimalSamples_FitPrecisionAndScale(string property, string expected)
    {
        var result = Run(ModelJson([Entity("Product", property)]));

        result.Succeeds();
        Assert.Contains(expected, result.GeneratedFile("TestData.cs"));
    }

    [Fact]
    public void EntityWithOnlyAKey_Generates()
    {
        var result = Run(ModelJson(["""{ "name": "Marker", "properties": [ { "name": "Id", "type": "Guid", "key": true, "generated": true } ] }"""]));

        result.Succeeds();
        Assert.Contains("public sealed class UpdateMarkerRequest", result.GeneratedFile("MarkerDtos.cs"));
    }

    [Fact]
    public void AllScalarTypes_Generate()
    {
        var properties = new[] { "string", "int", "long", "short", "decimal", "double", "float", "bool", "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly" }
            .Select(type => $$"""{ "name": "{{char.ToUpperInvariant(type[0]) + type[1..]}}Value", "type": "{{type}}", "required": true }""")
            .ToArray();

        Run(ModelJson([Entity("Sample", properties)])).Succeeds();
    }
}
