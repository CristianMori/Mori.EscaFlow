using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>Indented text builder for generated source files.</summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _lineStart = true;

    public string IndentUnit { get; init; } = "    ";
    public int Indent => _indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            _sb.Append('\n');
            _lineStart = true;
            return this;
        }
        Write(text);
        _sb.Append('\n');
        _lineStart = true;
        return this;
    }

    public CodeWriter Write(string text)
    {
        if (_lineStart && text.Length > 0)
        {
            for (var i = 0; i < _indent; i++) _sb.Append(IndentUnit);
            _lineStart = false;
        }
        _sb.Append(text);
        return this;
    }

    public CodeWriter Lines(IEnumerable<string> lines)
    {
        foreach (var l in lines) Line(l);
        return this;
    }

    /// <summary>Opens a brace block on its own line and indents; dispose the returned scope to close it.</summary>
    public IDisposable Block(string header, string close = "}")
    {
        Line(header);
        Line("{");
        _indent++;
        return new Scope(this, close);
    }

    /// <summary>Opens a bare brace block (no header); dispose the returned scope to close it.</summary>
    public IDisposable BareBlock()
    {
        Line("{");
        _indent++;
        return new Scope(this, "}");
    }

    public IDisposable IndentScope()
    {
        _indent++;
        return new Scope(this, null);
    }

    public override string ToString() => _sb.ToString();

    private sealed class Scope : IDisposable
    {
        private readonly CodeWriter _w;
        private readonly string? _close;
        public Scope(CodeWriter w, string? close) { _w = w; _close = close; }
        public void Dispose()
        {
            _w._indent--;
            if (_close is not null) _w.Line(_close);
        }
    }
}
