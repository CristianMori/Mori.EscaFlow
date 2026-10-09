# Mori.EscaFlow.Runtime.CSharp

The runtime library generated controllers run on (namespace `Mori.EscaFlow.Runtime`):

- `Values/` — `WrapBOOL` … `WrapLREAL`, `WrapSTRING`, `PlcStruct`, `PlcArray<T>`, `PlcBoolArray`, `PlcBitRef`, `PlcView`: every value is a view over EthernetIPSharp tag memory, so the logic and the EtherNet/IP server share one byte image.
- `Layout/` — `PlcLayout`, the structure layout a generated class declares; `Storage/TagStore` turns it into a server template and defines tags by Logix path.
- `Engine/` — `PlcController` (state, mode, start/stop, prescan, fault, `IPlcController`), `PlcTask` (continuous, periodic, event; one thread each; watchdog and statistics), `SystemStatus` (first scan, scan counters, status bits), `PlcClock`.
- `Blocks/` — `Logix` timers and counters (TON, TOF, TONR, CTU, CTD and their prescan forms) on `ITimer` / `ICounter` structures.
- `Functions/` — `PlcFn` standard functions, `PlcMath` (Logix rounding, division and conversion rules), `PlcMem` (COP/FLL semantics), `PlcLogic` (edges).
- `Services/LogixServices` — GSV/SSV/EVENT/IOT/UID/UIE, MSG stub, prescan hook, and `Unsupported` for everything else.
- `EipServer` — hosts the tag database behind the Logix dispatcher.
