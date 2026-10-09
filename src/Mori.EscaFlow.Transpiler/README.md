# Mori.EscaFlow.Transpiler

The middle format shared by every importer and converter: plc-ir 2.0, a vendor-neutral, IEC 61131-3
based description of a PLC project with a `vendor` slot on every node. Contents:

- `Ir*.cs` — the model (document, configuration, types, variables, POUs, bodies, statements, expressions, diagrams, I/O, values).
- `IrJson` — reader/writer (camelCase, `kind`/`op` discriminators, strict mode), `IrWalker` — visitor over every body, statement and expression.
- `Printing/StPrinter` — renders bodies and POUs as IEC structured text.
- `Schema/` — JSON Schema 2020-12 generator and the committed `plc-ir-2.0.schema.json`.
- `Catalogs/` — atomic types, standard function blocks with pin profiles (`std-fbs.json`), standard functions, and the `IServiceCatalog` contract that vendor packages implement.

The format contract is `docs/plc-ir-2.0.md`. This package has no dependencies.
