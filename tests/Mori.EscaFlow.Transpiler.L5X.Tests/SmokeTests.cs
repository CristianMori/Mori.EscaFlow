using Mori.EscaFlow.Transpiler.L5X.Model;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests;

/// <summary>A minimal sanity check that the test project itself is wired up and can see the parser assembly.</summary>
public class SmokeTests
{
    [Fact]
    public void LiteralRoundTrip()
    {
        Assert.Equal("16#ff", new IntegerLiteral(255, 16).ToLogixText());
    }
}
