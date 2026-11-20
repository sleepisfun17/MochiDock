using System.Text.Json;
using MochiDock.Core;
using MochiDock.Models;

public class WhatsAppChatListTests
{
    const string Json = """
    {"code":"SUCCESS","results":{"data":[
      {"jid":"628111@s.whatsapp.net","name":"Aunt Rina","last_message_time":"2026-11-14T10:00:00Z","archived":false},
      {"jid":"120363111@g.us","name":"Family","last_message_time":"2026-11-14T12:00:00Z","archived":false},
      {"jid":"120363222@g.us","name":"Group 120363222","last_message_time":"2026-11-13T09:00:00Z","archived":true},
      {"jid":"status@broadcast","name":"Status"},
      {"jid":"12345@newsletter","name":"A channel"},
      {"jid":"","name":"nobody"},
      {"jid":"99887766@lid","name":"","last_message_time":"not a time"}
    ]}}
    """;

    static IReadOnlyList<WhatsAppChatEntry> Chats()
    {
        using var doc = JsonDocument.Parse(Json);
        return WhatsAppChatList.Parse(doc.RootElement, "dev-1", "Work");
    }

    [Fact]
    public void ParsesChatsAndSkipsStatusChannelsAndEmptyIds()
    {
        var chats = Chats();
        Assert.Equal(new[] { "628111@s.whatsapp.net", "120363111@g.us", "120363222@g.us", "99887766@lid" }, chats.Select(c => c.Jid));
        Assert.True(chats[1].IsGroup);
        Assert.False(chats[0].IsGroup);
        Assert.True(chats[2].Archived);
        Assert.All(chats, c => { Assert.Equal("dev-1", c.DeviceId); Assert.Equal("Work", c.AccountName); });
        Assert.Null(chats[3].LastMessageUtc);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"results\":{\"data\":5}}")]
    [InlineData("{\"results\":[]}")]
    public void OddShapesGiveAnEmptyList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Empty(WhatsAppChatList.Parse(doc.RootElement, "d", "n"));
    }

    [Fact]
    public void DisplayNamePrefersTheNameThenTheNumber()
    {
        var chats = Chats();
        Assert.Equal("Aunt Rina", WhatsAppChatList.DisplayName(chats[0]));
        Assert.Equal("Family", WhatsAppChatList.DisplayName(chats[1]));
        Assert.Equal("Group", WhatsAppChatList.DisplayName(chats[2]));      // the server's placeholder name "Group 1203..." is not a name
        Assert.Equal("Unnamed contact", WhatsAppChatList.DisplayName(chats[3]));       // a linked id has no phone number to show
        Assert.Equal("+628111", WhatsAppChatList.DisplayName(chats[0] with { Name = "" }));
    }

    [Fact]
    public void FilterSortsNewestFirstAndFiltersByKindAndText()
    {
        var chats = Chats();
        Assert.Equal("Family", WhatsAppChatList.DisplayName(WhatsAppChatList.Filter(chats, "", WhatsAppChatKind.All)[0]));
        Assert.Equal(2, WhatsAppChatList.Filter(chats, null, WhatsAppChatKind.Groups).Count);
        Assert.Equal(2, WhatsAppChatList.Filter(chats, "", WhatsAppChatKind.People).Count);
        Assert.Single(WhatsAppChatList.Filter(chats, "rina", WhatsAppChatKind.All));
        Assert.Single(WhatsAppChatList.Filter(chats, "628111", WhatsAppChatKind.All));       // the id matches too
        Assert.Empty(WhatsAppChatList.Filter(chats, "rina", WhatsAppChatKind.Groups));
    }

    [Fact]
    public void EntryLabelShowsTheSavedNameForPlainAndAccountScopedEntries()
    {
        var names = new Dictionary<string, string> { ["120363111@g.us"] = "Family" };
        Assert.Equal("Family", WhatsAppChatList.EntryLabel("120363111@g.us", names));
        Assert.Equal("Family", WhatsAppChatList.EntryLabel("dev-1|120363111@g.us", names));
        Assert.Equal("999@g.us", WhatsAppChatList.EntryLabel("999@g.us", names));
        Assert.Equal("999@g.us", WhatsAppChatList.EntryLabel("999@g.us", null));
    }

    [Fact]
    public void PeopleCanHaveTheirOwnNotifyList()
    {
        var allowed = new[] { "628111@s.whatsapp.net" };
        Assert.False(GowaAccountPolicy.IsHiddenByAllowList(false, true, allowed, "628111@s.whatsapp.net", "d", false));   // listed person
        Assert.True(GowaAccountPolicy.IsHiddenByAllowList(false, true, allowed, "628222@s.whatsapp.net", "d", false));    // unlisted person
        Assert.False(GowaAccountPolicy.IsHiddenByAllowList(false, true, allowed, "999@g.us", "d", true));                 // groups follow the group switch
        Assert.True(GowaAccountPolicy.IsHiddenByAllowList(true, false, allowed, "999@g.us", "d", true));
        Assert.False(GowaAccountPolicy.IsHiddenByAllowList(true, false, allowed, "628222@s.whatsapp.net", "d", false));   // people follow the people switch
        Assert.False(GowaAccountPolicy.IsHiddenByAllowList(false, false, Array.Empty<string>(), "628222@s.whatsapp.net", "d", false));
        Assert.True(GowaAccountPolicy.IsHiddenByAllowList(true, true, Array.Empty<string>(), "628222@s.whatsapp.net", "d", false));
    }

    [Fact]
    public void OnePersonsPhoneIdMatchesInEveryFormButOtherChatKindsDoNot()
    {
        var entries = new[] { "628111@s.whatsapp.net" };
        Assert.True(GowaAccountPolicy.IsMuted(entries, "628111:12@s.whatsapp.net", "d"));   // the same person on another device
        Assert.True(GowaAccountPolicy.IsMuted(entries, "628111@c.us", "d"));
        Assert.False(GowaAccountPolicy.IsMuted(entries, "628111@lid", "d"));                // a linked id is a different chat to WhatsApp
        Assert.False(GowaAccountPolicy.IsMuted(new[] { "628111@g.us" }, "628111@s.whatsapp.net", "d"));
        Assert.False(GowaAccountPolicy.IsMuted(new[] { "6281110@s.whatsapp.net" }, "628111@s.whatsapp.net", "d"));
    }

    [Fact]
    public void OldSettingsWithoutTheNewKeysStillLoadWithEmptyDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WhatsAppOnlyListedGroups\":true,\"WhatsAppAllowedGroups\":[\"120363@g.us\"]}")!;
        Assert.True(settings.WhatsAppOnlyListedGroups);
        Assert.False(settings.WhatsAppOnlyListedPeople);
        Assert.NotNull(settings.WhatsAppChatNames);
        Assert.Empty(settings.WhatsAppChatNames);
    }
}
