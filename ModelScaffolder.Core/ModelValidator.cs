using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ModelScaffolder.Core.Context;
using ModelScaffolder.Core.Model;

namespace ModelScaffolder.Core
{
    /// <summary>
    ///     Checks the user model before anything is generated. Never throws; reports diagnostics with JSON paths
    ///     (e.g. "schemas[0].entities[1].properties[2].type"), which Scaffolder turns into line numbers.
    /// </summary>
    public static class ModelValidator
    {
        public const int SupportedVersion = 1;

        /// <summary>Postgres numeric allows up to 1000 digits.</summary>
        private const int MaxPrecision = 1000;

        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        /// <summary>Contextual keywords that cannot (or should not) name a type or namespace, e.g. a type named "var" breaks every "var x = ...".</summary>
        private static readonly HashSet<string> TypeKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "var", "dynamic", "record", "file", "required", "scoped", "nameof", "value", "async", "await", "field",
            "global", "partial", "notnull", "unmanaged", "when", "with", "init", "managed", "extension", "allows"
        };

        /// <summary>Types the generated code uses unqualified (implicit usings, ASP.NET Core, RePolicies, generated helpers).</summary>
        private static readonly HashSet<string> ReservedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Task", "Program", "Results", "TypedResults", "Policy", "Guid", "DateTime", "DateTimeOffset", "DateOnly",
            "TimeOnly", "TimeSpan", "String", "Object", "Exception", "Type", "Action", "Func", "Expression", "File", "Path",
            "Directory", "Environment", "Console", "Math", "Convert", "Thread", "Timer", "Random", "HttpClient",
            "CancellationToken", "JsonSerializer", "DbContext", "DependencyInjection", "PolicyRegistration",
            "ValidationRules", "TestData", "ApiFactory", "Interlocked"
        };

        /// <summary>Root namespaces a schema or project must not shadow.</summary>
        private static readonly HashSet<string> ReservedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Microsoft", "GenericRepository", "ReValidator", "RePolicies", "Npgsql", "Testcontainers",
            "Xunit", "NSubstitute"
        };

        private static readonly HashSet<string> DeleteBehaviors = new HashSet<string>(StringComparer.Ordinal)
        {
            "Cascade", "Restrict", "SetNull", "NoAction", "ClientSetNull", "ClientCascade", "ClientNoAction"
        };

        public static void Validate(ModelDocument model, DiagnosticBag d)
        {
            // "null" in the JSON ("properties": null) arrives as null; treat missing lists as empty.
            model.Project ??= new ProjectDefinition();
            model.Schemas ??= new List<SchemaDefinition>();

            if (model.Version != SupportedVersion)
                d.Error("MS001", "version", $"Unsupported model version '{model.Version}'. Expected {SupportedVersion}.");

            if (string.IsNullOrWhiteSpace(model.Project.Name) || !model.Project.Name.Split('.').All(IsValidTypeName))
                d.Error("MS002", "project.name", $"'{model.Project.Name}' is not a valid C# namespace.");
            else if (ReservedNamespaces.Contains(model.Project.Name.Split('.')[0]) || ReservedTypeNames.Contains(model.Project.Name.Split('.')[0]))
                d.Error("MS009", "project.name", $"'{model.Project.Name}' would shadow the '{model.Project.Name.Split('.')[0]}' namespace used by the generated code.");

            if (model.Schemas.Count == 0)
                d.Error("MS003", "schemas", "At least one schema is required.");

            var schemaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var s = 0; s < model.Schemas.Count; s++)
            {
                var schema = model.Schemas[s];
                var schemaPath = $"schemas[{s}]";
                if (schema is null)
                {
                    d.Error("MS008", schemaPath, "Empty schema entry (null).");
                    continue;
                }

                if (!IsValidTypeName(schema.Name))
                    d.Error("MS004", schemaPath + ".name", $"'{schema.Name}' is not a valid C# identifier.");
                else if (ReservedNamespaces.Contains(schema.Name))
                    d.Error("MS009", schemaPath + ".name", $"Schema '{schema.Name}' would shadow the '{schema.Name}' namespace used by the generated code.");
                else if (ReservedTypeNames.Contains(schema.Name))
                    d.Error("MS009", schemaPath + ".name", $"Schema '{schema.Name}' becomes a namespace that hides the '{schema.Name}' type the generated code uses (e.g. System.IO.Directory). Choose another name.");
                else if (!schemaNames.Add(schema.Name))
                    d.Error("MS005", schemaPath + ".name", $"Duplicate schema '{schema.Name}' (names must differ by more than letter case: folders and database schemas would collide).");

                ValidateSchema(schema, schemaPath, d);
            }
        }

        private static void ValidateSchema(SchemaDefinition schema, string schemaPath, DiagnosticBag d)
        {
            schema.Entities ??= new List<EntityDefinition>();
            schema.Relationships ??= new List<RelationshipDefinition>();

            if (schema.Entities.Count == 0)
                d.Error("MS006", schemaPath + ".entities", "At least one entity is required.");

            var entities = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);
            var namesIgnoringCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plurals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // plural -> entity
            for (var i = 0; i < schema.Entities.Count; i++)
            {
                var entity = schema.Entities[i];
                var path = $"{schemaPath}.entities[{i}]";
                if (entity is null)
                {
                    d.Error("MS008", path, "Empty entity entry (null).");
                    continue;
                }

                entity.Properties ??= new List<PropertyDefinition>();

                if (!IsValidTypeName(entity.Name))
                    d.Error("MS010", path + ".name", $"'{entity.Name}' is not a valid C# type name.");
                else if (!namesIgnoringCase.Add(entity.Name))
                    d.Error("MS011", path + ".name", $"Duplicate entity '{entity.Name}' in schema '{schema.Name}' (names must differ by more than letter case: file names would collide on Windows).");
                else
                {
                    if (entity.Name == schema.Name)
                        d.Error("MS013", path + ".name", $"Entity '{entity.Name}' has the same name as its schema (namespace/type clash).");
                    else if (ReservedTypeNames.Contains(entity.Name))
                        d.Error("MS016", path + ".name", $"'{entity.Name}' conflicts with a type the generated code uses (e.g. System.Threading.Tasks.Task). Choose another name.");
                    entities[entity.Name] = entity; // still known to relationships, so they don't report it as unknown
                }

                if (entity.Plural != null && !IsValidTypeName(entity.Plural))
                    d.Error("MS012", path + ".plural", $"'{entity.Plural}' is not a valid C# identifier.");
                else if (IsValidTypeName(entity.Name))
                {
                    // The plural names a namespace ({Schema}.Application.{Plural}), the route and the DbSet.
                    var plural = string.IsNullOrEmpty(entity.Plural) ? Naming.Pluralize(entity.Name) : entity.Plural!;
                    if (string.Equals(plural, entity.Name, StringComparison.OrdinalIgnoreCase))
                        d.Error("MS014", path + ".plural", $"The plural '{plural}' must differ from the entity name (it names the {schema.Name}.Application.{plural} namespace).");
                    else if (plurals.TryGetValue(plural, out var other))
                        d.Error("MS015", path + ".plural", $"Entities '{other}' and '{entity.Name}' have the same plural '{plural}' (namespace, route and DbSet would collide).");
                    else
                        plurals[plural] = entity.Name;
                }

                ValidateProperties(entity, path, d);
            }

            // A plural must not equal another entity's name ("Products" entity next to "Product"), and generated
            // per-entity types ("ProductService", ...) must not equal an entity name.
            for (var i = 0; i < schema.Entities.Count; i++)
            {
                var entity = schema.Entities[i];
                if (entity is null || !entities.ContainsKey(entity.Name))
                    continue;

                if (plurals.Keys.FirstOrDefault(p => string.Equals(p, entity.Name, StringComparison.OrdinalIgnoreCase)) is string clash &&
                    plurals[clash] != entity.Name)
                    d.Error("MS015", $"{schemaPath}.entities[{i}].name", $"'{entity.Name}' equals the plural of '{plurals[clash]}' (namespace/type clash). Set a different \"plural\" there.");

                foreach (var other in entities.Keys.Where(o => o != entity.Name))
                    if (GeneratedTypeNames(other).Contains(entity.Name))
                        d.Error("MS017", $"{schemaPath}.entities[{i}].name", $"'{entity.Name}' clashes with a type generated for '{other}'. Choose another name.");
            }

            var foreignKeys = new Dictionary<string, string>(StringComparer.Ordinal); // "Dependent.Fk" -> relationship path
            for (var i = 0; i < schema.Relationships.Count; i++)
            {
                var path = $"{schemaPath}.relationships[{i}]";
                if (schema.Relationships[i] is null)
                    d.Error("MS008", path, "Empty relationship entry (null).");
                else
                    ValidateRelationship(schema.Relationships[i], path, entities, foreignKeys, d);
            }
        }

        private static void ValidateProperties(EntityDefinition entity, string path, DiagnosticBag d)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < entity.Properties.Count; j++)
            {
                var p = entity.Properties[j];
                var pp = $"{path}.properties[{j}]";
                if (p is null)
                {
                    d.Error("MS008", pp, "Empty property entry (null).");
                    continue;
                }

                if (!IsValidName(p.Name))
                    d.Error("MS020", pp + ".name", $"'{p.Name}' is not a valid C# identifier.");
                else if (!names.Add(p.Name))
                    d.Error("MS021", pp + ".name", $"Duplicate property '{p.Name}' (names must differ by more than letter case: they share one camelCase JSON name).");
                else if (p.Name == entity.Name)
                    d.Error("MS022", pp + ".name", "A property cannot have the same name as its entity (CS0542).");

                if (!ScalarTypes.TryGet(p.Type, out var type))
                {
                    d.Error("MS023", pp + ".type", $"Unknown type '{p.Type}'. Supported: {string.Join(", ", ScalarTypes.Names)}.");
                    continue;
                }

                if (p.Key && type.RouteConstraint == null)
                    d.Error("MS024", pp + ".type", $"Type '{p.Type}' cannot be used as a key. Use int, long or Guid.");
                if (p.Generated && !p.Key)
                    d.Warning("MS025", pp + ".generated", "'generated' is only applied to keys; ignored.");
                if ((p.MaxLength.HasValue || p.MinLength.HasValue) && p.Type != "string")
                    d.Error("MS026", pp, "maxLength/minLength are only valid for string properties.");
                if (p.MaxLength < 1)
                    d.Error("MS035", pp + ".maxLength", "maxLength must be at least 1.");
                if (p.MinLength < 0)
                    d.Error("MS036", pp + ".minLength", "minLength cannot be negative.");
                if (p.MinLength > p.MaxLength)
                    d.Error("MS027", pp, "minLength is greater than maxLength.");
                if ((p.Minimum.HasValue || p.Maximum.HasValue) && !type.IsNumeric)
                    d.Error("MS028", pp, "minimum/maximum are only valid for numeric properties.");
                if (p.Minimum > p.Maximum)
                    d.Error("MS029", pp, "minimum is greater than maximum.");
                else if (p.Type is "int" or "long" or "short" && p.Minimum.HasValue && p.Maximum.HasValue &&
                         Math.Ceiling(p.Minimum.Value) > Math.Floor(p.Maximum.Value))
                    d.Error("MS034", pp, $"No whole number lies between minimum {p.Minimum} and maximum {p.Maximum}.");
                if ((p.Precision.HasValue || p.Scale.HasValue) && p.Type != "decimal")
                    d.Error("MS030", pp, "precision/scale are only valid for decimal properties.");
                if (p.Precision is int precision && (precision < 1 || precision > MaxPrecision))
                    d.Error("MS037", pp + ".precision", $"precision must be between 1 and {MaxPrecision}.");
                if (p.Scale is int scale && (scale < 0 || scale > (p.Precision ?? 18)))
                    d.Error("MS038", pp + ".scale", $"scale must be between 0 and the precision ({p.Precision ?? 18}).");
                if (p.Unique && p.Type == "bool")
                    d.Warning("MS033", pp + ".unique", "A unique bool allows at most two rows.");
            }

            var keys = entity.Properties.Count(p => p is { Key: true });
            if (keys == 0)
                d.Error("MS031", path, $"Entity '{entity.Name}' has no key property.");
            else if (keys > 1)
                d.Error("MS032", path, $"Entity '{entity.Name}' has a composite key; not supported yet.");
        }

        private static void ValidateRelationship(RelationshipDefinition r, string path, Dictionary<string, EntityDefinition> entities,
            Dictionary<string, string> foreignKeys, DiagnosticBag d)
        {
            if (r.Type != "many-to-one" && r.Type != "one-to-many")
            {
                d.Error("MS040", path + ".type", $"Relationship type '{r.Type}' is not supported yet. Use many-to-one.");
                return;
            }

            if (!entities.ContainsKey(r.From ?? string.Empty))
                d.Error("MS041", path + ".from", $"Unknown entity '{r.From}' (relationships must stay within one schema).");
            if (!entities.ContainsKey(r.To ?? string.Empty))
                d.Error("MS042", path + ".to", $"Unknown entity '{r.To}' (relationships must stay within one schema).");
            if (r.OnDelete != null && !DeleteBehaviors.Contains(r.OnDelete))
                d.Error("MS043", path + ".onDelete", $"Unknown delete behavior '{r.OnDelete}'. Use one of: {string.Join(", ", DeleteBehaviors)}.");
            if (r.Required && r.OnDelete == "SetNull")
                d.Error("MS044", path + ".onDelete", "SetNull cannot be used with a required relationship.");

            foreach (var (name, field) in new[] { (r.ForeignKey, "foreignKey"), (r.Navigation, "navigation"), (r.InverseNavigation, "inverseNavigation") })
                if (name != null && !IsValidName(name))
                    d.Error("MS045", $"{path}.{field}", $"'{name}' is not a valid C# identifier.");

            // Foreign key checks need both ends.
            var (dependentName, principalName) = r.Type == "one-to-many" ? (r.To, r.From) : (r.From, r.To);
            if (!entities.TryGetValue(dependentName ?? string.Empty, out var dependent) ||
                !entities.TryGetValue(principalName ?? string.Empty, out var principal))
                return;

            var principalKey = principal.Properties.FirstOrDefault(p => p is { Key: true });
            if (principalKey is null)
                return; // MS031 already reported

            var fkName = r.ForeignKey ?? principal.Name + principalKey.Name;
            if (foreignKeys.TryGetValue(dependent.Name + "." + fkName, out var firstUse))
                d.Error("MS046", path + (r.ForeignKey != null ? ".foreignKey" : string.Empty),
                    $"'{dependent.Name}.{fkName}' is already the foreign key of {firstUse}. Give each relationship its own \"foreignKey\" (and \"navigation\").");
            else
                foreignKeys[dependent.Name + "." + fkName] = path;

            var declared = dependent.Properties.FirstOrDefault(p => p?.Name == fkName);
            if (declared is null)
                return;
            if (declared.Key)
                d.Error("MS047", path + ".foreignKey", $"The foreign key '{fkName}' cannot be the key of '{dependent.Name}' (shared-key one-to-one is not supported).");
            else if (declared.Required != r.Required)
                d.Warning("MS047", path + ".required",
                    $"The relationship is {(r.Required ? "required" : "optional")} but its foreign key property '{dependent.Name}.{fkName}' is {(declared.Required ? "required" : "optional")}; the property's setting wins.");
        }

        /// <summary>Types generated for an entity; another entity must not use these names.</summary>
        private static HashSet<string> GeneratedTypeNames(string entity) => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entity + "Service", "I" + entity + "Service", entity + "Configuration", entity + "Endpoints", entity + "Mappings",
            "Create" + entity + "Request", "Update" + entity + "Request", "Get" + entity + "Request", "Delete" + entity + "Request",
            entity + "Response", entity + "Permissions", entity + "ServiceTests", entity + "EndpointsTests"
        };

        private static bool IsValidName(string? name) =>
            !string.IsNullOrEmpty(name) && Identifier.IsMatch(name) && !Keywords.Contains(name!);

        private static bool IsValidTypeName(string? name) => IsValidName(name) && !TypeKeywords.Contains(name!);
    }
}
