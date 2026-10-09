using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Mori.EscaFlow.Runtime.Host.Tests;

/// <summary>Boots <see cref="PlcWebHost"/> once over a <see cref="FakeController"/> and a temporary snapshot directory, shared by every test in the class.</summary>
public sealed class HostFixture : IAsyncLifetime
{
    public FakeController Controller { get; } = new();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string SnapshotDir { get; } = Path.Combine(Path.GetTempPath(), "plc-host-tests-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        App = PlcWebHost.CreateApp(Controller, new PlcWebHostOptions { SnapshotDirectory = SnapshotDir }, b => b.WebHost.UseTestServer());
        await App.StartAsync();
        Client = App.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        try { Directory.Delete(SnapshotDir, true); } catch { }
    }
}

/// <summary>The JSON API end to end: state polling, start/stop, paged and filtered tag listing, tag read/write, and both snapshot transports (download/upload and named server-side files, including path traversal rejection).</summary>
public class PlcWebHostTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _fx;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PlcWebHostTests(HostFixture fx) => _fx = fx;

    [Fact]
    public async Task IndexPageIsServed()
    {
        var html = await _fx.Client.GetStringAsync("/");
        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/state", html);
        Assert.Contains("/api/tags", html);
    }

    [Fact]
    public async Task StartAndStopChangeState()
    {
        var state = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/state", Json);
        Assert.Equal("Fake", state.GetProperty("name").GetString());

        var started = await _fx.Client.PostAsync("/api/start", null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var s1 = await started.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("running", s1.GetProperty("state").GetString());
        Assert.Equal("run", s1.GetProperty("mode").GetString());
        Assert.Equal("Main", s1.GetProperty("statistics").GetProperty("tasks")[0].GetProperty("name").GetString());

        var again = await _fx.Client.PostAsync("/api/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var err = await again.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("already", err.GetProperty("error").GetString());

        var stopped = await _fx.Client.PostAsync("/api/stop", null);
        var s2 = await stopped.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("stopped", s2.GetProperty("state").GetString());
    }

    [Fact]
    public async Task TagsAreListedFilteredAndPaged()
    {
        var all = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tags", Json);
        Assert.Equal(6, all.GetProperty("total").GetInt32());
        Assert.Equal(6, all.GetProperty("items").GetArrayLength());

        var page = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tags?skip=2&take=2", Json);
        Assert.Equal(6, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal("Name", page.GetProperty("items")[0].GetProperty("path").GetString());

        var filtered = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tags?filter=program", Json);
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal("P", filtered.GetProperty("items")[0].GetProperty("program").GetString());
    }

    [Fact]
    public async Task TagReadAndWrite()
    {
        var count = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tag?path=Count", Json);
        Assert.Equal("42", count.GetProperty("text").GetString());
        Assert.Equal("DINT", count.GetProperty("type").GetString());

        var missing = await _fx.Client.GetAsync("/api/tag?path=Nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var written = await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Count", text = "7" });
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        var after = await written.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("7", after.GetProperty("text").GetString());

        var bad = await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Count", text = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var err = await bad.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("not an integer", err.GetProperty("error").GetString());

        var noPath = await _fx.Client.PutAsJsonAsync("/api/tag", new { text = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, noPath.StatusCode);
    }

    [Fact]
    public async Task SnapshotDownloadAndUploadRoundTrip()
    {
        await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Speed", text = "2.25" });
        var download = await _fx.Client.GetAsync("/api/snapshot");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.EndsWith(".snapshot", download.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Contains("Speed=2.25", System.Text.Encoding.UTF8.GetString(bytes));

        await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Speed", text = "0" });
        var upload = await _fx.Client.PostAsync("/api/snapshot", new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var speed = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tag?path=Speed", Json);
        Assert.Equal("2.25", speed.GetProperty("text").GetString());

        // multipart upload
        await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Speed", text = "0" });
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "x.snapshot" } };
        var upload2 = await _fx.Client.PostAsync("/api/snapshot", form);
        Assert.Equal(HttpStatusCode.OK, upload2.StatusCode);
        speed = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tag?path=Speed", Json);
        Assert.Equal("2.25", speed.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ServerSideSnapshotsSaveListAndLoad()
    {
        await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Name", text = "saved" });
        var save = await _fx.Client.PostAsync("/api/snapshots/first/save", null);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.True(File.Exists(Path.Combine(_fx.SnapshotDir, "first.snapshot")));

        var list = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/snapshots", Json);
        Assert.Contains(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("name").GetString() == "first.snapshot");

        await _fx.Client.PutAsJsonAsync("/api/tag", new { path = "Name", text = "changed" });
        var load = await _fx.Client.PostAsync("/api/snapshots/first.snapshot/load", null);
        Assert.Equal(HttpStatusCode.OK, load.StatusCode);
        var name = await _fx.Client.GetFromJsonAsync<JsonElement>("/api/tag?path=Name", Json);
        Assert.Equal("saved", name.GetProperty("text").GetString());

        var missing = await _fx.Client.PostAsync("/api/snapshots/nothing/load", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var traversal = await _fx.Client.PostAsync("/api/snapshots/..%2Fevil/save", null);
        Assert.NotEqual(HttpStatusCode.OK, traversal.StatusCode);
    }
}
