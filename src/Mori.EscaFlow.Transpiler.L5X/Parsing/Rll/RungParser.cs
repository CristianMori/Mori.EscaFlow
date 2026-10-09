using System.Text;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>
/// Parses the neutral text of a ladder rung into a <see cref="RungAst"/>.
/// <para>
/// Grammar: <c>rung := element* ';'? EOF</c>, <c>element := branch | instruction</c>,
/// <c>branch := '[' leg (',' leg)* ']'</c>, <c>leg := element*</c>, <c>instruction := IDENT '(' args ')'</c>.
/// </para>
/// <para>
/// The scanner has exactly two states. At element level whitespace is skipped, <c>[</c> opens a branch,
/// <c>,</c> separates the legs of the innermost branch, <c>]</c> closes it, <c>;</c> ends the rung and an
/// identifier followed by <c>(</c> starts an instruction. Inside an instruction's parentheses characters are
/// copied verbatim while tracking the nesting depth of <c>(</c>/<c>[</c> against <c>)</c>/<c>]</c> and a
/// single-quoted string state (honouring <c>$'</c> and <c>$$</c>); a <c>,</c> at depth 0 ends the argument
/// and a <c>)</c> at depth 0 ends the instruction. Hence a <c>[</c> inside an argument is always a subscript.
/// </para>
/// <para>
/// Argument text is classified afterwards by <see cref="OperandParser"/>; this class only finds the boundaries.
/// Diagnostics are reported as warnings rather than errors because a damaged rung must not stop the import of
/// the rest of the project: the parser resynchronizes at the next element and returns what it understood.
/// </para>
/// Thread-safe: the parser holds no per-parse state. Never throws on bad input.
/// </summary>
public sealed class RungParser
{
    private readonly LogicParseContext _context;

    public RungParser(LogicParseContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public LogicParseContext Context => _context;

    /// <summary>Parses one rung's text. Blank text yields <see cref="RungAst.Empty"/> (an empty rung is legal: a plain wire).</summary>
    public RungAst Parse(string text, IDiagnosticSink diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text)) return RungAst.Empty;
        var state = new State(text, _context, diagnostics);
        var elements = state.ParseElements(inBranch: false);
        return new RungAst(elements);
    }

    /// <summary>Per-parse cursor over the rung text; one instance per <see cref="Parse"/> call, which is what keeps the outer class thread-safe.</summary>
    private sealed class State
    {
        private readonly string _text;
        private readonly LogicParseContext _context;
        private readonly IDiagnosticSink _diag;

        // Unknown mnemonics are reported once per rung, not once per occurrence.
        private readonly HashSet<string> _reportedUnknown = new(StringComparer.OrdinalIgnoreCase);
        private int _pos;
        private bool _terminated;

        public State(string text, LogicParseContext context, IDiagnosticSink diag)
        {
            _text = text;
            _context = context;
            _diag = diag;
        }

        private bool AtEnd => _pos >= _text.Length;

        /// <summary>
        /// Parses elements until the end of the text or, inside a branch, until the <c>,</c> or <c>]</c> that
        /// ends the current leg (left for the caller to consume).
        /// </summary>
        public List<RungElement> ParseElements(bool inBranch)
        {
            var elements = new List<RungElement>();
            while (true)
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    if (inBranch) Error("RLL004", "Unterminated branch: expected ']' before end of rung");
                    return elements;
                }

                var c = _text[_pos];
                switch (c)
                {
                    case '[':
                        elements.Add(ParseBranch());
                        continue;
                    case ',':
                    case ']':
                        if (inBranch) return elements;
                        // Stray separators at rung level: report and skip so the rest still parses.
                        Error("RLL003", $"Unexpected '{c}' outside a branch at offset {_pos}");
                        _pos++;
                        continue;
                    case ';':
                        if (inBranch)
                        {
                            Error("RLL004", "Unterminated branch: found ';' before ']'");
                            return elements;
                        }
                        _pos++;
                        _terminated = true;
                        SkipWhitespace();
                        if (!AtEnd) Error("RLL003", $"Text after rung terminator ';' at offset {_pos}");
                        continue;
                }

                if (IsIdentStart(c))
                {
                    var start = _pos;
                    while (!AtEnd && IsIdentPart(_text[_pos])) _pos++;
                    var mnemonic = _text[start.._pos];
                    var afterName = _pos;
                    SkipWhitespace();
                    if (!AtEnd && _text[_pos] == '(')
                    {
                        elements.Add(ParseInstruction(start, mnemonic));
                    }
                    else
                    {
                        // An identifier without an argument list is not an instruction; drop it and resynchronize.
                        _pos = afterName;
                        Error("RLL003", $"Expected '(' after '{mnemonic}' at offset {afterName}");
                    }
                    continue;
                }

                Error("RLL003", $"Unexpected character '{c}' at offset {_pos}");
                _pos++;
            }
        }

        /// <summary>Parses <c>[leg, leg, ...]</c>; the cursor is on the opening bracket.</summary>
        private BranchElement ParseBranch()
        {
            var offset = _pos;
            _pos++; // '['
            var legs = new List<IReadOnlyList<RungElement>>();
            while (true)
            {
                var leg = ParseElements(inBranch: true);
                legs.Add(leg);
                if (AtEnd) break;
                var c = _text[_pos];
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == ']')
                {
                    _pos++;
                    break;
                }
                // ';' inside a branch: diagnostic already raised by ParseElements; leave it for the caller.
                break;
            }
            return new BranchElement(offset, legs);
        }

        /// <summary>
        /// Scans the argument list of an instruction (cursor on the opening parenthesis), splitting on top-level
        /// commas, then classifies each raw argument. Strings and nested brackets keep their commas.
        /// </summary>
        private InstructionElement ParseInstruction(int offset, string mnemonic)
        {
            _pos++; // '('
            var rawArgs = new List<string>();
            var sb = new StringBuilder();
            var depth = 0;
            var inString = false;
            var sawComma = false;
            var closed = false;

            while (!AtEnd)
            {
                var c = _text[_pos];
                if (inString)
                {
                    // '$' escapes the next character ($' $$ $N $R $T $L $P and $hh), so a quote after '$' does not end the string.
                    if (c == '$' && _pos + 1 < _text.Length)
                    {
                        sb.Append(c).Append(_text[_pos + 1]);
                        _pos += 2;
                        continue;
                    }
                    if (c == '\'') inString = false;
                    sb.Append(c);
                    _pos++;
                    continue;
                }

                switch (c)
                {
                    case '\'':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '(':
                    case '[':
                        depth++;
                        sb.Append(c);
                        break;
                    case ')':
                        if (depth == 0)
                        {
                            closed = true;
                            _pos++;
                            goto done;
                        }
                        depth--;
                        sb.Append(c);
                        break;
                    case ']':
                        if (depth > 0) depth--;
                        sb.Append(c);
                        break;
                    case ',':
                        if (depth == 0)
                        {
                            rawArgs.Add(sb.ToString());
                            sb.Clear();
                            sawComma = true;
                            break;
                        }
                        sb.Append(c);
                        break;
                    case ';':
                        if (depth == 0)
                        {
                            // A rung terminator inside an argument list means the ')' is missing.
                            goto done;
                        }
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
                _pos++;
            }

        done:
            if (inString) Error("RLL004", $"Unterminated string in {mnemonic} at offset {offset}");
            if (!closed) Error("RLL004", $"Unterminated instruction {mnemonic}: expected ')' at offset {offset}");

            // Zero-argument form "AFI()" vs. a trailing empty argument "MOV(a,)".
            if (sawComma || sb.Length > 0 || rawArgs.Count > 0) rawArgs.Add(sb.ToString());

            // Add-on instructions take precedence over the built-in table: a user may name an add-on like a
            // built-in that this table does not know, and the add-on's parameter list is the authority then.
            var isAddOn = _context.IsAddOnInstruction(mnemonic);
            if (!isAddOn && !InstructionSignatures.IsKnown(mnemonic) && _reportedUnknown.Add(mnemonic))
                _diag.Info("RLL001", $"Unknown instruction '{mnemonic}'");

            var operands = new Operand[rawArgs.Count];
            for (var i = 0; i < rawArgs.Count; i++)
                operands[i] = OperandParser.Classify(mnemonic, i, rawArgs[i], isAddOn, _context, _diag);

            return new InstructionElement(offset, mnemonic, operands, isAddOn);
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        /// <summary>Rung-level problems are warnings: the rung is kept with whatever was understood. Location is attached by the caller's scoped sink.</summary>
        private void Error(string code, string message) => _diag.Warning(code, message, DiagnosticLocation.None);

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
