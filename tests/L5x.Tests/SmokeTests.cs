using L5x.Model;
using Xunit;

namespace L5x.Tests;

public class SmokeTests
{
    [Fact]
    public void LiteralRoundTrip()
    {
        Assert.Equal("16#ff", new IntegerLiteral(255, 16).ToLogixText());
    }
}
