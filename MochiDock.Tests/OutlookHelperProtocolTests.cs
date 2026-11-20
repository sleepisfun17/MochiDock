using System;
using MochiDock.Core;
using Xunit;

public class OutlookHelperProtocolTests
{
    [Fact]
    public void MailMessage_RoundTripsThroughOneJsonLine()
    {
        var mail = new OutlookMailDto("ENTRY1", "Alice", "Hello \"world\"", "Line1\nLine2 日本語", "Inbox", new DateTime(2026, 11, 13, 9, 30, 0));
        string line = OutlookPipeMessage.MailMessage(mail).Encode();

        Assert.DoesNotContain("\n", line);
        var decoded = OutlookPipeMessage.TryDecode(line);
        Assert.Equal("mail", decoded?.T);
        Assert.Equal(mail, decoded?.Mail);
    }

    [Fact]
    public void CommandAndHelloMessages_CarryTheirFields()
    {
        Assert.Equal("secret", OutlookPipeMessage.TryDecode(OutlookPipeMessage.Hello("secret").Encode())?.Secret);
        var open = OutlookPipeMessage.TryDecode(OutlookPipeMessage.Open("E1").Encode());
        Assert.Equal(("open", "E1"), (open?.T, open?.Id));
        var read = OutlookPipeMessage.TryDecode(OutlookPipeMessage.MarkRead("E2").Encode());
        Assert.Equal(("read", "E2"), (read?.T, read?.Id));
        Assert.Equal("stop", OutlookPipeMessage.TryDecode(OutlookPipeMessage.Stop().Encode())?.T);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"t\":")]
    public void TryDecode_RejectsGarbageWithoutThrowing(string? line) =>
        Assert.Null(OutlookPipeMessage.TryDecode(line));
}

public class MailAdFilterTests
{
    [Theory]
    [InlineData("Daily Newsletter <news@x.com>", "Hi", "text", true)]
    [InlineData("shop", "Big sale", "Click to unsubscribe", true)]
    [InlineData("shop", "【PR:】春のセール", "", true)]
    [InlineData("HR System (noreply@corp.com)", "Your timesheet is due", "Please submit", false)]
    [InlineData("noreply@build.corp.com", "Build failed", "Pipeline 42 failed", false)]
    public void IsAdvertisement_FiltersPromotionsButNotNoReplyMail(string sender, string subject, string body, bool expected) =>
        Assert.Equal(expected, MailAdFilter.IsAdvertisement(sender, subject, body));

    [Fact]
    public void IsAdvertisement_OnlyLooksAtTheStartOfTheBody()
    {
        string body = new string('a', 400) + " unsubscribe";
        Assert.False(MailAdFilter.IsAdvertisement("someone", "Meeting", body));
    }

    [Fact]
    public void ConfigCarriesTheOnlyListedScopeAndOldPeersSeeNoFlag()
    {
        var only = OutlookPipeMessage.TryDecode(OutlookPipeMessage.Config("案件", true).Encode());
        Assert.Equal("案件", only?.Msg);
        Assert.True(only?.OnlyListed);

        string plain = OutlookPipeMessage.Config("案件").Encode();
        Assert.DoesNotContain("only", plain);   // the default scope sends nothing extra
        Assert.Null(OutlookPipeMessage.TryDecode(plain)?.OnlyListed);
    }
}
