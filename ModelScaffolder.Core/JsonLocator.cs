using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ModelScaffolder.Core
{
    /// <summary>
    ///     Maps JSON paths used in diagnostics ("schemas[0].entities[1].properties[2].type") to 1-based line/column in the
    ///     model file, so the CLI and the Visual Studio Error List can point at the right place. Property names match
    ///     case-insensitively, like the model deserializer. A path that does not exist (e.g. a missing property) falls back
    ///     to its nearest existing parent.
    /// </summary>
    public sealed class JsonLocator
    {
        private readonly Dictionary<string, (int Line, int Column)> _positions;

        private JsonLocator(Dictionary<string, (int Line, int Column)> positions) => _positions = positions;

        /// <summary>Builds the path index. Never throws: invalid JSON yields a locator that finds nothing.</summary>
        public static JsonLocator Create(string json)
        {
            var positions = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Index(json, positions);
            }
            catch (JsonException)
            {
                // Syntax errors are reported by the deserializer, with their own line numbers.
                positions.Clear();
            }
            return new JsonLocator(positions);
        }

        /// <summary>Position of a model path, or of its nearest existing parent; null for paths outside the model.</summary>
        public (int Line, int Column)? Find(string path)
        {
            while (!string.IsNullOrEmpty(path))
            {
                if (_positions.TryGetValue(path, out var position))
                    return position;

                var cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
                if (cut <= 0)
                    return null;
                path = path.Substring(0, cut);
            }
            return null;
        }

        private static void Index(string json, Dictionary<string, (int, int)> positions)
        {
            var bytes = Encoding.UTF8.GetBytes(json.Length > 0 && json[0] == '﻿' ? json.Substring(1) : json);
            var lineStarts = new List<int> { 0 };
            for (var i = 0; i < bytes.Length; i++)
                if (bytes[i] == (byte)'\n')
                    lineStarts.Add(i + 1);

            (int, int) Position(long offset)
            {
                var line = lineStarts.BinarySearch((int)offset);
                if (line < 0)
                    line = ~line - 1;
                return (line + 1, (int)offset - lineStarts[line] + 1);
            }

            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            // One frame per open object/array: its path, and the current property (objects) or element index (arrays).
            var frames = new Stack<Frame>();

            string ValuePath()
            {
                if (frames.Count == 0)
                    return string.Empty;
                var top = frames.Peek();
                if (top.IsArray)
                    return $"{top.Path}[{++top.Index}]";
                return top.Path.Length == 0 ? top.Property : top.Path + "." + top.Property;
            }

            while (reader.Read())
            {
                var position = Position(reader.TokenStartIndex);
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                    {
                        var top = frames.Peek();
                        top.Property = reader.GetString() ?? string.Empty;
                        var path = top.Path.Length == 0 ? top.Property : top.Path + "." + top.Property;
                        if (!positions.ContainsKey(path))
                            positions[path] = position; // points at the property name
                        break;
                    }
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                    {
                        var inArray = frames.Count > 0 && frames.Peek().IsArray;
                        var path = ValuePath();
                        if (inArray)
                            positions[path] = position; // array elements have no property name; point at the element
                        frames.Push(new Frame(path, reader.TokenType == JsonTokenType.StartArray));
                        break;
                    }
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        frames.Pop();
                        break;
                    default:
                        if (frames.Count > 0 && frames.Peek().IsArray)
                            positions[ValuePath()] = position;
                        break;
                }
            }
        }

        private sealed class Frame
        {
            public Frame(string path, bool isArray)
            {
                Path = path;
                IsArray = isArray;
            }

            public string Path { get; }
            public bool IsArray { get; }
            public int Index { get; set; } = -1;
            public string Property { get; set; } = string.Empty;
        }
    }
}
