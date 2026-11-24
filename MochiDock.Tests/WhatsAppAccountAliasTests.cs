using MochiDock.Core;

public class WhatsAppAccountAliasTests
{
    private static readonly Dictionary<string, string> Aliases = new() { ["irfan-jp"] = " JP ", ["irfan-id"] = "ID", ["blank"] = "   " };

    [Fact]
    public void AnAliasReplacesTheAccountsOwnLabel()
    {
        Assert.Equal("JP", WhatsAppAccountAlias.Label(Aliases, "irfan-jp", "irfan-jp"));
        Assert.Equal("ID", WhatsAppAccountAlias.Label(Aliases, "IRFAN-ID", "irfan-id"));   // device ids match without regard to case
    }

    [Fact]
    public void WithoutAnAliasTheOwnLabelStays()
    {
        Assert.Equal("irfan-xx", WhatsAppAccountAlias.Label(Aliases, "irfan-xx", "irfan-xx"));
        Assert.Equal("work", WhatsAppAccountAlias.Label(Aliases, "blank", "work"));          // an empty alias means none
        Assert.Equal("own", WhatsAppAccountAlias.Label(null, "irfan-jp", "own"));
        Assert.Equal("own", WhatsAppAccountAlias.Label(Aliases, "", "own"));
    }

    [Fact]
    public void AliasesTravelWithTheSettingsExport() =>
        Assert.DoesNotContain("WhatsAppAccountAliases", SettingsTransfer.LocalOnlyKeys);

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  JP  ", "JP")]
    [InlineData("Work   phone", "Work phone")]
    [InlineData("A very long account name", "A very long")]
    public void AliasesAreTidiedAndKeptShort(string? input, string expected) =>
        Assert.Equal(expected, WhatsAppAccountAlias.Normalize(input));
}
