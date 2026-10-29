using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelScaffolder.Core.Context
{
    // The data handed to templates. Serialized camelCase, so templates use {{name}}, {{schema.namespace}}, ...
    // Everything a template needs is precomputed here, so templates need no custom logic helpers.
    // This is the contract between C# and templates: change it deliberately.

    public sealed class SolutionContext
    {
        public ProjectVm Project { get; set; } = new ProjectVm();
        public List<SchemaVm> Schemas { get; set; } = new List<SchemaVm>();
    }

    public sealed class ProjectVm
    {
        public string Name { get; set; } = string.Empty;
        public string TargetFramework { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string SystemUserName { get; set; } = string.Empty;
        public string SystemUserPasswordHash { get; set; } = string.Empty;
    }

    /// <summary>One vertical slice: {Name}.Domain, .Infrastructure, .Validation, .Application, .Api.</summary>
    public sealed class SchemaVm
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Root namespace of the slice's projects, e.g. "Store" (Store.Domain.Entities, ...).</summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>Database schema name, e.g. "store", "purchase_orders".</summary>
        public string DbSchema { get; set; } = string.Empty;

        /// <summary>DbContext class name, e.g. "StoreDbContext".</summary>
        public string DbContext { get; set; } = string.Empty;

        /// <summary>HTTP port of this slice's API.</summary>
        public int Port { get; set; }

        public string CamelName { get; set; } = string.Empty;

        public List<EntityVm> Entities { get; set; } = new List<EntityVm>();

        /// <summary>Principals before dependents (create order). Used by the Postman collection.</summary>
        public List<EntityVm> EntitiesInDependencyOrder { get; set; } = new List<EntityVm>();

        /// <summary>Dependents before principals (delete order).</summary>
        public List<EntityVm> EntitiesInReverseDependencyOrder { get; set; } = new List<EntityVm>();
    }

    public sealed class EntityVm
    {
        public string Name { get; set; } = string.Empty;
        public string Plural { get; set; } = string.Empty;
        public string CamelName { get; set; } = string.Empty;
        public string CamelPlural { get; set; } = string.Empty;

        /// <summary>File-name form, e.g. "purchase-order" (used for "purchase-order-rules.json").</summary>
        public string KebabName { get; set; } = string.Empty;

        /// <summary>URL segment, e.g. "order-items".</summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>Route template for a single item, e.g. "/{id:int}". Precomputed because "}}}" confuses Handlebars.</summary>
        public string IdRoute { get; set; } = string.Empty;

        /// <summary>Postman variable holding the id of the created item, e.g. "categoryId".</summary>
        public string IdVariable { get; set; } = string.Empty;

        /// <summary>Sample JSON body for create/update requests (may contain Postman variables such as {{categoryId}}).</summary>
        public string SampleCreateBody { get; set; } = string.Empty;
        public string SampleUpdateBody { get; set; } = string.Empty;

        /// <summary>Same bodies for Visual Studio .http files (ids come from named requests, e.g. {{createCategory.response.body.$.id}}).</summary>
        public string HttpCreateBody { get; set; } = string.Empty;
        public string HttpUpdateBody { get; set; } = string.Empty;

        /// <summary>C# expression for an id that does not exist, e.g. "int.MaxValue", "Guid.NewGuid()".</summary>
        public string MissingIdValue { get; set; } = string.Empty;

        /// <summary>A property name to demonstrate sorting with (camelCase), and the same property's C# name.</summary>
        public string SortSample { get; set; } = string.Empty;
        public string SortSampleName { get; set; } = string.Empty;

        public PropertyVm Key { get; set; } = new PropertyVm();

        /// <summary>All scalar properties, including foreign keys, in declaration order.</summary>
        public List<PropertyVm> Properties { get; set; } = new List<PropertyVm>();

        /// <summary>Properties a client may send on create (excludes generated key).</summary>
        public List<PropertyVm> CreateProperties { get; set; } = new List<PropertyVm>();

        /// <summary>Properties a client may send on update (excludes key; the key comes from the route).</summary>
        public List<PropertyVm> UpdateProperties { get; set; } = new List<PropertyVm>();

        public List<ReferenceVm> References { get; set; } = new List<ReferenceVm>();

        /// <summary>
        ///     References that sample data (API tests, Postman, .http) fills by creating the parent first: required ones, and
        ///     optional ones whose target comes earlier in the dependency order. Self-references and optional back-references
        ///     stay null, otherwise creating sample data would recurse forever.
        /// </summary>
        public List<ReferenceVm> SampleReferences { get; set; } = new List<ReferenceVm>();
        public List<CollectionVm> Collections { get; set; } = new List<CollectionVm>();

        /// <summary>ReValidator rules for the create request.</summary>
        public List<RuleVm> CreateRules { get; set; } = new List<RuleVm>();

        /// <summary>ReValidator rules for the update request.</summary>
        public List<RuleVm> UpdateRules { get; set; } = new List<RuleVm>();
    }

    public sealed class PropertyVm
    {
        public string Name { get; set; } = string.Empty;
        public string CamelName { get; set; } = string.Empty;

        /// <summary>Model type, e.g. "string", "int".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>C# type including nullability, e.g. "string?", "int".</summary>
        public string ClrType { get; set; } = string.Empty;

        /// <summary>Text appended after "{ get; set; }", e.g. " = string.Empty;". Empty when none is needed.</summary>
        public string Initializer { get; set; } = string.Empty;

        public bool IsString { get; set; }
        public bool IsDecimal { get; set; }
        public bool IsKey { get; set; }
        public bool IsGenerated { get; set; }
        public bool IsRequired { get; set; }
        public bool IsNullable { get; set; }
        public bool IsForeignKey { get; set; }
        public bool IsUnique { get; set; }
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }

        /// <summary>C# literal for unit tests, e.g. "\"Sample Name\"", "9.99m", "new DateOnly(2026, 1, 15)".</summary>
        public string TestValue { get; set; } = string.Empty;

        /// <summary>A different C# literal, used to check that updates are applied.</summary>
        public string TestUpdatedValue { get; set; } = string.Empty;

        /// <summary>
        ///     C# expression for the API tests' TestData class. Same as <see cref="TestValue"/>, except that unique properties and
        ///     client-assigned keys get a fresh value per call (TestData.Unique / TestData.NextNumber / Guid.NewGuid()).
        /// </summary>
        public string ApiTestValue { get; set; } = string.Empty;

        public string ApiTestUpdatedValue { get; set; } = string.Empty;

        /// <summary>Minimal API route constraint for keys, e.g. "int", "guid".</summary>
        public string? RouteConstraint { get; set; }
    }

    /// <summary>A reference navigation on the dependent side (Product.Category).</summary>
    public sealed class ReferenceVm
    {
        public string Navigation { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;

        /// <summary>Key property of the target entity, e.g. "Id".</summary>
        public string TargetKey { get; set; } = string.Empty;
        public string ForeignKey { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string? InverseNavigation { get; set; }
        public string? OnDelete { get; set; }

        /// <summary>Whether sample data creates the target first and fills the foreign key (see <see cref="EntityVm.SampleReferences"/>).</summary>
        public bool UseInSamples { get; set; }

        /// <summary>JSON path of the relationship in the model, for diagnostics. Not visible to templates.</summary>
        [JsonIgnore]
        public string ModelPath { get; set; } = string.Empty;
    }

    /// <summary>A collection navigation on the principal side (Category.Products).</summary>
    public sealed class CollectionVm
    {
        public string Navigation { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    /// <summary>One ReValidator rule; maps 1:1 to ReValidator.SetUp.DynamicReconfiguration.</summary>
    public sealed class RuleVm
    {
        public string RuleName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
