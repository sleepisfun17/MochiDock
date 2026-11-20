using MochiDock.Core;

public class SettingsBackupPolicyTests
{
    [Fact]
    public void NamesADayAndReadsItBack()
    {
        var name = SettingsBackupPolicy.FileName(new DateTime(2026, 11, 20, 10, 21, 0));
        Assert.Equal("settings.backup-20261120.json", name);
        Assert.Equal(new DateTime(2026, 11, 20), SettingsBackupPolicy.DayOf(name));
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("settings.json.bak")]
    [InlineData("settings.backup-2026.json")]
    [InlineData("settings.backup-20261340.json")]
    public void OtherFilesAreNotBackups(string name) => Assert.Null(SettingsBackupPolicy.DayOf(name));

    [Fact]
    public void KeepsTheNewestThreeAndLeavesOtherFilesAlone()
    {
        var names = new[] { "settings.backup-20261117.json", "settings.json", "settings.backup-20261120.json", "settings.backup-20261118.json", "settings.backup-20261119.json", "tasks.json" };
        Assert.Equal(new[] { "settings.backup-20261117.json" }, SettingsBackupPolicy.ToDelete(names));
        Assert.Empty(SettingsBackupPolicy.ToDelete(names.Take(3)));
    }
}
