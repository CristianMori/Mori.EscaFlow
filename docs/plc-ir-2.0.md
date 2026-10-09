# plc-ir 2.0 — the vendor-neutral middle file

`plc-ir` is the JSON document that sits between the PLC front-ends (today: Rockwell Logix from an L5X
export; later: Siemens TIA Portal, Beckhoff TwinCAT, Codesys) and the code generators. Its core is
IEC 61131-3: configuration, resources, tasks, program instances, POUs (programs, function blocks,
functions, interfaces), variable sections, IEC data types, standard function blocks, and typed statement
and expression trees. Everything a vendor has that the core cannot express travels in a `vendor.<id>`
slot on the node it belongs to, so nothing is lost and no backend has to know a vendor it does not
target.

Produce it with the CLI (`dotnet run -c Release --project src/Mori.EscaFlow.Cli -- ir build <file.L5X> <out.ir.json>`),
read it in C# with `Mori.EscaFlow.Transpiler.IrJson.ReadFile(path)` (assembly `Mori.EscaFlow.Transpiler`, no other dependencies) or in any
language from the JSON. The JSON Schema (draft 2020-12) is `src/Mori.EscaFlow.Transpiler/Schema/plc-ir-2.0.schema.json`,
generated from the C# model by `Mori.EscaFlow.Transpiler.Schema.SchemaGenerator` and embedded in the assembly.

## Design rules

- **IEC core, vendor slots.** Every node derives from `IrNode` with `vendor: { "<id>": {...} }`,
  `description` and `src` (provenance: line/col, rung/element, sheet). Backends that target the neutral
  core ignore `vendor`; a tag server or a same-vendor round trip reads it through the vendor package
  (`Mori.EscaFlow.Transpiler.Logix` gives typed views: `variable.Logix().Path`, `type.Logix().HiddenMembers`, ...).
- **Everything is resolved.** References carry the id of the declaring variable (aliases expanded, the
  alias noted), the leaf type, whether they touch an I/O channel image, and whether they enter an
  opaque type. Actions, POUs, channels, devices and tasks are referenced by id or name and checked by
  the validator.
- **Everything is typed.** Expressions carry `type`; conditions are `BOOL`; implicit vendor conversions
  are explicit `conv` nodes.
- **One executable form.** `body.statements` is always the executable content, whatever the source
  language. Ladder and dataflow diagrams keep their drawing in `body.diagram` for backends with native
  graphics; SFC bodies have no statements (the chart is executed by the runtime).
- **Plugins for what differs.** Timers and counters are standard blocks (`fbCall` with `fb` and
  `profile`); vendor services are `sysCall`/`sysFn` into a named catalog. A backend implements one
  runtime interface per profile and one per catalog; an unknown profile or service is a compile-time
  error, never silent.
- **Versioned.** `format` is `plc-ir`, `irVersion` is `2.<minor>`; readers accept the same major and
  ignore unknown members (strict mode reports them).

## Document

```json
{ "format": "plc-ir", "irVersion": "2.0",
  "source": { "vendor": "logix", "dialect": "l5x", "file", "exportDate", "softwareRevision", "generator", "generatedAt" },
  "configuration": { "name", "resources": [ { "name", "processorType", "tasks": [...], "programs": [...], "faultProgram" } ] },
  "types": [ ... ], "globals": [ { "name", "vars": [ ... ] } ],
  "io": { "devices": [ ... ], "channels": [ ... ] },
  "pous": [ ... ], "diagnostics": [ { "severity", "code", "message", "location" } ] }
```

- `resources[].tasks[]`: `kind` continuous | periodic | event | startup, `intervalMs`, `priority`,
  `watchdogMs`, `event { kind, source }`, `programs[]` (instance names in scan order), `disabled`.
- `resources[].programs[]`: program instances `{ name, pou, task, disabled }` (IEC
  `PROGRAM inst WITH task : Pou;`).

## Types

Atomic (elementary) types are never listed; they are the IEC set `BOOL SINT INT DINT LINT USINT UINT
UDINT ULINT REAL LREAL BYTE WORD DWORD LWORD CHAR WCHAR TIME LTIME DATE LDATE TOD LTOD DT LDT`
(`Mori.EscaFlow.Transpiler.Catalogs.AtomicTypes`).

```json
{ "name": "Motor", "kind": "struct", "class": "user", "opaque": false,
  "members": [ { "name": "Speed", "type": "REAL" }, { "name": "Fault", "type": "BOOL", "vendor": { "logix": { "bitHost": "ZZZZZZZZZZMotor0", "bitNumber": 0 } } },
               { "name": "Flags", "type": { "kind": "array", "dims": [ { "lower": 0, "upper": 31 } ], "element": "BOOL" } } ] }
```

- `kind`: struct | union | enum (`values[]`) | alias (`type`) | subrange (`type`, `lower`, `upper`) |
  array (`type` = element, `dims`) | string (`length`, `wide`).
- `class`: user | vendor (firmware-supplied: TIMER, PID, AXIS_*) | io (defined by a device) | library |
  unresolved.
- `stdFb { fbs[], profile }` on a vendor structure that backs standard function blocks (Logix TIMER →
  TON/TOF/TONR, COUNTER → CTU/CTD under profile `logix`).
- A **type reference** is a plain string for a named type (atomic, structure, POU used as an instance
  type, standard block) or an object: `{ "kind": "array", "dims": [ {lower, upper} ], "element": T }`,
  `{ "kind": "string", "length": 20, "wide": false }`, `{ "kind": "pointer" | "reference", "to": T }`,
  `{ "kind": "any" }`.
- Function block instances are typed by the POU name; add-on backing structures are not emitted.

## Variables

```json
{ "id": "pou/Cell/Timer1", "name": "Timer1", "section": "local", "type": "TIMER", "retain": "retain",
  "initial": { "kind": "struct", "type": "TIMER", "members": [ { "name": "PRE", "value": { "kind": "scalar", "type": "DINT", "int": 500 } } ] },
  "vendor": { "logix": { "path": "Program:Cell.Timer1", "scope": "program", "radix": "Decimal" } } }
```

- `id`: `global/<list>/<name>`, `pou/<pou>/<name>`, `pou/<pou>/<method>/<name>`.
- `section`: input | output | inOut | local | temp | static | global | external. `retain`: none |
  retain | persistent. `constant`, `address` (`%IX0.0`), `io` (channel id when the variable is a device
  image), `alias { text, target }` (every use in logic is already expanded to the target),
  `comments{}` (per member/element), `attributes{}` (units, min/max, state names).
- `initial`: value tree, `kind` = scalar (`int` for integers/BOOL/bit strings, `real`, or `raw` for
  time/date literals and unparsable text), string (`length`, `text`), struct (`members[]` in declaration
  order), array (`type` = element type, `dims`, row-major `elements`).

## I/O

`io.devices[]`: `{ id: "device/<name>", name, catalog, vendorName, parent, inhibited }`.
`io.channels[]`: `{ id: "io/<device>/<name>", device, name, direction: input | output | config | inOut,
type, address, variable }` — the variable is the channel's image in logic (Logix: the `global/IO/<Module>:<I|O|C>`
variables of the synthetic `IO` global list).

## POUs

```json
{ "name": "Blink", "kind": "functionBlock",
  "vars": [ { "id": "pou/Blink/Rate", "section": "input", ... } ],
  "body": { "language": "ld", "statements": [ ... ], "temporaries": [ { "name": "_rc1", "type": "BOOL" } ], "diagram": { "kind": "ld", ... } },
  "actions": [ { "name": "Prescan", "body": {...} }, { "name": "Init", "hook": "init", "synthetic": true, "body": {...} } ] }
```

- `kind`: program | functionBlock | function (`returnType`) | interface. `methods[]`, `properties[]`,
  `extends`, `implements[]` for object-oriented sources. `onFault`: action run on a program fault.
- `actions[]`: named bodies sharing the POU scope (IEC ACTION; Logix routines). `hook`: init (first
  scan / prescan), exit, enableFalse (called with EN false), postscan. `synthetic`: synthesized by the
  front-end. `calls[]`: actions this one calls.
- `body`: `language` st | ld | fbd | cfc | il | sfc | graph | encrypted | empty; `statements[]`;
  `temporaries[]` (lowering temporaries, referenced with root kind `temp`); `diagram`; `text[]`
  (source lines); `comments[]`.
- A Logix program is a POU of kind program whose synthetic body calls the main routine; each routine is
  an action. An add-on instruction is a function block whose Logic routine is the body; Prescan,
  Postscan and EnableInFalse routines are actions (hooked when the definition enables them).

## Expressions (`op`)

| op | fields | meaning |
|---|---|---|
| `ref` | `root { kind: var \| temp \| system \| this \| unresolved, id, name }`, `path[]`, `type`, `alias`, `io`, `opaque`, `error` | variable reference; `path` segments: `member { name }`, `index { indices[] }`, `length` (string length), `char { index }` (string character), `deref` |
| `lit` | `kind` int/real/bool/string/wstring/time/date/tod/dt/enum, `int`, `real`, `bool`, `string`, `radix`, `text`, `type` | constant |
| `un` | `operator` neg/not, `operand` | unary |
| `bin` | `operator` add sub mul div mod pow eq ne lt gt le ge and or xor, `left`, `right` | binary (and/or/xor are logical on BOOL, bitwise on integers) |
| `fn` | `name` or `pou`, `args[]` | standard function (`Mori.EscaFlow.Transpiler.Catalogs.StandardFunctions`: ABS SQRT … SHL SHR SEL MUX MIN MAX LIMIT LEN CONCAT MID FIND … `X_TO_Y`) or project function |
| `conv` | `to`, `mode` wrap/saturate/roundEven/roundHalfUp/trunc/checked, `operand` | explicit conversion where the source language converted implicitly (Logix REAL→integer stores round half to even; integer narrowing wraps) |
| `bit` | `of`, `bit` | one bit of an integer or bit string; usable as an assignment target (Logix `Word.3`, `Word.[i]`) |
| `edge` | `kind` rising/falling, `clk`, `storage` | edge of `clk` using the BOOL `storage` as previous-scan memory |
| `sysFn` | `catalog`, `service`, `args[]` | vendor runtime function inside an expression |
| `keyword` | `text`, `kind` enum/label/action/name | enumeration operand, jump label, action name or object name used as an argument |
| `placeholder` | `empty` | unsupplied operand (`?`) |
| `error` | `text` | text the front-end could not interpret |

## Statements (`kind`)

| kind | fields |
|---|---|
| `assign` | `target` (ref or bit), `value`, `nonRetentive` (SFC `[:=]`) |
| `fbCall` | `instance`, `fb` (standard block) or `pou`, `method`, `profile`, `inputs{}`, `outputs{}`, `en`, `eno`, `mode` normal/prescan/postscan/enableFalse |
| `fnCall` | `name` or `pou`, `args[]`, `outputs{}`, `en` |
| `actionCall` | `action`, `pou`, `inputs[]`, `outputs[]`, `en` |
| `sysCall` | `catalog`, `service`, `args[]` (positional, as the catalog lists them; null = elided), `en`, `mode` |
| `copy` | `source`, `dest`, `count`, `mode` typed/bytes, `atomic`, `en` |
| `fill` | `value`, `dest`, `count`, `en` |
| `if` | `branches[{condition, body}]`, `else` |
| `case` | `selector`, `arms[{labels[{value, to}], body}]`, `else` |
| `for` | `variable`, `from`, `to`, `by`, `body` |
| `while` / `repeat` | `condition`, `body` / `body`, `until` |
| `exit`, `continue`, `return`, `jump { label }`, `label { name }`, `error { text }` | |

`en` on a call is the enable input when the source has one (a ladder rung condition, an EN pin); a call
without `en` is unconditional. For a project function block (`pou`), `en` maps to the block's implicit
enable (Logix EnableIn; when false the block's `enableFalse` action runs instead of the body).

## Standard function blocks and profiles

`Mori.EscaFlow.Transpiler/Catalogs/std-fbs.json` lists TON, TOF, TP, TONR, CTU, CTD, CTUD, R_TRIG, F_TRIG, RS, SR with
IEC pin names and types, and the pin **profiles**:

- `iec`: the instance type is the block itself, TIME pins.
- `logix`: instances are TIMER / COUNTER structures; the enable pin is the rung condition (recorded in
  EN / CU / CD), PT ↔ PRE and ET ↔ ACC as DINT milliseconds, Q ↔ DN, extra pins TT / OV / UN; RTO is
  TONR whose reset is the RES instruction (lowered to member assignments).
- `s7`: IEC timers and counters as instance data blocks.

A `fbCall { fb: "TON", profile: "logix", instance: <ref TIMER>, inputs: { IN: <rc> } }` is implemented by
the backend's Logix timer runtime; a backend that only knows the `iec` profile rejects it.

## Vendor service catalogs

`sysCall` / `sysFn` name a catalog (`logix`, later `s7`, `twincat`) and a service. `Mori.EscaFlow.Transpiler.Logix`
embeds `catalog/logix-services.json`: every Logix instruction with its positional parameters (names
from 1756-RM003 where known), group (bit, timer, compare, math, move, file, program, system, messaging,
control, ascii, phase, motion, safety, fbd), `enable` (input: runs every scan and reads the rung
condition; gate: runs only when true), `variadic`, and `lowered` (instructions the front-end never emits
as services because they become contacts, coils, assignments, standard blocks, copies or action calls).
The validator checks service names and argument counts against the registered catalogs
(`IServiceCatalog`).

## Ladder

`body.diagram` of kind `ld` keeps every rung as drawn: `number`, `comment`, `text`, `elements[]` with
kinds `contact { name, operand, negated, edge, storage, args }`, `coil { name, operand, mode: assign |
negated | set | reset | risingPulse | fallingPulse, storage }`, `block { name, args[], pins{}, enable:
input | gate | none, eno, isPou }`, `branch { legs[][] }`, `jump { label }`, `label { label }`,
`mcr { begin }`, `return { outputs[] }`. The lowered statements carry `src { rung, element }`.

**Lowering contract.** The rung condition `rc` starts TRUE at the left rail and flows left to right.

| element | lowering |
|---|---|
| contact | `rc := rc AND operand` (negated: `AND NOT`; edge: `AND edge(kind, rc, storage)`; AFI: FALSE) |
| branch | `rc := rc AND (leg1 OR leg2 …)`; outputs inside a leg see `incoming AND legCondition` |
| coil assign / negated | `operand := rc` / `operand := NOT rc` every scan |
| coil set / reset | `IF rc THEN operand := TRUE / FALSE` |
| coil pulse | `operand := edge(kind, rc, storage)` |
| block, enable input | the statement always runs with `rc` bound to its enable pin (`fbCall.inputs.IN`, `fbCall.en`, `sysCall.en`) |
| block, enable gate | `IF rc THEN statement` |
| jump / return | `IF rc THEN jump / return` |
| mcr begin | `_mcrN := rc`; every following rung starts from `_mcrN` until the closing `mcr` |
| after an output | `rc` is unchanged, so several outputs share the same condition |

Compound conditions used by more than one output are materialized once in a body temporary (`_rcN`).

**Logix instruction mapping** (`LogixInstructionMap`, `CallEmitter`): XIC/XIO → contact; EQU..LEQ, CMP
→ contact with a comparison; LIM and MEQ → expression trees; DTR → `sysFn`; ONS → edge contact on the
rung condition; OTE/OTL/OTU → coil; OSR/OSF → pulse coil; TON/TOF/RTO/CTU/CTD → `fbCall` profile logix;
RES → member assignments; MOV/CLR/ADD…/CPT/AND/OR/XOR/NOT/NEG → `assign` (with `conv` where Logix
promotes); ABS/SQR/SIN…/TOD/FRD/string instructions/MVM/BTD/SWPB → `assign` with a standard function;
COP/CPS → `copy` (bytes, atomic for CPS); FLL → `fill`; JSR → assignments to the callee's SBR
parameters, `actionCall`, assignments from the callee's RET values; SBR → nothing; RET/TND → `return`;
JMP/LBL → `jump`/`label`; MCR → zone temporaries; FOR → `for` around an action call; add-on calls →
`fbCall { pou, en: rc }` with required parameters by name; everything else (GSV/SSV/MSG/PID/ALM*/motion/
file/phase/safety) → `sysCall` into the `logix` catalog, every scan with `en` or gated by IF.

## Structured text, SFC, FBD

- ST statements map one to one; `SBR`/`RET` in ST behave as in ladder; `Tag.LEN` and `Tag.DATA[i]` of
  string family types become `length` / `char` path segments.
- SFC: `diagram.sfc { steps[], transitions[], branches[], stops[], links[] }` with typed presets,
  limits, conditions and action bodies (`body` per action); qualifiers are the IEC letters.
- FBD: `diagram.net { sheets[{ elements[], wires[] }] }` with element kinds input | output |
  connectorIn | connectorOut | block | function | call | entry | exit | text. The dataflow lowering
  (`NetLowering`) orders executable elements topologically (feedback wires excluded, ties by position),
  assigns every wired pin into the block's instance member, then invokes the block (`sysCall` for
  vendor blocks, `fbCall` for add-ons); tagless functions produce `_fN` temporaries; output references
  are assigned in their own position; a feedback wire's destination therefore reads the previous scan.

## Prescan (init hook)

The Logix front-end synthesizes an `init` action per program (`Prescan`) and per add-on (`Init`): the
ladder is re-lowered in prescan mode (assign coils cleared, one-shot storages set, pulse outputs
cleared, standard blocks / add-ons / always-scan services invoked with `mode: prescan`), ST and FBD
bodies contribute their function block and always-scan service calls in prescan mode, and an add-on's
own Prescan routine is called afterwards when the definition executes it. Backends run the hook once
when entering Run, before the first scan.

## Logix vendor extension (`vendor.logix`)

| node | fields |
|---|---|
| document | `targetName`, `targetType`, `majorRev`, `minorRev`, `safetyLevel` |
| variable | `path` (tag-server address: `Tag`, `Program:P.Tag`, `Module:I`), `scope`, `radix`, `externalAccess`, `opcUaAccess`, `class`, `usage`, `required`, `visible`, `implicit`, `aliasFor`, `message` (full MESSAGE configuration incl. `socket` binding), `produced`, `consumed` |
| type | `class` (predefined/user/addon/module), `family`, `moduleType { vendor, product, direction, index, thirdParty }`, `hiddenMembers[]` (BIT overlay hosts) |
| member | `bitHost`, `bitNumber`, `radix`, `externalAccess`, `packed` |
| pou | `kind` (program/addOn), `revision`, `vendor`, `class`, `executePrescan/Postscan/EnableInFalse`, `backingType`, `mainRoutine`, `faultRoutine`, `disabled`, `folder`, `parentFolder`, `children`, `task` |
| action | `routineType` (RLL/ST/SFC/FBD), `scanMode` |
| body / rung | `onlineEditType`; rung `type` |
| device | `vendor`, `productType`, `productCode`, `major`, `minor`, `parentModule`, `parentModPortId`, `ports[]`, `connections[]` (with channel ids, RPI), `configChannel`, safety and comm fields |
| task | `type`, `class`, `eventTrigger`, `eventTag`, `inhibitTask`, `disableUpdateOutputs` |

## Validation

`Mori.EscaFlow.Transpiler.Validation.IrValidator` runs three passes: structural (format, enum values, ids), referential
(every id and name resolves; standard block pins and instance types under the profile; POU pins and
directions; catalog services and argument counts; labels; alias cycles; task/program consistency) and
typing (BOOL conditions, operand categories, `bit` on integers, `edge` storage, `conv` pairs,
assignment compatibility, pin types, reference path walks against the declared types). Codes are
`IRVnnn` (`ValidationCodes.Describe`). `ir validate` in the CLI runs it on a JSON file after a strict
read that reports unknown members.

## Mapping other vendors (planned)

Siemens LAD/FBD networks and TwinCAT LD map onto the same element kinds (contacts, coils with set/reset
modes, boxes with EN/ENO, jumps and labels); IEC timers use profile `iec` (TwinCAT/Codesys) or `s7`;
system functions (SFC/SFB, ADS, ...) get their own catalogs; instance and global data blocks are global
lists and function block instances; multi-instances are `local` variables typed by the POU.
