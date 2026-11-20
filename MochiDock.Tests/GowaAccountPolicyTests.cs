using MochiDock.Core;

public class GowaAccountPolicyTests
{
    [Theory]
    [InlineData("628123@s.whatsapp.net", "628123@s.whatsapp.net", true)]
    [InlineData("628123@S.WHATSAPP.NET", "628123@s.whatsapp.net", true)]
    [InlineData("628123:12@s.whatsapp.net", "628123@s.whatsapp.net", true)]   // same number, device suffix differs
    [InlineData("628123@s.whatsapp.net", "628999@s.whatsapp.net", false)]
    [InlineData("work", "WORK", true)]
    [InlineData("work", "home", false)]
    [InlineData("", "628123@s.whatsapp.net", false)]
    [InlineData(null, null, false)]
    public void SameAccountMatchesByIdOrNumber(string? a, string? b, bool expected) =>
        Assert.Equal(expected, GowaAccountPolicy.SameAccount(a, b));

    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a", "b", false)]
    [InlineData("", "b", true)]    // unknown account: the event may apply
    [InlineData("a", "", true)]
    [InlineData(null, null, true)]
    public void AppliesToTreatsUnknownAsMatching(string? message, string? evt, bool expected) =>
        Assert.Equal(expected, GowaAccountPolicy.AppliesTo(message, evt));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void LabelsOnlyWithMoreThanOneAccount(int count, bool shown) =>
        Assert.Equal(shown, GowaAccountPolicy.ShowLabels(count));

    [Fact]
    public void FindMatchesDeviceIdOrJid()
    {
        var accounts = new[]
        {
            new GowaAccount("dev-1", "628111@s.whatsapp.net", "Work"),
            new GowaAccount("dev-2", "628222@s.whatsapp.net", "Home"),
        };
        Assert.Equal("Work", GowaAccountPolicy.Find(accounts, "dev-1")?.Name);
        Assert.Equal("Home", GowaAccountPolicy.Find(accounts, "628222:7@s.whatsapp.net")?.Name);
        Assert.Null(GowaAccountPolicy.Find(accounts, "nope"));
        Assert.Null(GowaAccountPolicy.Find(accounts, ""));
    }

    [Fact]
    public void ParsesDeviceManagerList()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"results\":[{\"id\":\"user-a\",\"display_name\":\"Test User\",\"state\":\"logged_in\",\"jid\":\"628000000001@s.whatsapp.net\"},{\"id\":\"user-b\",\"display_name\":\"Test User\",\"state\":\"logged_in\",\"jid\":\"810000000001@s.whatsapp.net\"},{\"id\":\"old\",\"state\":\"logged_out\",\"jid\":\"\"}]}");
        var accounts = GowaAccountPolicy.ParseAccounts(doc.RootElement.GetProperty("results"));
        Assert.Equal(new[] { "user-a", "user-b" }, accounts.Select(a => a.DeviceId).ToArray());
        Assert.Equal("810000000001@s.whatsapp.net", accounts[1].Jid);
        Assert.Equal("Test User", accounts[0].Name);
    }

    [Fact]
    public void ParsesOldSingleSessionListAndCollapsesSameNumber()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"results\":[{\"name\":\"Me\",\"device\":\"628111:5@s.whatsapp.net\",\"jid\":\"628111:5@s.whatsapp.net\"},{\"name\":\"Me\",\"device\":\"628111:9@s.whatsapp.net\",\"jid\":\"628111:9@s.whatsapp.net\"}]}");
        var accounts = GowaAccountPolicy.ParseAccounts(doc.RootElement.GetProperty("results"));
        Assert.Single(accounts);
        Assert.Equal("628111:5@s.whatsapp.net", accounts[0].DeviceId);
    }

    [Fact]
    public void ParseIgnoresNonArrays()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"results\":{}}");
        Assert.Empty(GowaAccountPolicy.ParseAccounts(doc.RootElement.GetProperty("results")));
    }

    [Fact]
    public void MuteKeyIsScopedToTheAccountOnlyWithSeveralAccounts()
    {
        Assert.Equal("dev-1|123@s.whatsapp.net", GowaAccountPolicy.MuteKey("dev-1", "123@s.whatsapp.net", 2));
        Assert.Equal("123@s.whatsapp.net", GowaAccountPolicy.MuteKey("dev-1", "123@s.whatsapp.net", 1));
        Assert.Equal("123@s.whatsapp.net", GowaAccountPolicy.MuteKey("", "123@s.whatsapp.net", 3));
    }

    [Fact]
    public void ScopedMuteAppliesToOneAccountAndPlainMuteToAll()
    {
        var scoped = new[] { "dev-1|123@s.whatsapp.net" };
        Assert.True(GowaAccountPolicy.IsMuted(scoped, "123@s.whatsapp.net", "dev-1"));
        Assert.False(GowaAccountPolicy.IsMuted(scoped, "123@s.whatsapp.net", "dev-2"));
        Assert.True(GowaAccountPolicy.IsMuted(scoped, "123@s.whatsapp.net", ""));       // unknown account: stay muted
        var plain = new[] { "123", "999@g.us" };
        Assert.True(GowaAccountPolicy.IsMuted(plain, "123@s.whatsapp.net", "dev-2"));   // old entries keep covering every account
        Assert.True(GowaAccountPolicy.IsMuted(plain, "999@g.us", "dev-1"));
        Assert.False(GowaAccountPolicy.IsMuted(plain, "555@s.whatsapp.net", "dev-1"));
        Assert.False(GowaAccountPolicy.IsMuted(plain, "", "dev-1"));
    }

    [Fact]
    public void OnlyListedGroupsSilencesUnlistedGroupsButNeverDirectChats()
    {
        var allowed = new[] { "120363@g.us", "dev-1|555@g.us" };
        Assert.False(GowaAccountPolicy.IsHiddenByGroupAllowList(true, allowed, "120363@g.us", "dev-2", true));   // listed for every account
        Assert.True(GowaAccountPolicy.IsHiddenByGroupAllowList(true, allowed, "999@g.us", "dev-1", true));       // not listed
        Assert.False(GowaAccountPolicy.IsHiddenByGroupAllowList(true, allowed, "555@g.us", "dev-1", true));      // listed for this account
        Assert.True(GowaAccountPolicy.IsHiddenByGroupAllowList(true, allowed, "555@g.us", "dev-2", true));       // listed for another account only
        Assert.False(GowaAccountPolicy.IsHiddenByGroupAllowList(true, allowed, "628123@s.whatsapp.net", "dev-1", false));  // a direct chat
        Assert.False(GowaAccountPolicy.IsHiddenByGroupAllowList(false, allowed, "999@g.us", "dev-1", true));     // the mode is off
        Assert.True(GowaAccountPolicy.IsHiddenByGroupAllowList(true, Array.Empty<string>(), "999@g.us", "dev-1", true));   // empty list: no group notifies
    }

    [Fact]
    public void ReadIdsAreThoseOfTheAnsweredChatOnThatAccountOnly()
    {
        var messages = new[]
        {
            ("a1", "628111@s.whatsapp.net", "dev1"),
            ("a2", "628111@s.whatsapp.net", "dev1"),
            ("a2", "628111@s.whatsapp.net", "dev1"),   // the same id twice
            ("", "628111@s.whatsapp.net", "dev1"),     // no server id
            ("b1", "628222@s.whatsapp.net", "dev1"),   // another chat
            ("c1", "628111@s.whatsapp.net", "dev2"),   // the same chat on another account
        };
        Assert.Equal(new[] { "a1", "a2" }, WhatsAppReadIds.Pick(messages, "628111@S.whatsapp.net", "dev1"));
        Assert.Empty(WhatsAppReadIds.Pick(messages, "", "dev1"));
    }

    [Fact]
    public void ReadIdsAreCapped()
    {
        var many = Enumerable.Range(0, 50).Select(i => ($"m{i}", "x@s.whatsapp.net", "d")).ToList();
        Assert.Equal(WhatsAppReadIds.Cap, WhatsAppReadIds.Pick(many, "x@s.whatsapp.net", "d").Count);
        Assert.Equal(3, WhatsAppReadIds.Pick(many, "x@s.whatsapp.net", "d", 3).Count);
        Assert.Empty(WhatsAppReadIds.Pick(many, "x@s.whatsapp.net", "d", 0));
    }
}
