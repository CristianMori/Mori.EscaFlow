# Intermediate representation (middle file)

`L5x.Ir.Build` turns a parsed and linked L5X project into one JSON document, the *middle file*, that any
program can consume to generate code. It is produced with:

```
dotnet run -c Release --project src/L5x.Cli -- parse <file.L5X> --ir out.ir.json [--ir-no-values] [--ir-indent]
```

and read back in C# with `L5x.Ir.IrJson.ReadFile(path)`, an assembly with no dependency on the parser.
Any other language reads the JSON directly.

## Design rules

- **Everything is resolved.** Every tag reference carries the id of the declaring tag (after alias
  expansion), the leaf data type, whether it selects a bit and of which host type, and whether it is I/O.
  Routine calls and message targets are bound by id or name.
- **Everything is typed.** Expressions carry a `type` (atomic name or structure name); conditions are `BOOL`.
- **Nothing is invented.** Layouts of module I/O types come from the project; predefined structure
  members come from the Rockwell manuals; add-on backing types from the definitions. A type whose
  members are unknown is marked `opaque` and references into it carry `opaque: true`.
- **Source stays traceable.** Rungs keep their neutral text, ST bodies keep their lines, and the ladder
  is present both as drawn (`elements`) and lowered (`lowered`).
- **Versioned.** `irVersion` is `major.minor`; readers accept the same major.

## Top level

```json
{
  "irVersion": "1.0",
  "source":     { "file", "targetName", "targetType", "exportDate", "softwareRevision", "generator", "generatedAt" },
  "controller": { "name", "processorType", "majorRev", "minorRev", "description", "safetyLevel" },
  "types":      [ IrType ],
  "tags":       [ IrTag ],
  "modules":    [ IrModule ],
  "addOns":     [ IrAddOn ],
  "programs":   [ IrProgram ],
  "tasks":      [ IrTask ],
  "diagnostics":[ { "severity", "code", "message", "location" } ]
}
```

## Types

```json
{ "name": "TIMER", "kind": "struct", "class": "predefined", "opaque": false,
  "members": [ { "name": "PRE", "type": "DINT", "dims": [] },
               { "name": "EN", "type": "BOOL", "bitHost": "ZZZZZZZZZZTIMER0", "bitNumber": 31 } ] }
```

- `kind`: `atomic` (with `bitSize`), `struct`, `string` (with `stringLength`).
- `class`: `atomic`, `predefined`, `user`, `addon` (the backing structure of an add-on: parameters then
  locals), `module` (an I/O type such as `AB:1756_DI:I:0`, with `module: { vendor, product, direction,
  index, thirdParty }`), `unresolved`.
- Members list `dims` (UDT members are at most one-dimensional), `hidden` for bit hosts, `bitHost` and
  `bitNumber` for BOOL members overlaid on an integer, `radix` and `description`.
- Only types that are referenced (transitively through members) are emitted.

## Tags

```json
{ "id": "program/Cell/Timer1", "name": "Timer1", "scope": "program", "container": "Cell",
  "type": "TIMER", "dims": [], "constant": false, "description": "…",
  "value": { "kind": "struct", "type": "TIMER", "members": [ { "name": "PRE", "value": { "kind": "scalar", "type": "DINT", "int": 500 } } ] } }
```

- `id` is stable: `controller/<name>`, `program/<program>/<name>`, `aoi/<definition>/<name>`,
  `module/<module>/<I|O|C>`.
- `scope`: `controller`, `program`, `aoiParameter` (with `usage` input/output/inOut, `required`,
  `visible`), `aoiLocal`, `moduleIo`.
- `alias`: `{ path, tag, reference }` for alias tags; `reference` is a full `ref` expression.
- `comments`: per-member or per-element documentation keyed by operand specifier (`.Member`, `[3]`).
- `value`: initial value tree, `kind` = `scalar` (`int` for all integer kinds including BOOL, `real`,
  or `raw` text), `string` (`length`, `text`), `struct` (`members` in declaration order), `array`
  (`elementType`, `dims`, row-major `elements`). Omitted with `--ir-no-values`.
- `message`: MESSAGE configuration with decoded service/class/instance/attribute and bound
  `localElementTag` / `destinationTagId`. Messages to the Socket Object (class 16#0342) carry
  `socketService` (`createSocket`, `openConnection`, `readSocket`, `writeSocket`, `deleteSocket`,
  `acceptConnection`, `deleteAllSockets`, `joinMulticastAddress`, `dropMulticastAddress`, `clearLog`);
  the socket instance is `targetObject`, and the logic normally rewrites it at run time through the
  message tag's `.Instance` member (a `ref` with `tag` = the message tag), as it does `.Path`.
  `socket` is the structural binding a backend needs to implement the Socket Object dynamically:
  `session` (the tag holding the request structures, shared by all services of one socket), `request`
  and `response` (typed `ref`s to the structures the message sends and receives), `instance` (a `ref` to
  the message tag's `Instance` member, the socket handle written by logic after CreateSocket), and
  `fields`, a map of role to `ref` into the request structure: CreateSocket `type` (1 = TCP stream,
  2 = UDP datagram), `timeout`, `localFamily`, `localPort`, `localAddress` or `localAddressOctet1..4`;
  OpenConnection `timeout`, `destination` (string `host?port=n` or `?port=n&address=a.b.c.d`);
  ReadSocket `timeout`, `bufferLength`; WriteSocket `timeout`, `buffer`, `toPort`,
  `toAddressOctet1..4` / `toAddress`. These are locations, not values: the protocol, ports, addresses
  and buffers are whatever the logic last wrote when the MSG executes, so the generated code reads them
  at that moment. Initial values, when the export has them, are on the tags as usual.
- `produced` / `consumed`: produced and consumed tag settings.
- `io`: true for module I/O data and for tags of module-defined type.

## Modules, add-ons, programs, tasks

- `modules[]`: catalog, vendor, parent, ports, connections with `inputTag` / `outputTag` ids and
  `configTag`. The module data tags themselves are in `tags` with scope `moduleIo`.
- `addOns[]`: `parameters` and `locals` as tag ids (declaration order), `routines[]` (Logic, Prescan,
  Postscan, EnableInFalse), execution flags.
- `programs[]`: `mainRoutine`, `faultRoutine`, `task`, `class`, `disabled`, `folder` / `parentFolder` /
  `children`, `tags` ids, `routines[]`.
- `tasks[]`: `type` continuous/periodic/event, `rateMs`, `priority`, `watchdogMs`, `programs` in scan
  order.

## Routines

```json
{ "name": "MainRoutine", "language": "ladder", "calls": ["A00_PARAMETERS"], "ladder": { "rungs": [ … ] } }
```

`language` is `ladder`, `st`, `sfc`, `fbd` or `encrypted` (source-protected: no body).

### Expressions (`op`)

| op | fields | meaning |
|---|---|---|
| `ref` | `path`, `tag`, `expanded`, `module`, `segments[]`, `type`, `dims`, `bit`, `bitNumber`, `bitHost`, `io`, `opaque`, `error` | tag reference; `segments` are `member { name }`, `index { indices[] }`, `bit { bit }` or `bit { index }` |
| `lit` | `kind` int/real/string/bool, `int`, `real`, `string`, `bool`, `radix`, `type` | constant |
| `un` | `operator` neg/not, `operand` | unary |
| `bin` | `operator` add sub mul div mod pow eq ne lt gt le ge and or xor, `left`, `right` | binary |
| `fn` | `name`, `addOn`, `args[]` | function call in an expression (ABS, SQRT…) or a boolean-valued ladder instruction (LIM, MEQ, ONS, OSR, OSF, DTR) |
| `keyword` | `text`, `kind` keyword/routine/label | enumeration operand, routine name or label |
| `placeholder` | `empty` | `?` operand or elided argument |
| `error` | `text` | text the parser could not interpret |

### Statements (`kind`)

`assign { target, value, nonRetentive }`, `call { name, addOn, args[], enable }`, `if { branches[{condition, body}], else }`,
`case { selector, arms[{labels[{from,to}], body}], else }`, `for { variable, from, to, by, body }`, `while`, `repeat { body, until }`,
`exit`, `return`, `error { text }`.

### Ladder

Each rung has `number`, `comment`, `text`, `elements` and `lowered`.

`elements` is the rung as drawn: `instruction { name, addOn, role, args[] }` and `branch { legs[][] }`.
`role` is `input` (contributes to the rung condition), `output` (acts when the condition is true) or
`always` (executed every scan with the condition as enable).

`lowered` is the same rung as statements, produced with these rules. The rung condition `rc` starts as
`true` and flows left to right; a branch ORs the conditions at the end of its legs.

| instruction | lowering |
|---|---|
| `XIC(a)` / `XIO(a)` | `rc := rc AND a` / `rc := rc AND NOT a` |
| `EQU NEQ GRT GEQ LES LEQ (a,b)` | `rc := rc AND (a op b)` |
| `CMP(expr)` | `rc := rc AND expr` |
| `LIM MEQ ONS OSR OSF DTR` | `rc := rc AND fn(...)` (boolean instruction kept as a function; ONS/OSR/OSF carry their storage bit) |
| `AFI()` | `rc := false`; `NOP()` no effect |
| `OTE(a)` | `a := rc` (every scan) |
| `OTL(a)` / `OTU(a)` | `IF rc THEN a := true / false` |
| `MOV CLR ADD SUB MUL DIV MOD AND OR XOR NOT NEG ABS SQR CPT` | `IF rc THEN dest := expression` |
| `TON TOF RTO CTU CTD PID ALMA ALMD …` and every add-on call | `call { enable: rc }` (runs every scan; the instruction reads its EnableIn) |
| anything else (`JSR`, `MSG`, `COP`, `FLL`, motion, …) | `IF rc THEN call` |

After an output instruction the condition continues unchanged, so several outputs on one rung share the
same `rc`. Backends that model rung-in-condition natively can use `elements` instead.

### Structured text

`st { statements[], comments[], lines[] }`. Instruction calls with enumeration arguments carry them as
`keyword` expressions (`GSV(WallClockTime, …)`), routine names in `JSR` as `keyword { kind: "routine" }`.

### SFC

`sfc { steps[], transitions[], branches[], stops[], links[] }`. Steps carry `initial`, `preset`,
`limitHigh`, `limitLow` expressions and `actions[]` with `qualifier` (N S R L D P P1 P0 SL SD DS),
`preset` and `body` statements. Transitions carry `condition`. Branches list their leg ids and
`type` selection/simultaneous, `flow` diverge/converge. `links` connect element ids (steps,
transitions, stops, branch legs). Coordinates are kept for diagram output.

### FBD

`fbd { sheets[{ number, elements[], wires[] }] }` with element kinds `iref oref icon ocon block function
addon jsr sbr ret text`; `block`/`addon` carry `type`, `name` (backing tag), `pins`, `parameters`.
