using MochiDock.Core;
namespace MochiDock.Tests;
public class GowaRemotePolicyTests
{
    [Fact] public void BasicHeaderEncodesUserAndPassword() =>
        Assert.Equal("Basic dXNlcjpwYXNz", GowaRemotePolicy.BasicAuthHeader("user", "pass"));

    [Fact] public void BasicHeaderAllowsEmptyPasswordAndRejectsMissingUser()
    {
        Assert.Equal("Basic dXNlcjo=", GowaRemotePolicy.BasicAuthHeader("user", null));
        Assert.Null(GowaRemotePolicy.BasicAuthHeader("", "pass"));
        Assert.Null(GowaRemotePolicy.BasicAuthHeader("  ", "pass"));
    }

    [Theory]
    [InlineData("http://100.64.0.1:3099")] [InlineData("100.101.5.6:3099")] [InlineData("http://192.168.1.20:3099")]
    [InlineData("http://10.0.0.5")] [InlineData("http://172.20.1.1:3099")] [InlineData("http://127.0.0.1:3099")]
    [InlineData("http://localhost:3099")] [InlineData("http://gowa-server:3099")] [InlineData("http://box.local:3099")]
    [InlineData("https://gowa.tail1234.ts.net")]
    public void PrivateAddresses(string url) => Assert.True(GowaRemotePolicy.IsPrivateAddress(url));

    [Theory]
    [InlineData("http://8.8.8.8:3099")] [InlineData("https://gowa.example.com")] [InlineData("http://172.32.0.1")]
    [InlineData("http://100.128.0.1")] [InlineData("")] [InlineData(null)]
    public void PublicOrInvalidAddresses(string? url) => Assert.False(GowaRemotePolicy.IsPrivateAddress(url));
}
