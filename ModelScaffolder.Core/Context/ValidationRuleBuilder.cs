using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModelScaffolder.Core.Model;

namespace ModelScaffolder.Core.Context
{
    /// <summary>
    ///     Derives ReValidator rules (System.Linq.Dynamic.Core expression strings) from model constraints.
    ///     "{PropertyName}" in messages is substituted by ReValidator at validation time.
    /// </summary>
    public static class ValidationRuleBuilder
    {
        public static List<RuleVm> Build(IEnumerable<PropertyVm> properties, EntityDefinition entity)
        {
            var rules = new List<RuleVm>();
            foreach (var p in properties)
            {
                var def = entity.Properties.FirstOrDefault(x => x.Name == p.Name); // null for synthesized foreign keys
                var x = "x." + p.Name;

                if (p.IsString)
                {
                    if (p.IsRequired)
                        rules.Add(Rule("Required", p, $"!string.IsNullOrWhiteSpace({x})", "{PropertyName} is required."));
                    if (def?.MinLength is int min)
                        rules.Add(Rule("MinLength", p, $"{x} == null || {x}.Length >= {min}", $"{{PropertyName}} must be at least {min} characters."));
                    if (p.MaxLength is int max)
                        rules.Add(Rule("MaxLength", p, $"{x} == null || {x}.Length <= {max}", $"{{PropertyName}} must be at most {max} characters."));
                    continue;
                }

                if (p.IsRequired && p.IsForeignKey)
                {
                    var check = p.Type == "Guid" ? $"{x} != Guid.Empty" : $"{x} > 0";
                    rules.Add(Rule("Required", p, check, "{PropertyName} is required."));
                }
                else if (p.IsRequired && p.Type == "Guid" && !p.IsKey)
                {
                    rules.Add(Rule("Required", p, $"{x} != Guid.Empty", "{PropertyName} is required."));
                }

                var orNull = p.IsNullable ? $"{x} == null || " : string.Empty;
                if (def?.Minimum is decimal minimum)
                    rules.Add(Rule("Minimum", p, $"{orNull}{x} >= {Literal(minimum, p.Type)}", $"{{PropertyName}} must be at least {Display(minimum)}."));
                if (def?.Maximum is decimal maximum)
                    rules.Add(Rule("Maximum", p, $"{orNull}{x} <= {Literal(maximum, p.Type)}", $"{{PropertyName}} must be at most {Display(maximum)}."));
            }
            return rules;
        }

        private static RuleVm Rule(string name, PropertyVm p, string body, string message) => new RuleVm
        {
            RuleName = name,
            PropertyName = p.Name,
            Expression = "x => " + body,
            ErrorMessage = message
        };

        private static string Display(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        // Dynamic LINQ needs a typed literal when the bound is fractional (0.5m for decimal, 0.5f for float).
        private static string Literal(decimal value, string type)
        {
            var text = Display(value);
            if (value == decimal.Truncate(value)) return text;
            return type == "decimal" ? text + "m" : type == "float" ? text + "f" : text;
        }
    }
}
