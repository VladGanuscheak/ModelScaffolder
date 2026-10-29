# Templates

Everything the scaffolder generates comes from this folder. C# only loads the model, validates it,
precomputes the template context, and writes files.

- `manifest.json` lists every output file: `template`, `output` (a Handlebars path), `scope`.
  - `solution`: rendered once. Root: `{ project, schemas }`.
  - `schema`: rendered once per schema (slice). Root: `{ project, schemas, schema }`.
  - `entity`: rendered once per entity. Root: the entity's fields, plus `project` and `schema`.
- Optional `variables` on an entry add root values (each a Handlebars template rendered first), so one template can
  produce several files, e.g. `Policies/policy.json.hbs` renders one policy file per action.
- `Partials/*.hbs` are registered by file name: `{{> header}}`.
- Helpers: `{{json value}}` writes a JSON literal (use it in `.json` outputs); `{{pm "store" "BaseUrl"}}` writes a
  literal Postman variable `{{storeBaseUrl}}`.
- No HTML escaping. Output is tidied: LF endings, trailing spaces removed, at most one blank line in a row.
- Avoid `}}}` in templates (Handlebars reads it as a triple-stash). Use precomputed values such as `idRoute`.
- Use `/` in paths: `\{{` is Handlebars' escape for a literal `{{`, so `..\{{schema.name}}` is never rendered.

To customize without touching the shipped set: copy this folder, edit it, and run
`modelscaffold model.json -o out --templates <your-folder>`.

## Context fields

`project`: `name`, `targetFramework`, `connectionString`, `systemUserName`, `systemUserPasswordHash`

`schema`: `name`, `camelName`, `namespace`, `dbSchema`, `dbContext`, `port`, `entities`, `entitiesInDependencyOrder`,
`entitiesInReverseDependencyOrder`

Entity: `name`, `plural`, `camelName`, `camelPlural`, `kebabName`, `route`, `idRoute`, `key`, `properties`,
`createProperties`, `updateProperties`, `references`, `sampleReferences` (references sample data fills; never
self-references or optional back-references), `collections`, `createRules`, `updateRules`, `idVariable`,
`sampleCreateBody`, `sampleUpdateBody`, `httpCreateBody`, `httpUpdateBody`, `missingIdValue`, `sortSample`, `sortSampleName`

Property: `name`, `camelName`, `type`, `clrType`, `initializer`, `isString`, `isDecimal`, `isKey`, `isGenerated`,
`isRequired`, `isNullable`, `isForeignKey`, `isUnique`, `maxLength`, `precision`, `scale`, `routeConstraint`,
`testValue`, `testUpdatedValue` (C# literals for generated tests), `apiTestValue`, `apiTestUpdatedValue` (same, but a fresh
value per call for unique properties and client-assigned keys; for the API tests' TestData class)

Reference (dependent side): `navigation`, `target`, `targetKey`, `foreignKey`, `isRequired`, `inverseNavigation`, `onDelete`,
`useInSamples`

Collection (principal side): `navigation`, `target`

Rule (ReValidator): `ruleName`, `propertyName`, `expression`, `errorMessage`

The C# source of truth for these fields is `Context/TemplateContext.cs`.
