using Xunit;

public class SampleTests
{
    [Fact]
    public void AlwaysTrue() => Assert.True(1 + 1 == 2);
}