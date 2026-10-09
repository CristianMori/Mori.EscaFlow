# Mori.EscaFlow.Runtime.Host

The web console for generated controllers: `PlcWebHost.Run(controller, args, options)` serves a single
embedded page (`wwwroot/index.html`) and a JSON API over `IPlcController`: `/api/state`, `/api/start`,
`/api/stop`, `/api/tags`, `/api/tag` (GET/PUT), `/api/snapshot` (download/upload), `/api/snapshots` (named
snapshots on the server). Options: URL, auto-start, snapshot directory, page size.
