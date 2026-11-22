using System.Text.Json;
using MochiDock.Core;

public class GowaMessageParserTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ExtractContactName_FollowsPriorityOrder()
    {
        var payload = Parse("""
            {"name":"Fallback","push_name":"Push","from_name":"From","sender_display_name":"  Display  "}
            """);
        Assert.Equal("Display", GowaMessageParser.ExtractContactName(payload));

        var pushOnly = Parse("""{"name":"Fallback","push_name":"  Alice "}""");
        Assert.Equal("Alice", GowaMessageParser.ExtractContactName(pushOnly));
    }

    [Theory]
    [InlineData("""{"body":"Hello"}""", "Hello")]
    [InlineData("""{"message":{"conversation":"Hi there"}}""", "Hi there")]
    [InlineData("""{"message":{"extendedTextMessage":{"text":"Quoted reply"}}}""", "Quoted reply")]
    [InlineData("""{"message":{"imageMessage":{}}}""", "📷 [Photo]")]
    [InlineData("""{"message":{"imageMessage":{"caption":"Lunch"}}}""", "📷 Lunch")]
    [InlineData("""{"message":{"audioMessage":{}}}""", "🎤 [Voice Note]")]
    [InlineData("""{"message":{"videoMessage":{}}}""", "🎥 [Video]")]
    [InlineData("""{"message":{"videoMessage":{"caption":"Clip"}}}""", "🎥 Clip")]
    [InlineData("""{"message":{"stickerMessage":{}}}""", "🖼️ [Sticker]")]
    [InlineData("""{"message":{"documentMessage":{"fileName":"invoice.pdf"}}}""", "📄 [invoice.pdf]")]
    [InlineData("""{"message":{"documentMessage":{}}}""", "📄 [file]")]
    public void ExtractMessageText_HandlesAllPayloadShapes(string json, string expected)
    {
        Assert.Equal(expected, GowaMessageParser.ExtractMessageText(Parse(json)));
    }

    [Fact]
    public void ExtractRemoteIdAndIsFromMe_HandleBothSchemaVariants()
    {
        Assert.Equal("id-1", GowaMessageParser.ExtractRemoteId(Parse("""{"id":"id-1","key":{"id":"id-2"}}""")));
        Assert.Equal("id-2", GowaMessageParser.ExtractRemoteId(Parse("""{"key":{"id":"id-2"}}""")));
        Assert.True(GowaMessageParser.IsFromMe(Parse("""{"is_from_me":true}""")));
        Assert.True(GowaMessageParser.IsFromMe(Parse("""{"from_me":true}""")));
        Assert.False(GowaMessageParser.IsFromMe(Parse("""{"is_from_me":"not-a-bool"}""")));
    }

    [Fact]
    public void ResolveWebhookChatTarget_PrefersGroupJid()
    {
        var groupInFrom = GowaMessageParser.ResolveWebhookChatTarget("120363111@g.us", "628111@s.whatsapp.net");
        Assert.True(groupInFrom.IsGroup);
        Assert.Equal("120363111@g.us", groupInFrom.TargetChatId);

        var direct = GowaMessageParser.ResolveWebhookChatTarget("628111@s.whatsapp.net", "");
        Assert.False(direct.IsGroup);
        Assert.Equal("628111@s.whatsapp.net", direct.TargetChatId);
    }

    [Fact]
    public void FormatSender_FormatsGroupsDirectAndPhoneFallbacks()
    {
        var groupWithName = GowaMessageParser.FormatSender(true, "Family", "Budi", "Group 123", "628123456789@s.whatsapp.net");
        Assert.Equal(("Family", "Family: Budi", "Budi"), groupWithName);

        var groupFallbackPhone = GowaMessageParser.FormatSender(true, "", "", "Group 123", "628123456789@s.whatsapp.net");
        Assert.Equal(("Group", "Group: +62 812-3456-789", "+62 812-3456-789"), groupFallbackPhone);

        var direct = GowaMessageParser.FormatSender(false, "", "", "Aunt Rina", "628111@s.whatsapp.net");
        Assert.Equal(("", "Aunt Rina", "Aunt Rina"), direct);

        var directGroupPlaceholder = GowaMessageParser.FormatSender(false, "", "", "Group 123", "6281234567890@s.whatsapp.net");
        Assert.Equal(("", "+62 812-3456-7890", "+62 812-3456-7890"), directGroupPlaceholder);
    }
}
