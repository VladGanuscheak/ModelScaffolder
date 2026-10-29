using System.Collections.Generic;
using System.Linq;

namespace ModelScaffolder.Core
{
    public enum Severity
    {
        Warning,
        Error
    }

    /// <summary>A model or generation problem. <see cref="Path"/> points into the JSON, e.g. "entities[1].properties[2].type".</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(Severity severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code;
            Path = path;
            Message = message;
        }

        public Severity Severity { get; }
        public string Code { get; }
        public string Path { get; }
        public string Message { get; }

        /// <summary>1-based position in the model file, when the diagnostic points into the model.</summary>
        public int? Line { get; set; }

        public int? Column { get; set; }

        public override string ToString() =>
            $"{Severity.ToString().ToLowerInvariant()} {Code}: {Path}{(Line.HasValue ? $" (line {Line}, column {Column})" : string.Empty)}: {Message}";
    }

    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items => _items;

        public bool HasErrors => _items.Any(d => d.Severity == Severity.Error);

        public void Error(string code, string path, string message) => _items.Add(new Diagnostic(Severity.Error, code, path, message));

        public void Warning(string code, string path, string message) => _items.Add(new Diagnostic(Severity.Warning, code, path, message));
    }
}
