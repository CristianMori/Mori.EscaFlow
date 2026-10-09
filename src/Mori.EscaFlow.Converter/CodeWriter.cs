using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>Indented text builder for generated source files.</summary>
/// <remarks>
/// Converters emit nested blocks (classes, methods, IF bodies, loops); the writer tracks the indentation so emitters
/// only decide <em>what</em> to write. Blocks are closed by disposing the scope returned from <see cref="Block"/>, which
/// keeps opening and closing braces paired even when an emitter returns early from the middle of a block.
/// Lines always end with a bare newline; the output is written as-is, so generated files use LF endings on every platform.
/// </remarks>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    /// <summary>True when nothing has been written on the current line yet, so the next write must emit the indentation.</summary>
    private bool _lineStart = true;

    /// <summary>The text written once per indentation level (four spaces, the C# convention).</summary>
    public string IndentUnit { get; init; } = "    ";

    /// <summary>The current nesting depth.</summary>
    public int Indent => _indent;

    /// <summary>Writes one line at the current indentation; an empty argument writes a blank line without trailing spaces.</summary>
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

    /// <summary>Appends text to the current line, emitting the indentation first when the line is still empty.</summary>
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

    /// <summary>Writes several lines at the current indentation.</summary>
    public CodeWriter Lines(IEnumerable<string> lines)
    {
        foreach (var l in lines) Line(l);
        return this;
    }

    /// <summary>Opens a brace block on its own line and indents; dispose the returned scope to close it.</summary>
    /// <param name="header">The line preceding the opening brace (a class declaration, an <c>if (...)</c>, ...).</param>
    /// <param name="close">The closing text; <c>};</c> for switch-expression and initializer blocks.</param>
    public IDisposable Block(string header, string close = "}")
    {
        Line(header);
        Line("{");
        _indent++;
        return new Scope(this, close);
    }

    /// <summary>Opens a bare brace block (no header); dispose the returned scope to close it.</summary>
    /// <remarks>Used to give loop helpers their own C# scope so repeated loops in one method can reuse local names.</remarks>
    public IDisposable BareBlock()
    {
        Line("{");
        _indent++;
        return new Scope(this, "}");
    }

    /// <summary>Indents without braces (switch case bodies); dispose to outdent.</summary>
    public IDisposable IndentScope()
    {
        _indent++;
        return new Scope(this, null);
    }

    public override string ToString() => _sb.ToString();

    /// <summary>Outdents and optionally writes the closing text when disposed.</summary>
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
