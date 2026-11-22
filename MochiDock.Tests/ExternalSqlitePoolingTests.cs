using Microsoft.Data.Sqlite;
using Xunit;

namespace MochiDock.Tests;

public class ExternalSqlitePoolingTests
{
    [Fact]
    public void ReadOnlyUnpooledConnection_ReleasesFileHandleOnDispose_SoFileCanBeDeletedImmediately()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"mochidock-unpooled-{Guid.NewGuid():N}.db");
        try
        {
            using (var setup = new SqliteConnection($"Data Source={dbPath};Pooling=False;"))
            {
                setup.Open();
                using var cmd = setup.CreateCommand();
                cmd.CommandText = "CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT); INSERT INTO ItemTable VALUES ('k', 'v');";
                cmd.ExecuteNonQuery();
            }

            using (var ro = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False;"))
            {
                ro.Open();
                using var cmd = ro.CreateCommand();
                cmd.CommandText = "SELECT value FROM ItemTable WHERE key = 'k';";
                Assert.Equal("v", cmd.ExecuteScalar());
            }

            File.Delete(dbPath);
            Assert.False(File.Exists(dbPath));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void ExternalReadOnlySqliteServices_DisablePooling()
    {
        string root = FindRepoRoot();
        string aiUsage = File.ReadAllText(Path.Combine(root, "MochiDock", "Services", "AiUsageService.cs"));
        string mailspring = File.ReadAllText(Path.Combine(root, "MochiDock", "Services", "MailspringMailService.cs"));

        Assert.Contains("Mode=ReadOnly;Pooling=False;", aiUsage);
        Assert.DoesNotContain("Mode=ReadOnly;\"", aiUsage);
        Assert.Contains("Mode=ReadOnly;Pooling=False;", mailspring);
        Assert.DoesNotContain("Mode=ReadOnly\"", mailspring);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "MochiDock")) && Directory.Exists(Path.Combine(dir.FullName, "MochiDock.Core")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
