# C# generator, runtime and host — design contract

The C# generator turns a plc-ir 2.0 document into a complete solution that behaves like the source
controller and exposes every tag over EtherNet/IP through EthernetIPSharp (submodule
`external/EthernetIpSharp`, branch `feature/transpiler-gaps`). Three hand-written packages support the
generated code; this document is the contract between them and the generator.

```
src/Mori.EscaFlow.Converter      shared generator layer: naming tally, Logix layout calculator, code writer, report
src/Mori.EscaFlow.Converter.CSharp    C# emitter: solution, projects, types, programs, add-ons, controller, host
src/Mori.EscaFlow.Runtime.CSharp      runtime library (namespace Mori.EscaFlow.Runtime): wrappers over tag memory, structures,
                            arrays, scan engine, system status, Logix timers/counters, services, snapshots,
                            EtherNet/IP server hosting, IPlcController
src/Mori.EscaFlow.Runtime.Host        ASP.NET Core host (namespace Mori.EscaFlow.Runtime.Host): web page + JSON API over IPlcController
```

## Generated solution layout

```
<Name>/
  <Name>.slnx
  Directory.Build.props                  net10.0, nullable, implicit usings, warnings as in this repo
  <Name>.Logic/<Name>.Logic.csproj       ProjectReference to Mori.EscaFlow.Runtime.CSharp (path from --runtime-path)
    Types/<TypeName>.cs                  one structure class per IR type (user, vendor, io); user string types
    AddOns/<Pou>.cs                      one class per functionBlock POU
    Programs/<Pou>.cs                    one class per program POU
    Controller.cs                        <Name>Controller : PlcController
  <Name>.Host/<Name>.Host.csproj         ProjectReference to Mori.EscaFlow.Runtime.Host; Program.cs runs PlcWebHost
  generation-report.json                 aliases tally, naming tally, unsupported services, layout notes
  README.md
```

## Value model (runtime)

Every atomic leaf is a wrapper object bound to a byte range of an EthernetIPSharp `Tag` buffer, so the
EtherNet/IP server and the logic share one memory:

- `WrapBOOL` (tag, byteOffset, bit) `.Value` bool; `WrapSINT .Value sbyte`, `WrapINT short`, `WrapDINT int`,
  `WrapLINT long`, `WrapUSINT byte`, `WrapUINT ushort`, `WrapUDINT uint`, `WrapULINT ulong`, `WrapREAL float`,
  `WrapLREAL double`. Integer wrappers expose `bool this[int bit] { get; set; }` and `GetBit(int) / SetBit(int, bool)`.
  Writes use `Tag.WriteSilent` (no per-write events); BOOL writes use `Tag.AtomicSetBit`.
- `WrapSTRING` : `PlcStruct` with `LEN` (WrapDINT at +0), `DATA` (`PlcArray<WrapSINT>` at +4), `Capacity`,
  `.Value` string get/set (clamps to capacity, updates LEN). Generated user string types derive from it
  with their capacity: `public sealed class STRING20 : WrapSTRING`.
- `PlcStruct` (tag, byteOffset): base of every generated structure class; members are `public readonly`
  fields constructed at `byteOffset + memberOffset`; `Size` static per class.
- `PlcArray<T>` : elements of type T (wrapper, struct class or nested array) with `Dims`, `Length`,
  `this[int]`, `this[int, int]`, `this[int, int, int]`, row-major, constructed with an element factory
  `(tag, offset) => new T(tag, offset)` and the element size. BOOL arrays are DWORD-packed: element i is
  bit i of DINT i/32 (runtime `PlcBoolArray`).
- Rung temporaries and expression intermediates are plain C# locals.

## Layout

The generator computes every structure layout with Logix rules (`Mori.EscaFlow.Converter.Layout.LogixLayout`)
and emits, per structure class, a static `Layout` (`PlcLayout { Name, Size, Members[] { Name, TypeCode, Offset,
Bit, Dims, ElementSize, TemplateName } }`). The runtime turns a `PlcLayout` into an EthernetIPSharp
`TemplateDefinition` (`TagStore.RegisterLayout`) so the server presents the same member list and offsets.
Rules: atomic alignment = size (BOOL is one bit in its host; BIT overlays use the host and bit from
`vendor.logix`; other BOOL members get synthesized SINT hosts, 8 per host, in member order); structures align
to 8 when they contain a 64-bit member, else 4; structure size rounds up to its alignment; BOOL arrays are
packed 32 per DINT; add-on backing structures put EnableIn/EnableOut first and pack BOOL parameters and
locals 32 per hidden DINT host; STRING is LEN + DATA[n]. Vendor structures whose exact packing is unknown
are laid out by the same rules and listed under `layoutNotes` in the report.

## Generated code shapes

```csharp
public sealed class Motor : PlcStruct {
    public static readonly PlcLayout Layout = ...;
    public readonly WrapREAL Speed; public readonly WrapBOOL Fault; public readonly TIMER Timer; public readonly STRING Name;
    public Motor(Tag tag, int offset) : base(tag, offset) { Speed = new WrapREAL(tag, offset + 0); ... }
}
public sealed class TIMER : PlcStruct, ITimer { ... PRE, ACC, EN, TT, DN ... }      // vendor types are generated too
public sealed class Blink : FunctionBlock {                                           // add-on instruction
    public readonly WrapBOOL EnableIn, EnableOut; public readonly WrapDINT Rate; public readonly WrapBOOL Out; public readonly TIMER T1;
    public Motor? ModuleInput;                      // InOut parameters are references rebound before each call
    public Blink(Tag tag, int offset, TController c) ...
    public void Execute(bool enableIn) { EnableIn.Value = enableIn; if (!enableIn) { EnableInFalse(); return; } /* Logic body */ }
    public void Prescan() { ... }  public void Postscan() { ... }  public void EnableInFalse() { ... }
}
public sealed class P : PlcProgram<TController> {                                     // program
    public readonly WrapDINT Idx; ...                                                 // program tags
    public override void Main() { MainRoutine(); }                                    // synthetic body
    public void MainRoutine() { ... }  public void Sub() { ... }                       // routines
    public override void Prescan() { ... }                                            // init hook
}
public sealed class TController : PlcController {
    public readonly WrapBOOL Run; public readonly Motor M1; public readonly PlcArray<Motor> Motors; ...   // controller tags
    public readonly AB_1756_DI_I_0 DI_I; ...                                          // module images (global list IO)
    public readonly P P;                                                              // program instances
    public TController(PlcControllerOptions? options = null) : base("T", options) {
        // register layouts, define tags in the TagStore, bind wrappers, build tasks with programs in scan order
    }
}
```

Calls: a standard block under the logix profile → `Logix.TON(M1.Timer, rc)` (`Mori.EscaFlow.Runtime.Logix` static
class: TON, TOF, TONR, CTU, CTD and their `Prescan` forms on `ITimer` / `ICounter`); an add-on →
`Blk.Rate.Value = 100; Blk.Execute(rc); Pulse.Value = Blk.Out.Value;` (inputs copied in, outputs copied
out, InOut fields rebound); an action → method call; a `sysCall` → `Services.GSV(...)` on
`LogixServices` (wrappers and structures passed as objects, keywords as strings, literals as values);
`copy`/`fill` → `PlcMem.Copy(src, dst, count)` / `PlcMem.Fill(value, dst, count)`; `conv` →
`PlcMath.RoundEven`, `PlcMath.Wrap<T>`; `edge` → `PlcLogic.RisingEdge(clk, storage)`; standard functions →
`PlcFn.<NAME>`; `jump`/`label` → `goto`; `case` → `switch`; `repeat` → `do … while (!until)`.

Services not implemented by the runtime compile to `Services.Unsupported("MAJ", args)` (throws at run
time) and are listed in the generation report; `--strict-services` turns them into generation errors.

## Runtime behaviour

- `PlcController`: `Name`, `State` (Stopped, Running, Faulted), `Mode` (Program, Run), `Start()`, `Stop()`,
  `Tasks`, `Sys` (`SystemStatus`: `FirstScan` true only during the first scan after entering Run, scan
  counters, wall clock, mode, fault state), `Services`, `Store` (`TagStore` over `TagDatabase`),
  `IPlcController` implementation for hosts. `Start()` runs every program's `Prescan()` once (init hook),
  then starts the tasks.
- `PlcTask`: continuous (free-running loop), periodic (interval, priority), event (triggered by
  `Trigger()`; the EVENT service raises it). One thread per task; programs run in scan order; watchdog
  timing recorded.
- `TagStore`: defines tags (`Define(path, atomicType, dims)`, `Define(path, layout, dims)`), program scopes
  (`Program:P.Tag`), module images by Logix path (`DI:I`), reads/writes by Logix path for hosts
  (`TryRead(path, out value)`, `TryWrite(path, text)`) using `TagPathWalker`, enumerates tags with types.
- Snapshots: `SaveSnapshot(stream)` / `LoadSnapshot(stream)` via `TagDatabasePersistence`.
- EtherNet/IP: `EipServer.Start(TagDatabase, port = 44818)` hosts the Logix dispatcher so clients read and
  write the controller's tags.

## IPlcController (host contract)

```csharp
public interface IPlcController {
    string Name { get; }  ControllerState State { get; }  ControllerMode Mode { get; }
    void Start();  void Stop();
    ScanStatistics Statistics { get; }                                   // scans, last/avg/max scan time per task
    IEnumerable<TagInfo> Tags(string? filter = null);                    // path, type name, dims, program scope
    bool TryRead(string path, out TagValueText value);                   // path = Logix path incl. members/elements
    bool TryWrite(string path, string text, out string? error);
    void SaveSnapshot(Stream stream);  void LoadSnapshot(Stream stream);
}
```

The host serves `/` (single embedded page: state, start/stop, mode, scan statistics, save/load snapshot,
tag browser with filter, value read and write) and the JSON API `/api/state`, `/api/start`, `/api/stop`,
`/api/snapshot` (GET downloads, POST uploads), `/api/tags?filter=&skip=&take=`, `/api/tag?path=` (GET/PUT).
