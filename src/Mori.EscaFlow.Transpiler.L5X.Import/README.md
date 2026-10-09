# Mori.EscaFlow.Transpiler.L5X.Import

Builds the middle file (plc-ir 2.0) from a parsed L5X project.

- `LogixIrBuilder` — document, configuration, global lists, I/O devices and channels, POUs (programs and add-on instructions), prescan synthesis, SFC diagrams.
- `LogixTypeMapper` — Logix data types to neutral types with the `vendor.logix` details (bit hosts, hidden members, module types).
- `LogixExpressionBuilder` — operands and ST expressions to typed references and expressions; inserts the implicit Logix conversions.
- `LadderLowering` — rungs to neutral ladder elements plus lowered statements (the contract is in `docs/plc-ir-2.0.md`), including MCR zones, one-shots, jumps and the prescan form.
- `NetLowering` — FBD sheets to statements in dataflow order.
- `CallEmitter` / `SubroutineIndex` — add-on calls, JSR/SBR/RET binding, COP/FLL, FOR, and vendor service calls.
- `LogixInstructionMap` — the single table that classifies every ladder instruction.
