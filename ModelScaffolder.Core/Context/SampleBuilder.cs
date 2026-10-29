using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ModelScaffolder.Core.Model;

namespace ModelScaffolder.Core.Context
{
    /// <summary>
    ///     Sample data for the generated tests, Postman collection and .http files: dependency order of entities and
    ///     values that satisfy the model's constraints. Bodies may contain Postman variables ({{categoryId}}, {{$timestamp}}),
    ///     so they are built as text rather than serialized.
    /// </summary>
    public static class SampleBuilder
    {
        private const string Timestamp = "{{$timestamp}}"; // Postman / .http dynamic variable, renders as 10 digits
        private const int TimestampLength = 10;

        public static void Apply(SchemaVm schema, IDictionary<string, EntityDefinition> defs, DiagnosticBag d)
        {
            // First: which references sample data can fill (bodies below depend on it).
            schema.EntitiesInDependencyOrder = DependencyOrder(schema, d);
            schema.EntitiesInReverseDependencyOrder = Enumerable.Reverse(schema.EntitiesInDependencyOrder).ToList();

            foreach (var entity in schema.Entities)
            {
                entity.IdVariable = entity.CamelName + "Id";
                var sortProperty = entity.Properties.FirstOrDefault(p => !p.IsKey && !p.IsForeignKey) ?? entity.Key;
                entity.SortSample = sortProperty.CamelName;
                entity.SortSampleName = sortProperty.Name;

                foreach (var p in entity.Properties)
                {
                    var def = defs[entity.Name].Properties.FirstOrDefault(x => x.Name == p.Name);
                    p.TestValue = CSharpValue(p, def, updated: false);
                    p.TestUpdatedValue = CSharpValue(p, def, updated: true);
                    p.ApiTestValue = ApiTestValue(p, def, p.TestValue);
                    p.ApiTestUpdatedValue = ApiTestValue(p, def, p.TestUpdatedValue);
                }
            }

            var byName = schema.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
            Func<EntityVm, string> postmanId = target => "{{" + target.IdVariable + "}}";
            Func<EntityVm, string> httpId = target => "{{create" + target.Name + ".response.body.$." + target.Key.CamelName + "}}";
            foreach (var entity in schema.Entities)
            {
                foreach (var reference in entity.References)
                    reference.TargetKey = byName[reference.Target].Key.Name;

                entity.SampleCreateBody = Body(entity, entity.CreateProperties, defs[entity.Name], byName, false, postmanId);
                entity.SampleUpdateBody = Body(entity, entity.UpdateProperties, defs[entity.Name], byName, true, postmanId);
                entity.HttpCreateBody = Body(entity, entity.CreateProperties, defs[entity.Name], byName, false, httpId);
                entity.HttpUpdateBody = Body(entity, entity.UpdateProperties, defs[entity.Name], byName, true, httpId);
                entity.MissingIdValue = entity.Key.Type switch
                {
                    "Guid" => "Guid.NewGuid()",
                    "long" => "long.MaxValue",
                    _ => "int.MaxValue"
                };
            }
        }

        /// <summary>
        ///     Parents before children, and which references sample data may fill:
        ///     <list type="bullet">
        ///         <item>Required references must not form a cycle (MS048): no row of such an entity could ever be inserted.</item>
        ///         <item>An optional reference is followed only if its target does not (transitively) require an entity that is
        ///         still being ordered; otherwise (self-references, optional back-references) it stays null in sample data.</item>
        ///     </list>
        /// </summary>
        private static List<EntityVm> DependencyOrder(SchemaVm schema, DiagnosticBag d)
        {
            var byName = schema.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);

            // 1. Required cycles (including required self-references).
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = visiting, 2 = done
            var path = new List<string>();

            void VisitRequired(EntityVm entity)
            {
                state[entity.Name] = 1;
                path.Add(entity.Name);
                foreach (var reference in entity.References.Where(r => r.IsRequired))
                {
                    state.TryGetValue(reference.Target, out var s);
                    if (s == 1)
                    {
                        var cycle = path.Skip(path.IndexOf(reference.Target)).Concat(new[] { reference.Target });
                        d.Error("MS048", reference.ModelPath,
                            $"Required relationships form a cycle ({string.Join(" -> ", cycle)}), so no row could ever be inserted. " +
                            "Make one of them optional (\"required\": false).");
                    }
                    else if (s == 0)
                    {
                        VisitRequired(byName[reference.Target]);
                    }
                }
                path.RemoveAt(path.Count - 1);
                state[entity.Name] = 2;
            }

            foreach (var entity in schema.Entities.Where(e => !state.ContainsKey(e.Name)))
                VisitRequired(entity);

            // 2. Order. requiredClosure[X] = X plus everything X requires, transitively.
            var requiredClosure = schema.Entities.ToDictionary(e => e.Name, e => RequiredClosure(e, byName), StringComparer.Ordinal);
            var ordered = new List<EntityVm>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);

            void Visit(EntityVm entity)
            {
                visiting.Add(entity.Name);
                foreach (var reference in entity.References.OrderByDescending(r => r.IsRequired))
                {
                    var target = reference.Target;
                    if (done.Contains(target) || visiting.Contains(target))
                        continue;
                    if (reference.IsRequired || !requiredClosure[target].Overlaps(visiting))
                        Visit(byName[target]);
                }
                visiting.Remove(entity.Name);
                done.Add(entity.Name);
                ordered.Add(entity);
            }

            foreach (var entity in schema.Entities.Where(e => !done.Contains(e.Name)))
                Visit(entity);

            // 3. A reference is filled in samples when its target is created earlier.
            var index = ordered.Select((e, i) => new { e.Name, i }).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
            foreach (var entity in schema.Entities)
            {
                foreach (var reference in entity.References)
                    reference.UseInSamples = reference.Target != entity.Name && index[reference.Target] < index[entity.Name];
                entity.SampleReferences = entity.References.Where(r => r.UseInSamples).ToList();
            }

            return ordered;
        }

        private static HashSet<string> RequiredClosure(EntityVm start, Dictionary<string, EntityVm> byName)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { start.Name };
            var queue = new Queue<EntityVm>(new[] { start });
            while (queue.Count > 0)
                foreach (var reference in queue.Dequeue().References.Where(r => r.IsRequired))
                    if (seen.Add(reference.Target))
                        queue.Enqueue(byName[reference.Target]);
            return seen;
        }

        private static string Body(EntityVm entity, List<PropertyVm> properties, EntityDefinition def,
            Dictionary<string, EntityVm> byName, bool updated, Func<EntityVm, string> idOf)
        {
            var sb = new StringBuilder("{\n");
            for (var i = 0; i < properties.Count; i++)
            {
                var p = properties[i];
                sb.Append("  \"").Append(p.CamelName).Append("\": ")
                  .Append(Value(entity, p, def.Properties.FirstOrDefault(x => x.Name == p.Name), byName, updated, idOf))
                  .Append(i < properties.Count - 1 ? ",\n" : "\n");
            }
            return sb.Append('}').ToString();
        }

        private static string Value(EntityVm entity, PropertyVm p, PropertyDefinition? def, Dictionary<string, EntityVm> byName, bool updated,
            Func<EntityVm, string> idOf)
        {
            if (p.IsForeignKey)
            {
                var reference = entity.References.First(r => r.ForeignKey == p.Name);
                if (!reference.UseInSamples)
                    return "null";
                var variable = idOf(byName[reference.Target]);
                return p.Type == "Guid" ? Quote(variable) : variable;
            }

            // Client-assigned keys and unique numbers must differ per run, or re-running the collection fails.
            if (MustDiffer(p) && TimestampFits(p, def))
                return Timestamp;

            switch (p.Type)
            {
                case "string":
                    return Quote(Text(p, def, updated));
                case "int":
                case "long":
                case "short":
                    return decimal.Truncate(Number(p, def, updated)).ToString(CultureInfo.InvariantCulture);
                case "decimal":
                case "double":
                case "float":
                    return Number(p, def, updated).ToString(CultureInfo.InvariantCulture);
                case "bool": return updated ? "false" : "true";
                case "Guid": return Quote("{{$guid}}");
                case "DateTime":
                case "DateTimeOffset": return Quote(updated ? "2026-02-01T12:00:00Z" : "2026-01-15T10:00:00Z");
                case "DateOnly": return Quote(updated ? "2026-02-01" : "2026-01-15");
                case "TimeOnly": return Quote(updated ? "12:00:00" : "10:00:00");
                default: return "null";
            }
        }

        private static bool MustDiffer(PropertyVm p) => p.IsUnique || (p.IsKey && !p.IsGenerated);

        /// <summary>A 10-digit timestamp fits ints (until 2038), longs, and unconstrained decimals/doubles.</summary>
        private static bool TimestampFits(PropertyVm p, PropertyDefinition? def)
        {
            if (def?.Maximum != null || def?.Minimum > 1_000_000_000m)
                return false;
            return p.Type switch
            {
                "int" or "long" or "double" or "float" => true,
                "decimal" => (p.Precision ?? 18) - (p.Scale ?? 2) >= TimestampLength,
                _ => false
            };
        }

        /// <summary>Sample number within minimum/maximum and, for decimals, within precision/scale.</summary>
        private static decimal Number(PropertyVm p, PropertyDefinition? def, bool updated)
        {
            var integer = p.Type is "int" or "long" or "short";
            var value = (def?.Minimum ?? 0) + (integer ? (updated ? 11 : 10) : (updated ? 19.99m : 9.99m));
            if (def?.Maximum is decimal max && value > max)
                value = max;

            if (p.IsDecimal)
            {
                var scale = p.Scale ?? 2;
                var precision = p.Precision ?? 18;
                value = Math.Round(value, Math.Min(scale, 28), MidpointRounding.AwayFromZero);
                if (precision - scale <= 27)
                {
                    var limit = Pow10(precision - scale) - Pow10(-scale);
                    if (value > limit)
                        value = limit;
                }
                if (def?.Minimum is decimal min && value < min)
                    value = Math.Ceiling(min * Pow10(scale)) / Pow10(scale);
            }

            return value;
        }

        private static decimal Pow10(int exponent)
        {
            var result = 1m;
            for (var i = 0; i < Math.Abs(Math.Min(exponent, 28)); i++)
                result = exponent > 0 ? result * 10 : result / 10;
            return result;
        }

        // Unique strings get a timestamp so the collection can be run repeatedly; lengths respect min/max.
        private static string Text(PropertyVm p, PropertyDefinition? def, bool updated)
        {
            var prefix = (updated ? "Updated " : "Sample ") + p.Name;
            var max = p.MaxLength ?? int.MaxValue;
            var min = def?.MinLength ?? 0;

            string text;
            if (p.IsUnique)
            {
                var room = max - TimestampLength - 1;
                text = room >= 1 ? prefix.Substring(0, Math.Min(prefix.Length, room)).TrimEnd() + " " + Timestamp
                     : max >= TimestampLength ? Timestamp
                     : new string('x', max);
            }
            else
            {
                text = prefix.Length > max ? prefix.Substring(0, max) : prefix;
            }

            var length = text.Replace(Timestamp, new string('0', TimestampLength)).Length;
            return length < min ? text + new string('x', min - length) : text;
        }

        // C# literals for generated unit tests. Keys and foreign keys get fixed ids.
        private static string CSharpValue(PropertyVm p, PropertyDefinition? def, bool updated)
        {
            if (p.IsKey || p.IsForeignKey)
            {
                if (p.Type == "Guid")
                    return p.IsKey ? "new Guid(\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\")" : "new Guid(\"6d2f1e8a-1b7c-4c55-9a51-2e0f4a7b9c10\")";
                return p.Type == "long" ? (p.IsKey ? "42L" : "7L") : p.IsKey ? "42" : "7";
            }

            switch (p.Type)
            {
                case "string":
                {
                    // Unique values get their run-specific suffix in the generated tests (TestData.Unique), not here;
                    // without the timestamp the text may be shorter than minLength again, so pad it.
                    var text = Text(p, def, updated).Replace(" " + Timestamp, string.Empty).Replace(Timestamp, "1234567890");
                    var minLength = Math.Min(def?.MinLength ?? 0, p.MaxLength ?? int.MaxValue);
                    if (text.Length < minLength)
                        text += new string('x', minLength - text.Length);
                    return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                }
                case "int":
                case "long":
                case "short":
                {
                    var text = decimal.Truncate(Number(p, def, updated)).ToString(CultureInfo.InvariantCulture);
                    return p.Type == "long" ? text + "L" : p.Type == "short" ? "(short)" + text : text;
                }
                case "decimal":
                case "double":
                case "float":
                {
                    var text = Number(p, def, updated).ToString(CultureInfo.InvariantCulture);
                    return text + (p.Type == "decimal" ? "m" : p.Type == "float" ? "f" : "d");
                }
                case "bool": return updated ? "false" : "true";
                case "Guid": return updated ? "new Guid(\"0b6a8c1e-2f3d-4e5f-8a9b-1c2d3e4f5a6b\")" : "new Guid(\"9e8d7c6b-5a49-4382-9170-6f5e4d3c2b1a\")";
                case "DateTime": return updated ? "new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)" : "new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)";
                case "DateTimeOffset": return updated ? "new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)" : "new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)";
                case "DateOnly": return updated ? "new DateOnly(2026, 2, 1)" : "new DateOnly(2026, 1, 15)";
                case "TimeOnly": return updated ? "new TimeOnly(12, 0)" : "new TimeOnly(10, 0)";
                default: return "default";
            }
        }

        /// <summary>
        ///     The API tests create many items in one database, so unique properties and client-assigned keys need a fresh
        ///     value per call (helpers in the generated TestData class). Everything else uses the plain literal.
        /// </summary>
        private static string ApiTestValue(PropertyVm p, PropertyDefinition? def, string literal)
        {
            if (p.IsForeignKey || !MustDiffer(p))
                return literal;

            switch (p.Type)
            {
                case "string":
                    return $"Unique({literal}, {(p.MaxLength.HasValue ? p.MaxLength.Value.ToString(CultureInfo.InvariantCulture) : "int.MaxValue")})";
                case "int":
                case "long":
                case "short":
                {
                    var sequence = IntegerSequence(def);
                    return sequence is null ? literal : p.Type == "short" ? $"(short)({sequence})" : sequence;
                }
                case "decimal":
                case "double":
                case "float":
                    // A growing number could break minimum/maximum or the decimal's precision.
                    return def?.Minimum != null || def?.Maximum != null || (p.IsDecimal && (p.Precision ?? 18) - (p.Scale ?? 2) < TimestampLength)
                        ? literal
                        : "NextNumber()";
                case "Guid": return "Guid.NewGuid()";
                case "DateOnly": return "new DateOnly(2026, 1, 1).AddDays(NextNumber())";
                case "DateTime": return "new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(NextNumber())";
                case "DateTimeOffset": return "new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(NextNumber())";
                case "TimeOnly": return "new TimeOnly(0, 0).Add(TimeSpan.FromSeconds(NextNumber()))";
                default: return literal; // bool: at most two distinct values anyway (warning MS033)
            }
        }

        /// <summary>Distinct integers within minimum/maximum, e.g. "1 + NextNumber() % 5"; null when the range has a single value.</summary>
        private static string? IntegerSequence(PropertyDefinition? def)
        {
            var min = def?.Minimum is decimal a ? (long?)Math.Ceiling(a) : null;
            var max = def?.Maximum is decimal b ? (long?)Math.Floor(b) : null;
            string Literal(long value) => value.ToString(CultureInfo.InvariantCulture);

            if (min is null && max is null) return "NextNumber()";
            if (max is null) return $"{Literal(min!.Value)} + NextNumber()";
            if (min is null) return $"{Literal(max.Value)} - NextNumber()";

            var span = max.Value - min.Value + 1;
            return span <= 1 ? null : $"{Literal(min.Value)} + NextNumber() % {Literal(span)}";
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
