using Mori.EscaFlow.Transpiler.L5X.Model;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests;

public class SmokeTests
{
    [Fact]
    public void LiteralRoundTrip()
    {
        Assert.Equal("16#ff", new IntegerLiteral(255, 16).ToLogixText());
    }
}
