# Sample models

Models for trying the generator (CLI or the Visual Studio extension) and for `ModelScaffolder.Core.Tests`,
which runs every file here (`SampleFilesTests`). The first line of each file states the expected outcome:
`// expect: success` or `// expect: MS0xx` (the diagnostic code). A file without a marker (empty, binary) must
fail with MS000.

| Folder | What it contains |
| --- | --- |
| `store.model.json` | The main sample: two schemas (Store, Procurements). |
| `valid/` | Models that must generate: minimal, blog (self-reference, optional relationships), hr (two schemas, client-assigned key, unique dates), inventory (every scalar type and constraint), JSONC with CRLF line endings. |
| `invalid/` | One problem per file: syntax errors, truncated/empty files, typos, wrong value types, nulls, unknown types, missing keys, names differing only in case, reserved names, plural clashes, impossible constraints, required cycles, shared foreign keys, unknown relationship targets. |
| `other-formats/` | Not JSON (YAML, XML, CSV, JSON5, binary) and JSON in other encodings (UTF-16, UTF-8 with BOM). |

In Visual Studio, right-click any `*.model.json` and choose **Scaffold Solution...**: invalid models list their
problems in the Error List (double-click jumps to the line). Files not named `*.model.json` (`.yaml`, `.xml`,
`.csv`) do not show the command at all.

From the command line:

```
dotnet run --project ModelScaffolder.Cli -- samples/invalid/required-cycle.model.json -o out/Cycle --dry-run
```
