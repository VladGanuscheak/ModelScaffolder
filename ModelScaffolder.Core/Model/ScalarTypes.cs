using System;
using System.Collections.Generic;

namespace ModelScaffolder.Core.Model
{
    /// <summary>The scalar types a model property may use, and what the templates need to know about them.</summary>
    public static class ScalarTypes
    {
        public sealed class Info
        {
            public Info(string clrType, bool isValueType, bool isNumeric = false, string? routeConstraint = null)
            {
                ClrType = clrType;
                IsValueType = isValueType;
                IsNumeric = isNumeric;
                RouteConstraint = routeConstraint;
            }

            public string ClrType { get; }
            public bool IsValueType { get; }
            public bool IsNumeric { get; }

            /// <summary>Minimal API route constraint; null = type cannot be a key.</summary>
            public string? RouteConstraint { get; }
        }

        private static readonly Dictionary<string, Info> Map = new Dictionary<string, Info>(StringComparer.Ordinal)
        {
            ["string"] = new Info("string", isValueType: false),
            ["int"] = new Info("int", true, isNumeric: true, routeConstraint: "int"),
            ["long"] = new Info("long", true, isNumeric: true, routeConstraint: "long"),
            ["short"] = new Info("short", true, isNumeric: true),
            ["decimal"] = new Info("decimal", true, isNumeric: true),
            ["double"] = new Info("double", true, isNumeric: true),
            ["float"] = new Info("float", true, isNumeric: true),
            ["bool"] = new Info("bool", true),
            ["Guid"] = new Info("Guid", true, routeConstraint: "guid"),
            ["DateTime"] = new Info("DateTime", true),
            ["DateTimeOffset"] = new Info("DateTimeOffset", true),
            ["DateOnly"] = new Info("DateOnly", true),
            ["TimeOnly"] = new Info("TimeOnly", true),
        };

        public static IEnumerable<string> Names => Map.Keys;

        public static bool TryGet(string? type, out Info info)
        {
            if (type is null)
            {
                info = null!;
                return false;
            }
            return Map.TryGetValue(type, out info!);
        }
    }
}
