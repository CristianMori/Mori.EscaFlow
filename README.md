# PlcTranspiler — L5X parser and plc-ir middle file

A C# (.NET 10) toolchain that reads PLC projects into a vendor-neutral middle file for transpilers.
Today it reads Rockwell Logix 5000 `.L5X` exports into a complete in-memory object model (controller,
data types, modules, add-on instructions, tags with values, programs, routines in ladder, structured
text, SFC and FBD, tasks) and turns that model into **plc-ir 2.0**: an IEC 61131-3 based JSON document
with vendor extension slots, a validator, a JSON Schema and a conformance suite. Other front-ends
(Siemens, Beckhoff, Codesys) will produce the same document; code generators consume it.

## Layout

```
PlcTranspiler.slnx
src/L5x/                   L5X parser class library (namespace L5x)
  Model/                   the object model (Controller, DataTypes, Tags, Values, Programs, Logic/Rll, Logic/St, Logic/Sfc, Logic/Fbd)
  Parsing/Xml/             streaming XmlReader front end, one reader per element family
  Parsing/Rll/             ladder rung-text parser (branches, instructions, operand classification, text writer)
  Parsing/St/              structured text lexer and recursive-descent parser (statements and expressions)
  Parsing/Values/          L5K initializer parser, Decorated data reader, L5K-to-typed-value shaper
  Parsing/Linking/         reference resolution (types, aliases, JSR targets, tasks, SFC/FBD graphs)
  Diagnostics/             ParseDiagnostic, DiagnosticBag
  Json/                    System.Text.Json serialization of the model
src/Plc.Ir/                plc-ir 2.0 model, JSON reader/writer, walker, ST printer, schema generator, catalogs (no dependencies)
src/Plc.Ir.Validation/     structural, referential and typing validator (IRVnnn codes)
src/Plc.Ir.Logix/          Logix vendor extension: typed vendor.logix views, MESSAGE/socket model, logix-services.json catalog
src/Plc.Ir.Logix.Build/    Logix front-end: parsed project -> plc-ir (types, variables, I/O, POUs, ladder and dataflow lowering, prescan)
src/Plc.Cli/               console tool (parse, ir build, ir validate, ir print)
docs/plc-ir-2.0.md         middle file format (docs/ir-1.0.md is the retired v1 format)
tests/L5x.Tests/           parser tests (unit + integration over the sample exports)
tests/Plc.Ir.Tests/        model, JSON, catalog, walker, printer and schema tests
tests/Plc.Ir.Validation.Tests/
tests/Plc.Ir.Logix.Tests/  front-end tests: fixture, sample exports, conformance goldens
tests/Conformance/         crafted L5X fixtures per feature and their golden plc-ir / ST outputs
```

## Using the library

```csharp
using L5x.Parsing;

var project = L5xParser.ParseFile("MyController.L5X");
var controller = project.Controller;

foreach (var program in controller.Programs)
    foreach (var routine in program.Routines)
        switch (routine.Content)
        {
            case L5x.Model.Logic.RllContent rll:
                foreach (var rung in rll.Rungs)
                    foreach (var instr in rung.Ast!.AllInstructions())
                        Console.WriteLine($"{instr.Mnemonic}({string.Join(",", instr.Arguments)})");
                break;
            case L5x.Model.Logic.StContent st:
                Console.WriteLine(st.Ast!.Statements.Count + " statements");
                break;
        }

foreach (var d in project.Diagnostics) Console.WriteLine(d);
```

Parsing runs in three phases:

1. **XML phase**: a streaming `XmlReader` walks the file once. Bounded elements (one data type, module,
   task, SFC or FBD body) are materialized as `XElement`; tags, programs and routines are read with the
   cursor so the 200 MB exports never load whole. `CustomProperties` subtrees are skipped.
2. **Logic phase**: every rung, ST body and SFC snippet is parsed in parallel into an AST.
3. **Link phase**: the data type registry is built (built-in, user, add-on, module-defined types),
   references are resolved and L5K initializers are shaped into typed values when Decorated data is absent.

Content problems never throw; they are reported as `ParseDiagnostic` entries (codes prefixed `XML`,
`RLL`, `ST`, `L5K`, `DEC`, `SFC`, `FBD`, `LNK`). Only I/O errors and malformed XML raise `L5xParseException`.

`ParseOptions` controls the work: `ParseLogic`, `ParseValues` (None / L5kOnly / DecoratedOnly / Both),
`KeepRawL5kText`, `Threads`, `ValidateL5kAgainstDecorated`, `Link`.

### Messages

A MESSAGE tag carries its configuration in `Tag.Data.Message` (`MessageConfiguration`): message type,
requested length, connected flag, connection path (raw and split into port/address segments), CIP
service code, class, instance and attribute (hex values decoded), DH+/block-transfer addressing, local
and remote elements, destination tag and cache flags. Unknown attributes are preserved. The linker binds
`LocalElementTag` and `DestinationTagRef` to the tags they name. `Controller.MessageTags` lists them all.
The runtime members of the tag (`.DN`, `.ER`, `.EN`, `.ERR`, `.EXERR`, `.Instance`, `.Path`, ...) come from the
predefined MESSAGE layout, so `MSG(tag)` instructions and `tag.DN` references resolve like any other structure.
Socket communication (the Logix Socket Object, CIP class 16#0342) is a set of such messages: the
configuration exposes `IsSocketService`, `SocketService` (CreateSocket, OpenConnection, ReadSocket,
WriteSocket, DeleteSocket, AcceptConnection, ...) and `SocketInstance`; the instance and module path that
the logic writes into the message tag at run time appear as ordinary assignments to `tag.Instance` and `tag.Path`.
The socket analysis is structural, not evaluated: for each socket message it binds the request and reply
structures, the message tag's `Instance` member (the socket handle), the session (the tag holding the
request structures, which ties the services of one socket together; DeleteSocket, which sends only the
instance, joins the single session of its program or add-on), and the paths of the fields the Socket
Object interprets, located by data type: `type` (1 = TCP, 2 = UDP), timeout, local port and address,
destination string, buffer and buffer length, UDP write destination. No protocol or endpoint is decided
by the parser, because the logic can rewrite any of those fields before the MSG runs; the generated code
implements the socket services dynamically and reads these locations at execution time.

### Resolving operands to types

`ScopeResolver` maps any tag path (from a rung operand, an ST variable or an alias) to its declaring tag
and leaf data type, following program/add-on scoping, aliases, array indices, structure members, BIT
overlays, `.n` bits and module I/O tags such as `Local:3:I.Data.4`:

```csharp
var resolver = new L5x.Parsing.Linking.ScopeResolver(project.Controller);
var r = resolver.Resolve(tagRefOperand.Path, routine.Owner);
// r.Tag, r.LeafType (e.g. DINT), r.IsBit / r.BitNumber, r.LeafDimensions, r.Module, r.Error
```

During linking, bare identifiers in built-in instructions that name no tag, module or routine
(`Disabled`, `All`, `Programmed`, `S-Curve`, ...) are reclassified as `KeywordOperand` in ladder and
`StKeyword` in structured text, so a transpiler only ever sees real variables as tag references.

### Predefined (controller-supplied) data types

`src/L5x/Model/DataTypes/BuiltInTypes.cs` defines the atomics and the structures whose L5K packing is
known exactly (STRING, TIMER, COUNTER, CONTROL). Every other predefined structure comes from the embedded
resource `src/L5x/Model/DataTypes/PredefinedTypes.txt`, one line per type:

```
?AXIS_CIP_DRIVE: CIPAxisState:DINT, ActualPosition:REAL, ..., MotionFault:BOOL, ...
```

The leading `?` marks the L5K packing as unknown (member typing only). The member lists were extracted
from the Rockwell reference manuals: MOTION-RM002 / MOTION-RM003 and the Studio 5000 v37 structure pages
for the motion types (AXIS_*, MOTION_GROUP, MOTION_INSTRUCTION, COORDINATE_SYSTEM, CAM, CAM_PROFILE,
OUTPUT_CAM), 1756-RM003 / 1756-RM006 / 1756-PM006 / LOGIX-UM001 for MESSAGE, PID, ALARM_*, FBD_*, SFC_*,
PHASE and CONNECTION_STATUS, and 1756-RM006 / 1756-RM094 for the process, drives and safety instruction
structures. Comment lines starting with `#` inside the file record the caveats (inferred spellings,
undocumented layouts). Add or correct a type by editing that file; the test suite checks that every
member type is known and that there are no duplicate names.

Module-defined I/O types (`AB:1756_DI:I:0`, third-party `_0015:935700_001_B2B4CBB0:C:0`) are never
predefined: each is created as `DataTypeClass.ModuleDefined` with a parsed `ModuleType` (vendor, product,
direction Input/Output/Config/SafetyInput/SafetyOutput, index) and its members are learned from the
Decorated data of the module connections and tags in the same export. `ScopeResolver` marks every
reference that passes through such a type or a module tag with `IsIoData` and `IoType`. A type that is
only referenced (for example an add-on instruction parameter for a module the project does not contain)
stays opaque until a project that has the module is parsed.

## Command line

```
dotnet run -c Release --project src/Plc.Cli -- parse <file.L5X> [options]

  --summary               counts, timings, memory and diagnostics by code (default)
  --diagnostics           print every diagnostic
  --json <out.json>       write the model as JSON
  --json-include <list>   model, logic, values, all (comma separated; default all)
  --no-values             skip tag values
  --no-logic              skip rung / ST parsing
  --validate-values       cross-check L5K initializers against Decorated data
  --keep-l5k              keep raw L5K text on each tag
  --threads <n>           parallelism of the logic phase
  --strict                exit code 1 when an Error diagnostic was raised

dotnet run -c Release --project src/Plc.Cli -- ir build <file.L5X> <out.ir.json> [--no-values] [--no-source] [--no-prescan] [--indent] [--validate]
dotnet run -c Release --project src/Plc.Cli -- ir validate <file.ir.json> [--no-typing] [--warnings]
dotnet run -c Release --project src/Plc.Cli -- ir print <file.ir.json> [Pou[/Action]]
```

## Middle file (plc-ir 2.0)

`ir build` writes the vendor-neutral middle file that code generators consume. The format is documented
in [docs/plc-ir-2.0.md](docs/plc-ir-2.0.md); its JSON Schema is `src/Plc.Ir/Schema/plc-ir-2.0.schema.json`.
The core is IEC 61131-3: a configuration with resources, tasks and program instances; global variable
lists; types (user, vendor, I/O, with neutral members); POUs of kind program / functionBlock / function
with variable sections, a body, actions (with `init` / `enableFalse` / `postscan` hooks), and typed
statement and expression trees. Every reference is resolved to a variable id and typed; ladder and FBD
keep their drawing in `body.diagram` and are lowered to `body.statements`; timers and counters are
standard blocks called with a pin `profile`; vendor instructions are `sysCall`s into a named catalog;
everything Rockwell-only (tag paths for the tag server, radix, MESSAGE and socket bindings, module
ports and connections, BIT overlay hosts, scan-mode flags) lives in `vendor.logix` slots.

Producers and consumers:

- `src/Plc.Ir` — the model, `IrJson`, `IrWalker`, `StPrinter`, catalogs (`AtomicTypes`,
  `StandardFunctionBlocks` with the `iec` / `logix` / `s7` profiles, `StandardFunctions`,
  `IServiceCatalog`) and `SchemaGenerator`. No dependency on the parser.
- `src/Plc.Ir.Validation` — `IrValidator`: structural, referential and typing passes; `ValidationCodes`.
- `src/Plc.Ir.Logix` — `LogixExtensions` (`variable.Logix()`, `type.Logix()`, ...), `LogixMessage`,
  `LogixServiceCatalog`.
- `src/Plc.Ir.Logix.Build` — `LogixIrBuilder` (project → document), `LadderLowering`, `NetLowering`,
  `CallEmitter`, `SubroutineIndex`, `LogixInstructionMap`.

```csharp
var ir = Plc.Ir.IrJson.ReadFile("project.ir.json");
var result = new Plc.Ir.Validation.IrValidator().Validate(ir, new() { ServiceCatalogs = { Plc.Ir.Logix.LogixServiceCatalog.Instance } });
foreach (var pou in ir.Pous)
    foreach (var action in pou.Actions)
        Emit(action.Body.Statements);   // typed statements, every ref carries a variable id and a type
```

## Tests

```
dotnet test                                   # unit + integration
dotnet test --filter Category!=Integration    # unit tests only
```

Integration tests look for the three sample exports in the repository root or in the directory named
by the `L5X_SAMPLES_DIR` environment variable and skip when they are absent. The parser tests assert the
element counts of each sample, zero ladder / ST diagnostics, that every rung round-trips through the text
writer, and that every type, alias, routine call, task and SFC link resolves. The front-end tests build
plc-ir for each sample and assert zero unresolved references, zero untyped expressions, every rung with
outputs lowered, zero validator errors, and a byte-identical JSON round trip.

Conformance: `tests/Conformance/fixtures/logix/*.L5X` are small crafted exports, one per feature area;
`tests/Conformance/golden/logix/*.ir.json` and `*.st` are their expected middle file and structured text
print. Regenerate them with `PLC_IR_UPDATE_GOLDEN=1 dotnet test tests/Plc.Ir.Logix.Tests` after an
intentional format change, and the JSON Schema with `PLC_IR_UPDATE_SCHEMA=1 dotnet test tests/Plc.Ir.Tests`.

## Notes on the format

- All free text is CDATA on its own line; the readers strip exactly one surrounding newline and keep
  the payload's indentation.
- `Line`, `Description` and similar elements can contain a nested `CustomProperties` element; only the
  element's own text nodes are used.
- Tags carry several `Data` children keyed by `Format` (L5K, Decorated, String, Message, Alarm, Axis,
  MotionGroup, CoordinateSystem, or raw hex without a format).
- Ladder rung text: instructions are concatenated, `[leg ,leg ]` is a branch, `[` inside an argument is a
  subscript, `?` is a placeholder, empty arguments are allowed, CPT/CMP/FSC/FAL carry expressions and
  motion instructions use multi-word enumeration operands such as `Forward Only`.
- Structured text: `//`, `(* *)`, `/* */` comments and `#region` directives are trivia; `[:=]` is the
  non-retentive assignment used in SFC actions; `Tag.[i]` is indirect bit addressing; `.30` is a real.
