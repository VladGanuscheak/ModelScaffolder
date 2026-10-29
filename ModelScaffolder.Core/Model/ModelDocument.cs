using System.Collections.Generic;

namespace ModelScaffolder.Core.Model
{
    /// <summary>
    ///     The user-authored JSON model (source of truth). Describes the domain only;
    ///     generation choices (framework, database, ...) live in <see cref="GenerationSettings"/>.
    /// </summary>
    public sealed class ModelDocument
    {
        public int Version { get; set; }

        public ProjectDefinition Project { get; set; } = new ProjectDefinition();

        /// <summary>Each schema becomes its own vertical slice ({Schema}.Domain ... {Schema}.Api, {Schema}DbContext).</summary>
        public List<SchemaDefinition> Schemas { get; set; } = new List<SchemaDefinition>();
    }

    public sealed class SchemaDefinition
    {
        /// <summary>Slice name and root namespace, e.g. "Store", "Procurements". Also the database schema (lower-case).</summary>
        public string Name { get; set; } = string.Empty;

        public List<EntityDefinition> Entities { get; set; } = new List<EntityDefinition>();

        /// <summary>Relationships between entities of this schema. Cross-schema relationships are not supported.</summary>
        public List<RelationshipDefinition> Relationships { get; set; } = new List<RelationshipDefinition>();
    }

    public sealed class ProjectDefinition
    {
        /// <summary>Solution name and output folder, e.g. "Store". Also the default database name.</summary>
        public string Name { get; set; } = string.Empty;
    }

    public sealed class EntityDefinition
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional. Defaults to Name + "s".</summary>
        public string? Plural { get; set; }

        public List<PropertyDefinition> Properties { get; set; } = new List<PropertyDefinition>();
    }

    public sealed class PropertyDefinition
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>One of the scalar types listed in <see cref="ScalarTypes"/>.</summary>
        public string Type { get; set; } = string.Empty;

        public bool Key { get; set; }

        /// <summary>Value is produced by the database (identity / sequence). Only meaningful for keys.</summary>
        public bool Generated { get; set; }

        /// <summary>Required = non-nullable + validated. Keys are always required.</summary>
        public bool Required { get; set; }

        public bool Unique { get; set; }

        public int? MaxLength { get; set; }

        public int? MinLength { get; set; }

        public decimal? Minimum { get; set; }

        public decimal? Maximum { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }
    }

    public sealed class RelationshipDefinition
    {
        /// <summary>"many-to-one" (or "one-to-many", which is flipped). Others are not supported yet.</summary>
        public string Type { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public string To { get; set; } = string.Empty;

        /// <summary>Optional. Defaults to To + key name, e.g. "CategoryId".</summary>
        public string? ForeignKey { get; set; }

        /// <summary>Optional. Reference navigation on the dependent ("many") side. Defaults to To.</summary>
        public string? Navigation { get; set; }

        /// <summary>Optional. Collection navigation on the principal ("one") side. Omit for none.</summary>
        public string? InverseNavigation { get; set; }

        public bool Required { get; set; }

        /// <summary>EF Core DeleteBehavior name: Cascade, Restrict, SetNull, NoAction. Optional.</summary>
        public string? OnDelete { get; set; }
    }

    /// <summary>Generation choices that are not part of the domain model.</summary>
    public sealed class GenerationSettings
    {
        public string TargetFramework { get; set; } = "net10.0";

        public string ConnectionString { get; set; } =
            "Host=localhost;Port=5432;Database={name};Username=postgres;Password=postgres";

        /// <summary>HTTP port of the first schema's API; each further schema gets the next port.</summary>
        public int BasePort { get; set; } = 5080;

        /// <summary>Default system user that gets every permission. Asked for by the CLI when scaffolding.</summary>
        public string SystemUserName { get; set; } = string.Empty;

        /// <summary>PBKDF2 hash of the system user's password ("PBKDF2-SHA256$iterations$salt$hash"). Never the plain password.</summary>
        public string SystemUserPasswordHash { get; set; } = string.Empty;
    }
}
