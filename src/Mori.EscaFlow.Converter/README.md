# Mori.EscaFlow.Converter

The language-neutral converter layer every `Mori.EscaFlow.Converter.<Language>` builds on:

- `IGenerator`, `GenerationResult`, `GeneratedFile` — what a converter produces.
- `GenerationReport` — alias and naming tallies, unsupported services, layout notes, instruction inventory, messages.
- `NameMapper` — identifier mapping per scope (keywords, case-only duplicates, reserved names, case-insensitive sources).
- `Layout/LogixLayoutCalculator` — the Logix byte layout of every structure and add-on backing structure, so a target can reproduce the controller's tag image.
- `CodeWriter` — indented text builder.
