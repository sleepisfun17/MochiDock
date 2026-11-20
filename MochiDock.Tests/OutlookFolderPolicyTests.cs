using MochiDock.Core;
using Xunit;

public class OutlookFolderPolicyTests
{
    [Fact]
    public void JoinAndSplit_RoundTripAndDropBlanksAndDuplicates()
    {
        string joined = OutlookFolderPolicy.Join(new[] { " Clients ", "", "clients", "案件", "a|b" });
        Assert.Equal("Clients|案件", joined);
        Assert.Equal(new[] { "Clients", "案件" }, OutlookFolderPolicy.Split(joined));
        Assert.Empty(OutlookFolderPolicy.Split(null));
    }

    [Theory]
    [InlineData("Clients", true)]
    [InlineData("clients", true)]
    [InlineData("案件", true)]
    [InlineData("Client", false)]
    [InlineData("Others 2", false)]
    [InlineData("", false)]
    public void IsTrackedName_MatchesWholeNamesOnly(string name, bool expected) =>
        Assert.Equal(expected, OutlookFolderPolicy.IsTrackedName(name, new[] { "案件", "Clients", "Others" }));

    [Fact]
    public void NothingIsListedBeforeTheUserListsFolders() =>
        Assert.Empty(OutlookFolderPolicy.DefaultTrackedFolders);

    [Theory]
    [InlineData("Inbox", true)]
    [InlineData("受信トレイ", true)]
    [InlineData("Sent Items", false)]
    public void LooksLikeInbox_RecognisesEnglishAndJapanese(string name, bool expected) =>
        Assert.Equal(expected, OutlookFolderPolicy.LooksLikeInbox(name));

    [Theory]
    // name, isInbox, inside, onlyListed -> watched
    [InlineData("受信トレイ", true, false, false, true)]      // default scope: the Inbox is watched
    [InlineData("株式会社サンプル", false, true, false, true)]   // default scope: below the Inbox
    [InlineData("株式会社サンプル", false, false, false, false)] // not inside, not listed
    [InlineData("案件", false, false, false, true)]          // listed
    [InlineData("受信トレイ", true, false, true, false)]       // only listed: the Inbox itself is ignored
    [InlineData("株式会社サンプル", false, false, true, false)]  // only listed: a sibling folder below the Inbox is ignored
    [InlineData("案件", false, false, true, true)]           // only listed: the listed folder is watched
    [InlineData("サブ", false, true, true, true)]              // only listed: its subfolders are watched
    [InlineData("Inbox", false, false, true, false)]           // only listed: a folder merely named like an inbox is ignored
    public void IsWatched_AppliesTheChosenScope(string name, bool isInbox, bool inside, bool onlyListed, bool expected) =>
        Assert.Equal(expected, OutlookFolderPolicy.IsWatched(name, isInbox, inside, new[] { "案件" }, onlyListed));
}
