using System.Globalization;
using System.Text;
using Plc.Runtime;

namespace Plc.Runtime.Host.Tests;

/// <summary>An in-memory controller with a handful of tags, enough to exercise the host.</summary>
public sealed class FakeController : IPlcController
{
    private readonly Dictionary<string, (string Type, string Value, string? Program)> _tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Run"] = ("BOOL", "FALSE", null),
        ["Count"] = ("DINT", "42", null),
        ["Speed"] = ("REAL", "1.5", null),
        ["Name"] = ("STRING", "hello", null),
        ["Program:P.Idx"] = ("DINT", "0", "P"),
        ["M1.Timer.PRE"] = ("DINT", "500", null),
    };

    public string Name => "Fake";
    public ControllerState State { get; private set; } = ControllerState.Stopped;
    public ControllerMode Mode => State == ControllerState.Running ? ControllerMode.Run : ControllerMode.Program;
    public ScanStatistics Statistics { get; } = new() { Tasks = { new TaskStatistics { Name = "Main", Kind = "continuous" } } };
    public int Starts { get; private set; }

    public void Start()
    {
        if (State == ControllerState.Running) throw new InvalidOperationException("already running");
        State = ControllerState.Running;
        Statistics.StartedAt = DateTimeOffset.Now;
        Starts++;
    }

    public void Stop()
    {
        State = ControllerState.Stopped;
    }

    public IEnumerable<TagInfo> Tags(string? filter = null)
        => _tags.Where(t => filter is null || t.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagInfo(t.Key, t.Value.Type, Array.Empty<int>(), t.Value.Program, null));

    public bool TryRead(string path, out TagValueText? value)
    {
        if (_tags.TryGetValue(path, out var t))
        {
            value = new TagValueText(path, t.Type, t.Value);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryWrite(string path, string text, out string? error)
    {
        error = null;
        if (!_tags.TryGetValue(path, out var t))
        {
            error = "not found";
            return false;
        }
        switch (t.Type)
        {
            case "DINT":
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { error = "not an integer"; return false; }
                _tags[path] = (t.Type, i.ToString(CultureInfo.InvariantCulture), t.Program);
                return true;
            case "REAL":
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { error = "not a real"; return false; }
                _tags[path] = (t.Type, f.ToString("R", CultureInfo.InvariantCulture), t.Program);
                return true;
            case "BOOL":
                var b = text.Trim().ToUpperInvariant() switch { "1" or "TRUE" => true, "0" or "FALSE" => false, _ => (bool?)null };
                if (b is null) { error = "not a boolean"; return false; }
                _tags[path] = (t.Type, b.Value ? "TRUE" : "FALSE", t.Program);
                return true;
            default:
                _tags[path] = (t.Type, text.Trim('\''), t.Program);
                return true;
        }
    }

    public void SaveSnapshot(Stream stream)
    {
        using var w = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var (k, v) in _tags) w.WriteLine($"{k}={v.Value}");
    }

    public void LoadSnapshot(Stream stream)
    {
        using var r = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        while (r.ReadLine() is { } line)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq];
            if (_tags.TryGetValue(k, out var t)) _tags[k] = (t.Type, line[(eq + 1)..], t.Program);
        }
    }
}
