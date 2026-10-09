using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Values;

/// <summary>
/// Proves <c>L5kValueParser</c> reads the untyped L5K grammar correctly: scalars in every radix, nested and
/// deeply nested lists, string escapes, empty slots and lists, and that malformed input (unterminated strings
/// or lists, junk tokens, trailing garbage) is reported as a diagnostic and recovered from rather than thrown.
/// </summary>
public class L5kValueParserTests
{
    private static (L5kValue Value, DiagnosticBag Diagnostics) Parse(string text)
    {
        var bag = new DiagnosticBag();
        var v = L5kValueParser.Parse(text, bag);
        return (v, bag);
    }

    [Fact]
    public void TopLevelScalar()
    {
        var (v, d) = Parse("21");
        Assert.Empty(d);
        Assert.Equal(new L5kInteger(21), v);
    }

    [Fact]
    public void NegativeAndPositiveSign()
    {
        Assert.Equal(new L5kInteger(-5), Parse("-5").Value);
        Assert.Equal(new L5kInteger(7), Parse("+7").Value);
    }

    [Fact]
    public void RealWithExponent()
    {
        var (v, d) = Parse("0.00000000e+000");
        Assert.Empty(d);
        var r = Assert.IsType<L5kReal>(v);
        Assert.Equal(0.0, r.Value);

        var r2 = Assert.IsType<L5kReal>(Parse("1.50000000e+002").Value);
        Assert.Equal(150.0, r2.Value);
        var r3 = Assert.IsType<L5kReal>(Parse("-2.5").Value);
        Assert.Equal(-2.5, r3.Value);
    }

    [Fact]
    public void RadixInteger()
    {
        var (v, d) = Parse("16#00ff");
        Assert.Empty(d);
        var i = Assert.IsType<L5kInteger>(v);
        Assert.Equal(255, i.Value);
        Assert.Equal("16#", i.RadixText);

        var b = Assert.IsType<L5kInteger>(Parse("2#0000_1010").Value);
        Assert.Equal(10, b.Value);
        Assert.Equal("2#", b.RadixText);
    }

    [Fact]
    public void NestedListsWithExportWhitespace()
    {
        var text = "[0,0,0\r\n\t\t,0,0]";
        var (v, d) = Parse(text);
        Assert.Empty(d);
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal(5, list.Items.Count);
        Assert.All(list.Items, item => Assert.Equal(new L5kInteger(0), item));
    }

    [Fact]
    public void DeeplyNestedLists()
    {
        var text = "[298123,0,73,0,0,3918,[0,0,0],73,3918,[[0,0,0,0,0,0,0],[0,'$00$00$00'],0,0,3918,73],[0,0,0]]";
        var (v, d) = Parse(text);
        Assert.Empty(d);
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal(11, list.Items.Count);
        var inner = Assert.IsType<L5kList>(list.Items[9]);
        Assert.Equal(6, inner.Items.Count);
        var sub = Assert.IsType<L5kList>(inner.Items[1]);
        var s = Assert.IsType<L5kString>(sub.Items[1]);
        Assert.Equal("\0\0\0", s.Value);
    }

    [Fact]
    public void StringWithEscapes()
    {
        var (v, d) = Parse("[24,'abc$00$00']");
        Assert.Empty(d);
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal(new L5kInteger(24), list.Items[0]);
        Assert.Equal("abc\0\0", Assert.IsType<L5kString>(list.Items[1]).Value);
    }

    [Fact]
    public void StringWithQuoteAndDollarEscapes()
    {
        var (v, d) = Parse("'it$'s $$5'");
        Assert.Empty(d);
        Assert.Equal("it's $5", Assert.IsType<L5kString>(v).Value);
    }

    [Fact]
    public void EmptyList()
    {
        var (v, d) = Parse("[]");
        Assert.Empty(d);
        var list = Assert.IsType<L5kList>(v);
        Assert.Empty(list.Items);
    }

    [Fact]
    public void EmptyInput()
    {
        var (v, d) = Parse("   \r\n\t ");
        Assert.Empty(d);
        Assert.IsType<L5kList>(v);
    }

    [Fact]
    public void UnterminatedListReportsL5K002()
    {
        var (v, d) = Parse("[1,2");
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal(2, list.Items.Count);
        Assert.Contains(d, x => x.Code == L5kValueParser.CodeUnterminated);
    }

    [Fact]
    public void JunkTokenReportsL5K001()
    {
        var (v, d) = Parse("[1,2#zz,3]");
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal(3, list.Items.Count);
        var raw = Assert.IsType<L5kRaw>(list.Items[1]);
        Assert.Equal("2#zz", raw.Text);
        var diag = Assert.Single(d, x => x.Code == L5kValueParser.CodeBadToken);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal(3, diag.Location.Column);
    }

    [Fact]
    public void BareIdentifierIsRawWithoutDiagnostic()
    {
        var (v, d) = Parse("[NONE,1]");
        Assert.Empty(d);
        var list = Assert.IsType<L5kList>(v);
        Assert.Equal("NONE", Assert.IsType<L5kRaw>(list.Items[0]).Text);
    }

    [Fact]
    public void TrailingGarbageReported()
    {
        var (v, d) = Parse("[1] xyz");
        Assert.IsType<L5kList>(v);
        Assert.Contains(d, x => x.Code == L5kValueParser.CodeBadToken);
    }

    [Fact]
    public void UnterminatedStringReported()
    {
        var (v, d) = Parse("['abc");
        Assert.Contains(d, x => x.Code == L5kValueParser.CodeUnterminated);
        Assert.IsType<L5kList>(v);
    }
}
