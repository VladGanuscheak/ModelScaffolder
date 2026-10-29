using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ModelScaffolder.Core.Model;

namespace ModelScaffolder.Core.Context
{
    /// <summary>Turns a validated <see cref="ModelDocument"/> into the <see cref="SolutionContext"/> templates render from.</summary>
    public static class ContextBuilder
    {
        public static SolutionContext Build(ModelDocument model, GenerationSettings settings, DiagnosticBag d)
        {
            var context = new SolutionContext
            {
                Project = new ProjectVm
                {
                    Name = model.Project.Name,
                    TargetFramework = settings.TargetFramework,
                    ConnectionString = settings.ConnectionString.Replace("{name}", model.Project.Name.ToLowerInvariant()),
                    SystemUserName = settings.SystemUserName,
                    SystemUserPasswordHash = settings.SystemUserPasswordHash
                }
            };

            for (var i = 0; i < model.Schemas.Count; i++)
                context.Schemas.Add(BuildSchema(model.Schemas[i], $"schemas[{i}]", settings.BasePort + i, d));

            return context;
        }

        private static SchemaVm BuildSchema(SchemaDefinition schema, string path, int port, DiagnosticBag d)
        {
            var vm = new SchemaVm
            {
                Name = schema.Name,
                Namespace = schema.Name,
                DbSchema = Naming.Snake(schema.Name),
                DbContext = schema.Name + "DbContext",
                Port = port,
                CamelName = Naming.Camel(schema.Name)
            };

            var byName = new Dictionary<string, EntityVm>(StringComparer.Ordinal);
            foreach (var entity in schema.Entities)
            {
                var entityVm = BuildEntity(entity);
                byName[entityVm.Name] = entityVm;
                vm.Entities.Add(entityVm);
            }

            for (var i = 0; i < schema.Relationships.Count; i++)
                ApplyRelationship(schema.Relationships[i], $"{path}.relationships[{i}]", byName, d);

            for (var i = 0; i < vm.Entities.Count; i++)
            {
                var entityVm = vm.Entities[i];
                var entityPath = $"{path}.entities[{i}]";
                CheckMemberClashes(entityVm, entityPath, d);
                if (entityVm.Name == vm.DbContext || entityVm.Plural == vm.Name)
                    d.Error("MS053", entityPath + ".name", "Entity name or plural clashes with a generated schema type/namespace.");
                entityVm.CreateProperties = entityVm.Properties.Where(p => !(p.IsKey && p.IsGenerated)).ToList();
                entityVm.UpdateProperties = entityVm.Properties.Where(p => !p.IsKey).ToList();
            }

            // Rules depend on FK info, so build them last.
            var defs = schema.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var entityVm in vm.Entities)
            {
                entityVm.CreateRules = ValidationRuleBuilder.Build(entityVm.CreateProperties, defs[entityVm.Name]);
                entityVm.UpdateRules = ValidationRuleBuilder.Build(entityVm.UpdateProperties, defs[entityVm.Name]);
            }

            SampleBuilder.Apply(vm, defs, d);
            return vm;
        }

        private static EntityVm BuildEntity(EntityDefinition e)
        {
            var plural = string.IsNullOrEmpty(e.Plural) ? Naming.Pluralize(e.Name) : e.Plural!;
            var vm = new EntityVm
            {
                Name = e.Name,
                Plural = plural,
                CamelName = Naming.Camel(e.Name),
                CamelPlural = Naming.Camel(plural),
                KebabName = Naming.Kebab(e.Name),
                Route = Naming.Kebab(plural),
                Properties = e.Properties.Select(BuildProperty).ToList()
            };
            vm.Key = vm.Properties.First(p => p.IsKey);
            vm.IdRoute = "/{id:" + vm.Key.RouteConstraint + "}";
            return vm;
        }

        private static PropertyVm BuildProperty(PropertyDefinition p)
        {
            ScalarTypes.TryGet(p.Type, out var t);
            var required = p.Required || p.Key;
            return new PropertyVm
            {
                Name = p.Name,
                CamelName = Naming.Camel(p.Name),
                Type = p.Type,
                ClrType = required ? t.ClrType : t.ClrType + "?",
                Initializer = required && !t.IsValueType ? " = string.Empty;" : string.Empty,
                IsString = p.Type == "string",
                IsDecimal = p.Type == "decimal",
                IsKey = p.Key,
                IsGenerated = p.Key && p.Generated,
                IsRequired = required,
                IsNullable = !required,
                IsUnique = p.Unique,
                MaxLength = p.MaxLength,
                Precision = p.Type == "decimal" ? p.Precision ?? 18 : (int?)null,
                Scale = p.Type == "decimal" ? p.Scale ?? 2 : (int?)null,
                RouteConstraint = p.Key ? t.RouteConstraint : null
            };
        }

        private static void ApplyRelationship(RelationshipDefinition r, string path, Dictionary<string, EntityVm> byName, DiagnosticBag d)
        {
            // Normalize to dependent ("many") -> principal ("one").
            var (dependentName, principalName) = r.Type == "one-to-many" ? (r.To, r.From) : (r.From, r.To);
            if (!byName.TryGetValue(dependentName, out var dependent) || !byName.TryGetValue(principalName, out var principal))
                return; // already reported by ModelValidator

            var fkName = r.ForeignKey ?? principal.Name + principal.Key.Name;
            var navigation = r.Navigation ?? principal.Name;
            var principalKeyType = principal.Key.Type;

            var fk = dependent.Properties.FirstOrDefault(p => p.Name == fkName);
            if (fk == null)
            {
                ScalarTypes.TryGet(principalKeyType, out var t);
                fk = new PropertyVm
                {
                    Name = fkName,
                    CamelName = Naming.Camel(fkName),
                    Type = principalKeyType,
                    ClrType = r.Required ? t.ClrType : t.ClrType + "?",
                    IsRequired = r.Required,
                    IsNullable = !r.Required
                };
                dependent.Properties.Add(fk);
            }
            else if (fk.Type != principalKeyType)
            {
                d.Error("MS051", path + ".foreignKey",
                    $"'{dependent.Name}.{fkName}' is '{fk.Type}' but '{principal.Name}.{principal.Key.Name}' is '{principalKeyType}'.");
            }
            fk.IsForeignKey = true;

            dependent.References.Add(new ReferenceVm
            {
                Navigation = navigation,
                Target = principal.Name,
                ForeignKey = fkName,
                IsRequired = r.Required,
                InverseNavigation = r.InverseNavigation,
                OnDelete = r.OnDelete,
                ModelPath = path
            });

            if (r.InverseNavigation != null)
                principal.Collections.Add(new CollectionVm { Navigation = r.InverseNavigation, Target = dependent.Name });
        }

        private static void CheckMemberClashes(EntityVm vm, string path, DiagnosticBag d)
        {
            var members = vm.Properties.Select(p => p.Name)
                .Concat(vm.References.Select(r => r.Navigation))
                .Concat(vm.Collections.Select(c => c.Navigation));

            // Case-insensitive: members differing only in case collide as camelCase JSON properties.
            foreach (var group in members.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                d.Error("MS050", path, $"Member '{group.Key}' of '{vm.Name}' is defined more than once (property/navigation clash, letter case ignored).");

            foreach (var name in members.Where(n => n == vm.Name))
                d.Error("MS052", path, $"Navigation '{name}' cannot have the same name as its entity '{vm.Name}' (CS0542). Set \"navigation\" on the relationship.");
        }
    }

    internal static class Naming
    {
        public static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        public static string Pluralize(string s)
        {
            if (s.EndsWith("y", StringComparison.Ordinal) && s.Length > 1 && "aeiou".IndexOf(char.ToLowerInvariant(s[s.Length - 2])) < 0)
                return s.Substring(0, s.Length - 1) + "ies";
            if (s.EndsWith("s", StringComparison.Ordinal) || s.EndsWith("x", StringComparison.Ordinal) ||
                s.EndsWith("ch", StringComparison.Ordinal) || s.EndsWith("sh", StringComparison.Ordinal))
                return s + "es";
            return s + "s";
        }

        public static string Kebab(string s) => Separate(s, '-');

        public static string Snake(string s) => Separate(s, '_');

        private static string Separate(string s, char separator)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && i > 0) sb.Append(separator);
                sb.Append(char.ToLowerInvariant(s[i]));
            }
            return sb.ToString();
        }
    }
}
