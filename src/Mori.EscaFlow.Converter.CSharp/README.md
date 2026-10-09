# Mori.EscaFlow.Converter.CSharp

Generates a complete C# solution from a middle file: `<Name>.slnx`, `<Name>.Logic` (structure classes,
add-on classes, program classes, the controller), `<Name>.Host` (console host with the web console and
the EtherNet/IP tag server), `README.md` and `generation-report.json`.

- `CSharpGenerator` — orchestration and the class shapes (see `docs/csharp-generator.md`).
- `CSharpTypes` — wrapper and class names, field types, construction expressions, layout literals.
- `CSharpExpressions` — typed references, literals, operators, conversions, bit access, edges, functions and services.
- `CSharpStatements` — assignments, calls (standard blocks, add-ons with InOut views, actions, services), control flow, labels.
- `CSharpGeneratorOptions` — paths to the runtime and host projects, EtherNet/IP port, strictness.
