# Mori.EscaFlow.Transpiler.Logix

The Rockwell flavour of the middle format, used by the importer and by converters that want the
vendor details:

- `LogixExtensions` — typed views of the `vendor.logix` slot on documents, variables, types, members, POUs, actions, bodies, rungs, devices and tasks (`node.Logix()` / `SetLogix`), and `LogixVendor` constants (vendor id, synthetic global list names).
- `LogixMessage` / `LogixSocket` — MESSAGE configuration and the structural socket binding (paths, never values).
- `LogixServiceCatalog` — every Logix instruction with its parameters, loaded from `catalog/logix-services.json` (regenerate with `tools/gen-logix-catalog.sh`).
- `LogixProfiles` — the `logix` pin profile of the standard function blocks (TIMER / COUNTER structures).
