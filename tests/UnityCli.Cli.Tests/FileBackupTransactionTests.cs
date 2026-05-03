using UnityCli.Protocol;

namespace UnityCli.Cli.Tests;

public sealed class FileBackupTransactionTests
{
    [Fact]
    public void RunWithBackup_WhenActionSucceeds_KeepsMutationAndDeletesBackups()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        string metaPath = path + ".meta";
        File.WriteAllText(path, "before");
        File.WriteAllText(metaPath, "guid: before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "success",
        };

        string result = FileBackupTransaction.RunWithBackup(
            path,
            "asset-test",
            () =>
            {
                File.WriteAllText(path, "after");
                File.WriteAllText(metaPath, "guid: after");
                return "ok";
            },
            options);

        Assert.Equal("ok", result);
        Assert.Equal("after", File.ReadAllText(path));
        Assert.Equal("guid: after", File.ReadAllText(metaPath));
        Assert.False(File.Exists(FileBackupTransaction.BuildBackupPath(path, "success")));
        Assert.False(File.Exists(FileBackupTransaction.BuildBackupPath(metaPath, "success")));
    }
}
