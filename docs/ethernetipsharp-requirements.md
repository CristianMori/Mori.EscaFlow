# EthernetIPSharp: limitations to address for the PlcTranspiler C# target

Context for the agent working on https://github.com/CristianMori/EthernetIpSharp (state as of commit
31ccf38, July 2026, net8.0).

The PlcTranspiler project turns a Logix L5X export into generated C# where every tag value is a wrapper
(`WrapDINT`, `WrapBOOL`, ...) backed by a `Tag` byte buffer in `EthernetIPSharp.Logix.TagDatabase`, so
the generated controller is a Logix-compatible tag server: HMIs, OPC servers, pycomm3, Studio 5000 and
other PLCs read and write the simulated controller over EtherNet/IP exactly as they would a real one.
The logic scan runs on its own thread and writes tag memory hundreds of thousands of times per scan.
Later, module I/O tags will be bound to real modules through `EipScanner` and the safety scanner.

The items below are what the generated code needs and cannot get from the library today. File and line
references are to `src/EthernetIPSharp.Logix` unless stated otherwise. Each item ends with an acceptance
criterion.

## 1. Program-scoped tags on the server

`TagDatabase` is flat: `FindByName` looks up a root name in one case-insensitive dictionary. Only the
client (`TagClient.cs`, lines 36, 588, 634, 1170) understands the `Program:Name.Tag` convention. A Logix
project has controller tags and program tags with the same names in different programs (the sample
projects have 38 programs, 5,700 program-scoped tags), and every real client addresses them as
`Program:<program>.<tag>`.

Needed: register a tag in a program scope; resolve symbolic paths whose first segment is
`Program:<name>`; make `Get Instance Attribute List` (0x55) return the `Program:<name>` pseudo-tags at
controller scope and support browsing inside a program scope, the way a 1756 controller does (this is
what `TagClient.BrowseTagsAsync` already expects).

Acceptance: `TagClient` connected to the server browses both scopes and reads/writes
`Program:Cell.Timer1.PRE` and a controller tag `Timer1` that has the same name.

## 2. Member and element paths on the server

`LogixDispatcher.OnUnhandled` (lines 110–129) resolves `path.SymbolicName` to a whole tag by its root
name and `DispatchTagService` applies only `path.ElementId` as a first-dimension element offset
(`TagServices.HandleReadTag/HandleWriteTag`, byte offset = element * element size). Nothing walks member
segments. A request for `Motor.Speed`, `Motor.Timer.PRE`, `Line[2].Motor.Fault`, `Matrix[1,2]` or a
BOOL member bit is answered with the root tag's data or an error, while real clients (and the client half
of this very library) send exactly those paths.

Needed: a path walker over the template definition: member offsets, nested templates, array element
offsets with the member's element size, multi-dimensional indices, BOOL members as bit positions inside
their host byte (read returns a BOOL, write and Read-Modify-Write touch only that bit), STRING members.
Responses must carry the type code of the addressed element (atomic code, or 0x02A0 + structure handle
for a structure), not the root tag's.

Acceptance: `TagClient.ReadAsync<int>("Motor.Timer.PRE")`, `ReadAsync<bool>("Motor.Fault")`,
`WriteAsync("Line[2].Speed", 1.5f)` and `ReadStructAsync("Motor.Timer", ...)` succeed against the server.

## 3. Templates with nested structures, strings and pre-resolved layouts

`TagDatabase.AddTemplate` (lines 119–192) accepts atomic members only. A member whose `DataType` is not
an atomic code gets `GetElementSize` = -1 and falls back to 4 bytes (line 156), so nested UDTs, arrays of
UDTs inside UDTs, STRING members (88 bytes) and user-defined string types (LEN + DATA[n]) all produce
wrong layouts. The L5X export contains the exact layout of every user type (hidden `ZZZZZZZZZZ` host
members, bit numbers, member order), so the generator wants to register that layout rather than have the
library recompute it. `TemplateDefinition` has a public constructor but the only registration path is
`AddTemplate(name, members)`, and `_templates` is private.

Needed:
- Struct-typed members: `TemplateMember` referencing another template (by `TemplateDefinition` or
  instance id); size = the nested template's `StructureSize`, alignment 4; arrays of them.
- STRING members and user string types as templates (`LogixDataTypes.IsLogixString` exists on the client
  side only).
- `AddTemplate(TemplateDefinition)` or an overload taking pre-resolved `TemplateMemberInfo` with explicit
  offsets and bit positions, so the exported layout can be registered verbatim.
- `TemplateObject` (class 0x6C) must serve the member list of nested templates so clients can resolve them
  recursively (the client's `ReadTemplateAsync` does this).

Acceptance: register `Motor { Speed:REAL, Fault:BOOL, Timer:TIMER, Name:STRING }` and `Line { Motors:
Motor[4], Count:DINT }`; `TagClient.BrowseTagsAsync` pulls both templates and `ReadStructAsync("Line1")`
returns the right values at the right offsets, byte-identical to the layout Studio 5000 shows for the same UDT.

## 4. Add-on instruction backing structures

Logix packs BOOL parameters and BOOL local tags of an add-on instruction into hidden DINT hosts (32 per
DINT, a new host whenever the 1st, 33rd, 65th BOOL appears) and may reorder SINT/INT locals into
alignment gaps (Logix 5000 Import/Export reference manual 1756-RM014, pages 174–175). The library's
packing (8 BOOLs per hidden SINT, line 133–152) is the UDT rule and does not match. Tags of add-on type
are common (the samples have 102 definitions and thousands of instances).

Needed: either an add-on packing mode in `AddTemplate` or, simpler, the pre-resolved layout registration
from item 3, in which case the generator supplies the layout.

Acceptance: a tag of an add-on type with 40 BOOL parameters reads back from the server with the same byte
layout a real controller exports in Decorated data.

## 5. Multi-dimensional arrays

`AddTag(name, tagType, elementCount)` and `AddTag(name, template, elementCount)` support one dimension
(`arrayDims = elementCount > 1 ? 1 : 0`, line 30). Logix tags have up to three dimensions (`DINT[5,10,2]`,
`Index="[i,j]"` in the export) and clients index them as `arr[1,2,3]` (the client already emits the
segments, `TagClient` "multi" example in the README).

Needed: `AddTag` with a dimensions array, symbol type dimension bits set accordingly
(`MakeAtomicSymbolType(tagType, dims)` exists), the Symbol Object dimension attribute (attribute 8, three
UDINTs) returned in browse, and element offset computed row-major over all dimensions in item 2.

Acceptance: `WriteAsync("Matrix[1,2,3]", 7)` then `ReadAsync<int>("Matrix[1,2,3]")` returns 7 and the
element lands at byte offset ((1*10+2)*2+3)*4 for `DINT[5,10,2]`.

## 6. BOOL arrays

Logix stores `BOOL[n]` (n a multiple of 32) packed 32 per DWORD; `GetElementSize(BOOL)` is 1 (line 63),
so a BOOL array tag is one byte per element and its wire image differs from a controller's. Clients that
read `Flags[5]` expect a bit inside a DWORD.

Needed: BOOL array tags stored as DWORD-packed, element access by bit, symbol type as Logix reports it
(BOOL array with DWORD element size).

Acceptance: `BOOL[32]` tag occupies 4 bytes; `WriteAsync("Flags[5]", true)` sets bit 5 of DWORD 0.

## 7. Thread safety between the logic scan and CIP threads

`Tag.Read<T>` / `Write<T>` use `Unsafe.ReadUnaligned` / `WriteUnaligned` on a plain `byte[]` with no
synchronization (Tag.cs lines 70–86), and `SetData` copies spans the same way. The generated logic writes
tag memory continuously from its scan thread while `EipAdapter` serves reads and writes from socket
threads. Multi-byte values, structures and fragmented reads can be torn, and an HMI can observe a
structure half-updated in the middle of a scan.

Needed: a defined consistency model. The Logix-like one: CIP readers see the values as they were at the
end of the last scan, and CIP writes are applied between scans. A practical implementation is a
per-database "publish" that snapshots or double-buffers tag memory at scan boundaries, or at minimum
per-tag locking with an API the scan thread can hold across a whole scan. Also make `_symbolCache` and
`RegisterTag` safe against tags added while requests are in flight (they are `ConcurrentDictionary`
already; the tag buffers are the issue).

Acceptance: a test with one thread writing a 64-byte structure member by member in a loop and another
thread reading it via `TagClient` never observes a mixed state.

## 8. Per-write events

`Tag.Write<T>` raises `ValueChanged` on every write and `TagDatabase` forwards to `AnyTagChanged`
(Tag.cs line 78, TagDatabase.cs line 16). The generated scan performs 10⁵ to 10⁶ writes per scan and has
no use for per-write events (its HMI reads asynchronously), so the delegate check and the `TagChangeInfo`
construction on the hot path are pure overhead, and any subscriber makes the scan unusable.

Needed: a write path without notification (a flag on `TagDatabase` or on `Tag`, or a separate
`WriteSilent<T>`), and, if change tracking is wanted at all, a per-scan dirty set published at the scan
boundary of item 7 instead of one event per write.

Acceptance: 1,000,000 `Write<int>` calls with events disabled cost no more than the underlying
`Unsafe.WriteUnaligned`.

## 9. Persistence of tag data

The README lists "no persistent storage" as a known limitation. Logix tags are retentive across power
cycles, and the generated controller must be able to stop and resume with the same values.

Needed: `TagDatabase` save and restore of all tag buffers (and template ids/handles so restored data
matches the registered layout), by name, tolerant of tags added or removed between versions.

Acceptance: save, restart the process, register the same tags and templates, restore, and every value
reads back identical.

## 10. Explicit messaging client for the MSG runtime (verify and expose)

The generated code implements Logix MSG instructions at run time. It needs, from the scanner side:
- CIP Data Table Read/Write to another controller: `TagClient` covers this, including backplane routes.
- CIP Generic: send an arbitrary service to class/instance/attribute with request data and get the reply,
  unconnected (UCMM, with `Unconnected_Send` routing) or over a Class 3 connection. The README says
  `EipScanner` + `ConnectedExplicit` do explicit messaging; please expose a public method of the form
  `SendGenericAsync(service, classId, instanceId, attributeId?, requestData, route?)` returning status and
  reply bytes if it is not already public.

Acceptance: from a test, send Get Attribute Single to the Identity object of a real or simulated device
through both the unconnected and the connected path.

## 11. Smaller items

- `Tag` should expose its `TemplateDefinition` (or template id) so a view layer can compute member
  offsets without re-querying the database.
- Structure handles are `0x8000 | instanceId` (line 181). Logix uses a CRC over the template definition.
  Not required for correctness (clients use the handle the server hands out) but a Logix-style CRC would
  let generated projects present the same handles as the original controller.
- Target framework: net8.0 is fine to reference from the net10.0 solution; a NuGet package (folder
  `packaging/EthernetIPSharp.Nuget` exists) would simplify consumption.

## Priority for the transpiler

1, 2, 3 and 7 are blocking (without them the generated controller cannot be read correctly or safely by
any client). 4, 5, 6 and 8 are needed for the sample projects (add-on tags, multi-dimensional arrays,
BOOL arrays, scan performance). 9 and 10 are needed before real deployments. 11 is convenience.
