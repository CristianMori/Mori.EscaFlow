using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plc.Runtime.Host;

/// <summary>
/// Hosts a generated controller behind a single web page and a JSON API: state, start/stop, scan statistics,
/// snapshots and a tag browser with read/write. Every route is under <c>/api</c>; <c>/</c> serves the embedded page.
/// </summary>
public static class PlcWebHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Run(IPlcController controller, string[] args, PlcWebHostOptions? options = null)
        => RunAsync(controller, args, options).GetAwaiter().GetResult();

    public static async Task RunAsync(IPlcController controller, string[] args, PlcWebHostOptions? options = null)
    {
        options ??= new PlcWebHostOptions();
        var app = CreateApp(controller, options, args: args);
        if (options.AutoStart) controller.Start();
        await app.RunAsync();
    }

    /// <summary>Builds the application without running it (tests use this with a test server).</summary>
    public static WebApplication CreateApp(IPlcController controller, PlcWebHostOptions? options = null, Action<WebApplicationBuilder>? configure = null, string[]? args = null)
    {
        options ??= new PlcWebHostOptions();
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Plc.Runtime.Host", LogLevel.Information);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Services.AddSingleton(controller);
        builder.Services.AddSingleton(options);
        if (configure is null) builder.WebHost.UseUrls(options.Url);
        configure?.Invoke(builder);

        var app = builder.Build();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Plc.Runtime.Host");

        app.Use(async (ctx, next) =>
        {
            await next();
            if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Method != HttpMethods.Get)
                log.LogInformation("{Method} {Path} -> {Status}", ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, ctx.Response.StatusCode);
        });

        app.MapGet("/", () => Results.Content(LoadPage(), "text/html; charset=utf-8"));
        app.MapGet("/index.html", () => Results.Content(LoadPage(), "text/html; charset=utf-8"));

        app.MapGet("/api/state", () => Results.Json(StateOf(controller), Json));

        app.MapPost("/api/start", () =>
        {
            try
            {
                controller.Start();
                log.LogInformation("Controller {Name} started", controller.Name);
                return Results.Json(StateOf(controller), Json);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });

        app.MapPost("/api/stop", () =>
        {
            try
            {
                controller.Stop();
                log.LogInformation("Controller {Name} stopped", controller.Name);
                return Results.Json(StateOf(controller), Json);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });

        app.MapGet("/api/tags", (string? filter, int? skip, int? take) =>
        {
            var all = controller.Tags(string.IsNullOrWhiteSpace(filter) ? null : filter).ToList();
            var s = Math.Max(0, skip ?? 0);
            var t = Math.Clamp(take ?? 100, 1, options.MaxPageSize);
            return Results.Json(new { total = all.Count, skip = s, take = t, items = all.Skip(s).Take(t) }, Json);
        });

        app.MapGet("/api/tag", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return Error("path is required");
            return controller.TryRead(path, out var value) && value is not null
                ? Results.Json(value, Json)
                : Results.Json(new { error = $"tag '{path}' not found" }, Json, statusCode: StatusCodes.Status404NotFound);
        });

        app.MapPut("/api/tag", async (HttpRequest request) =>
        {
            WriteRequest? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<WriteRequest>(request.Body, Json);
            }
            catch (JsonException ex)
            {
                return Error("invalid JSON: " + ex.Message);
            }
            if (body is null || string.IsNullOrWhiteSpace(body.Path)) return Error("path is required");
            if (!controller.TryWrite(body.Path, body.Text ?? "", out var error)) return Error(error ?? "write failed");
            return controller.TryRead(body.Path, out var value) && value is not null ? Results.Json(value, Json) : Results.Ok();
        });

        app.MapGet("/api/snapshot", () =>
        {
            var ms = new MemoryStream();
            controller.SaveSnapshot(ms);
            ms.Position = 0;
            var name = $"{Safe(controller.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.snapshot";
            return Results.File(ms, "application/octet-stream", name);
        });

        app.MapPost("/api/snapshot", async (HttpRequest request) =>
        {
            try
            {
                Stream source;
                if (request.HasFormContentType)
                {
                    var form = await request.ReadFormAsync();
                    var file = form.Files.FirstOrDefault();
                    if (file is null) return Error("no file in the upload");
                    source = file.OpenReadStream();
                }
                else
                {
                    source = request.Body;
                }
                var ms = new MemoryStream();
                await source.CopyToAsync(ms);
                if (ms.Length == 0) return Error("empty snapshot");
                ms.Position = 0;
                controller.LoadSnapshot(ms);
                log.LogInformation("Snapshot loaded from upload ({Bytes} bytes)", ms.Length);
                return Results.Json(new { loaded = true, bytes = ms.Length }, Json);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });

        app.MapGet("/api/snapshots", () =>
        {
            var dir = Directory.CreateDirectory(options.SnapshotDirectory);
            var files = dir.GetFiles("*.snapshot").OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new { name = f.Name, bytes = f.Length, modified = f.LastWriteTimeUtc });
            return Results.Json(new { directory = dir.FullName, items = files }, Json);
        });

        app.MapPost("/api/snapshots/{file}/save", (string file) =>
        {
            if (!TrySnapshotPath(options, file, out var path)) return Error("invalid snapshot name");
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                controller.SaveSnapshot(stream);
                log.LogInformation("Snapshot saved to {Path}", path);
                return Results.Json(new { saved = Path.GetFileName(path), bytes = stream.Length }, Json);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });

        app.MapPost("/api/snapshots/{file}/load", (string file) =>
        {
            if (!TrySnapshotPath(options, file, out var path)) return Error("invalid snapshot name");
            if (!File.Exists(path)) return Results.Json(new { error = $"snapshot '{file}' not found" }, Json, statusCode: StatusCodes.Status404NotFound);
            try
            {
                using var stream = File.OpenRead(path);
                controller.LoadSnapshot(stream);
                log.LogInformation("Snapshot loaded from {Path}", path);
                return Results.Json(new { loaded = Path.GetFileName(path), bytes = stream.Length }, Json);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (controller.State == ControllerState.Running)
            {
                try
                {
                    controller.Stop();
                    log.LogInformation("Controller {Name} stopped on shutdown", controller.Name);
                }
                catch (Exception ex)
                {
                    log.LogWarning("Stopping the controller on shutdown failed: {Message}", ex.Message);
                }
            }
        });

        return app;
    }

    private sealed record WriteRequest(string? Path, string? Text);

    private static object StateOf(IPlcController c) => new
    {
        name = c.Name,
        state = c.State,
        mode = c.Mode,
        statistics = c.Statistics,
        now = DateTimeOffset.Now,
    };

    private static IResult Error(string message) => Results.Json(new { error = message }, Json, statusCode: StatusCodes.Status400BadRequest);

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) || ch == ' ' ? '_' : ch).ToArray();
        return chars.Length == 0 ? "controller" : new string(chars);
    }

    private static bool TrySnapshotPath(PlcWebHostOptions options, string file, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.Contains('/') || file.Contains('\\')) return false;
        var name = file.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase) ? file : file + ".snapshot";
        var dir = Directory.CreateDirectory(options.SnapshotDirectory).FullName;
        path = Path.Combine(dir, name);
        return true;
    }

    private static string? _page;

    private static string LoadPage()
    {
        if (_page is not null) return _page;
        using var stream = typeof(PlcWebHost).Assembly.GetManifestResourceStream("Plc.Runtime.Host.index.html")
            ?? throw new InvalidOperationException("embedded page missing");
        using var reader = new StreamReader(stream);
        return _page = reader.ReadToEnd();
    }
}
