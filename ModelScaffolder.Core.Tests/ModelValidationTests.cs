using static ModelScaffolder.Core.Tests.Scaffold;

namespace ModelScaffolder.Core.Tests;

/// <summary>Valid JSON that describes a model the generator cannot (or should not) turn into code.</summary>
public class ModelValidationTests
{
    [Fact]
    public void ValidModel_Succeeds()
    {
        var result = Run(ModelJson([Category, Product], """
            { "type": "many-to-one", "from": "Product", "to": "Category", "inverseNavigation": "Products", "required": true }
            """));

        result.Succeeds();
        Assert.NotEmpty(result.Files);
    }

    // ---------- nulls and missing parts ----------

    [Fact]
    public void NullLists_AreReportedNotThrown()
    {
        var result = Run("""
            { "version": 1, "project": { "name": "Shop" },
              "schemas": [ { "name": "Sales", "entities": [ { "name": "Product", "properties": null } ], "relationships": null } ] }
            """);

        result.Error("MS031"); // no key
    }

    [Fact]
    public void NullEntry_IsReported() => Run(ModelJson(["null"])).Error("MS008");

    [Fact]
    public void NullPropertyType_IsReported() => Run(ModelJson([Entity("Product", """{ "name": "Price", "type": null }""")])).Error("MS023");

    [Fact]
    public void NullProject_IsReported() => Run("""{ "version": 1, "project": null, "schemas": [] }""").Error("MS002");

    [Fact]
    public void MissingSchemas_IsReported() => Run("""{ "version": 1, "project": { "name": "Shop" } }""").Error("MS003");

    [Fact]
    public void UnsupportedVersion_IsReported() => Run(ModelJson([Category]).Replace("\"version\": 1", "\"version\": 2")).Error("MS001");

    // ---------- names ----------

    [Fact]
    public void EntitiesDifferingOnlyInCase_AreReported() => Run(ModelJson([Entity("Product"), Entity("product")])).Error("MS011");

    [Fact]
    public void PropertiesDifferingOnlyInCase_AreReported() =>
        Run(ModelJson([Entity("Product", """{ "name": "Name", "type": "string" }""", """{ "name": "name", "type": "string" }""")])).Error("MS021");

    [Fact]
    public void SchemasDifferingOnlyInCase_AreReported()
    {
        var json = """
            { "version": 1, "project": { "name": "Shop" }, "schemas": [
              { "name": "Sales", "entities": [ { "name": "Product", "properties": [ { "name": "Id", "type": "int", "key": true } ] } ] },
              { "name": "sales", "entities": [ { "name": "Order", "properties": [ { "name": "Id", "type": "int", "key": true } ] } ] } ] }
            """;
        Run(json).Error("MS005");
    }

    [Fact]
    public void PluralEqualToName_IsReported() =>
        Run(ModelJson([Entity("Product").Replace("\"name\": \"Product\"", "\"name\": \"Product\", \"plural\": \"Product\"")])).Error("MS014");

    [Fact]
    public void SharedPlural_IsReported() =>
        Run(ModelJson([
            Entity("Person").Replace("\"name\": \"Person\"", "\"name\": \"Person\", \"plural\": \"People\""),
            Entity("Human").Replace("\"name\": \"Human\"", "\"name\": \"Human\", \"plural\": \"People\"")])).Error("MS015");

    [Fact]
    public void PluralEqualToAnotherEntity_IsReported() => Run(ModelJson([Entity("Product"), Entity("Products")])).Error("MS015");

    [Theory]
    [InlineData("Task")]
    [InlineData("Program")]
    [InlineData("Results")]
    public void ReservedEntityName_IsReported(string name) => Run(ModelJson([Entity(name)])).Error("MS016");

    [Fact]
    public void EntityNamedLikeAGeneratedType_IsReported() => Run(ModelJson([Entity("Product"), Entity("ProductService")])).Error("MS017");

    [Fact]
    public void ContextualKeywordAsEntityName_IsReported() => Run(ModelJson([Entity("var")])).Error("MS010");

    [Fact]
    public void SchemaShadowingSystem_IsReported() => Run(ModelJson([Category]).Replace("\"name\": \"Sales\"", "\"name\": \"System\"")).Error("MS009");

    [Fact]
    public void PropertyAndNavigationDifferingOnlyInCase_AreReported() =>
        Run(ModelJson([Category, Entity("Product", """{ "name": "category", "type": "string" }""")], """
            { "type": "many-to-one", "from": "Product", "to": "Category", "required": true }
            """)).Error("MS050");

    // ---------- property constraints ----------

    [Theory]
    [InlineData("""{ "name": "Code", "type": "string", "maxLength": 0 }""", "MS035")]
    [InlineData("""{ "name": "Code", "type": "string", "minLength": -1 }""", "MS036")]
    [InlineData("""{ "name": "Code", "type": "string", "minLength": 5, "maxLength": 3 }""", "MS027")]
    [InlineData("""{ "name": "Code", "type": "int", "maxLength": 3 }""", "MS026")]
    [InlineData("""{ "name": "Price", "type": "decimal", "precision": 0 }""", "MS037")]
    [InlineData("""{ "name": "Price", "type": "decimal", "precision": 3, "scale": 5 }""", "MS038")]
    [InlineData("""{ "name": "Price", "type": "decimal", "scale": -1 }""", "MS038")]
    [InlineData("""{ "name": "Level", "type": "int", "minimum": 2.2, "maximum": 2.5 }""", "MS034")]
    [InlineData("""{ "name": "Level", "type": "int", "minimum": 5, "maximum": 1 }""", "MS029")]
    [InlineData("""{ "name": "Active", "type": "bool", "minimum": 1 }""", "MS028")]
    [InlineData("""{ "name": "Active", "type": "Bool" }""", "MS023")]
    [InlineData("""{ "name": "Code", "type": "string", "key": true }""", "MS024")]
    public void InvalidPropertyConstraint_IsReported(string property, string code) => Run(ModelJson([Entity("Product", property)])).Error(code);

    [Fact]
    public void UniqueBool_IsOnlyAWarning()
    {
        var result = Run(ModelJson([Entity("Product", """{ "name": "Active", "type": "bool", "unique": true }""")]));

        result.Succeeds();
        Assert.Contains(result.Diagnostics, d => d.Code == "MS033" && d.Severity == Severity.Warning);
    }

    [Fact]
    public void CompositeKey_IsReported() =>
        Run(ModelJson([Entity("Product", """{ "name": "Code", "type": "int", "key": true }""")])).Error("MS032");

    // ---------- relationships ----------

    [Fact]
    public void RelationshipToUnknownEntity_IsReported() =>
        Run(ModelJson([Product], """{ "type": "many-to-one", "from": "Product", "to": "Category" }""")).Error("MS042");

    [Fact]
    public void UnsupportedRelationshipType_IsReported() =>
        Run(ModelJson([Category, Product], """{ "type": "many-to-many", "from": "Product", "to": "Category" }""")).Error("MS040");

    [Fact]
    public void SetNullOnRequiredRelationship_IsReported() =>
        Run(ModelJson([Category, Product], """{ "type": "many-to-one", "from": "Product", "to": "Category", "required": true, "onDelete": "SetNull" }""")).Error("MS044");

    [Fact]
    public void TwoRelationshipsSharingAForeignKey_AreReported() =>
        Run(ModelJson([Category, Product],
            """{ "type": "many-to-one", "from": "Product", "to": "Category", "navigation": "MainCategory" }""",
            """{ "type": "many-to-one", "from": "Product", "to": "Category", "navigation": "OtherCategory" }""")).Error("MS046");

    [Fact]
    public void ForeignKeyThatIsTheKey_IsReported() =>
        Run(ModelJson([Category, Product], """{ "type": "many-to-one", "from": "Product", "to": "Category", "foreignKey": "Id" }""")).Error("MS047");

    [Fact]
    public void RequiredSelfReference_IsReported() =>
        Run(ModelJson([Entity("Node")], """{ "type": "many-to-one", "from": "Node", "to": "Node", "foreignKey": "ParentId", "navigation": "Parent", "required": true }""")).Error("MS048");

    [Fact]
    public void RequiredCycle_IsReported() =>
        Run(ModelJson([Entity("Left"), Entity("Right")],
            """{ "type": "many-to-one", "from": "Left", "to": "Right", "required": true }""",
            """{ "type": "many-to-one", "from": "Right", "to": "Left", "required": true }""")).Error("MS048");

    [Fact]
    public void SelfReferenceWithDefaultNavigation_IsReported() =>
        // The navigation would default to "Node" inside class Node (CS0542).
        Run(ModelJson([Entity("Node")], """{ "type": "many-to-one", "from": "Node", "to": "Node", "foreignKey": "ParentId" }""")).Error("MS052");

    // ---------- locations ----------

    [Fact]
    public void Diagnostics_PointAtTheLineInTheModel()
    {
        var json = ModelJson([Category, Entity("Product", """{ "name": "Price", "type": "Decimal" }""")]);
        var expectedLine = json.Split('\n').ToList().FindIndex(l => l.Contains("\"Decimal\"")) + 1;

        var error = Run(json).Error("MS023");

        Assert.Equal(expectedLine, error.Line);
        Assert.Contains("(line " + expectedLine, error.ToString());
    }

    // ---------- output ----------

    [Fact]
    public void OutputFolderThatIsAFile_IsReported()
    {
        var file = Path.GetTempFileName();

        Run(ModelJson([Category]), dryRun: false, outputDirectory: file).Error("MS092");
    }

    [Fact]
    public void ExistingChangedFile_IsNotOverwrittenWithoutForce()
    {
        var output = Directory.CreateTempSubdirectory("modelscaffold-out-").FullName;
        var json = ModelJson([Category]);
        Run(json, dryRun: false, outputDirectory: output).Succeeds();

        var program = Directory.GetFiles(output, "Program.cs", SearchOption.AllDirectories).First();
        File.AppendAllText(program, "// my edit");

        Run(json, dryRun: false, outputDirectory: output).Error("MS090");
        Assert.EndsWith("// my edit", File.ReadAllText(program));
    }
}
