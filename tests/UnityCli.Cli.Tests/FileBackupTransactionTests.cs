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

    [Fact]
    public void RunWithBackup_WhenActionFails_RestoresBodyAndMeta()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        string metaPath = path + ".meta";
        File.WriteAllText(path, "before");
        File.WriteAllText(metaPath, "guid: before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "middlefail",
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FileBackupTransaction.RunWithBackup<object?>(
                path,
                "asset-test",
                () =>
                {
                    File.WriteAllText(path, "partial");
                    File.WriteAllText(metaPath, "guid: partial");
                    throw new InvalidOperationException("boom");
                },
                options));

        Assert.Equal("boom", exception.Message);
        Assert.Equal("before", File.ReadAllText(path));
        Assert.Equal("guid: before", File.ReadAllText(metaPath));
        Assert.False(File.Exists(FileBackupTransaction.BuildBackupPath(path, "middlefail")));
        Assert.False(File.Exists(FileBackupTransaction.BuildBackupPath(metaPath, "middlefail")));
    }

    [Fact]
    public void RunWithBackup_WhenOriginalMetaIsMissing_RemovesPartialMetaOnFailure()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        string metaPath = path + ".meta";
        File.WriteAllText(path, "before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "missingmeta",
        };

        Assert.Throws<InvalidOperationException>(() =>
            FileBackupTransaction.RunWithBackup<object?>(
                path,
                "asset-test",
                () =>
                {
                    File.WriteAllText(path, "partial");
                    File.WriteAllText(metaPath, "guid: partial");
                    throw new InvalidOperationException("boom");
                },
                options));

        Assert.Equal("before", File.ReadAllText(path));
        Assert.False(File.Exists(metaPath));
    }

    [Fact]
    public void RunWithMovedBackup_WhenActionFails_RestoresExistingTargetAndDeletesGeneratedBackupMeta()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        string metaPath = path + ".meta";
        File.WriteAllText(path, "before");
        File.WriteAllText(metaPath, "guid: before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "movedfail",
        };

        string backupPath = FileBackupTransaction.BuildBackupPath(path, "movedfail");
        string generatedBackupMetaPath = backupPath + ".meta";

        Assert.Throws<InvalidOperationException>(() =>
            FileBackupTransaction.RunWithMovedBackup<object?>(
                path,
                "asset-test",
                () =>
                {
                    Assert.False(File.Exists(path));
                    Assert.True(File.Exists(backupPath));
                    File.WriteAllText(generatedBackupMetaPath, "unity generated meta");
                    File.WriteAllText(path, "partial");
                    File.WriteAllText(metaPath, "guid: partial");
                    throw new InvalidOperationException("boom");
                },
                options));

        Assert.Equal("before", File.ReadAllText(path));
        Assert.Equal("guid: before", File.ReadAllText(metaPath));
        Assert.False(File.Exists(backupPath));
        Assert.False(File.Exists(generatedBackupMetaPath));
    }

    [Fact]
    public void RunWithBackup_WhenBackupCreationFails_ThrowsBackupFailed()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        File.WriteAllText(path, "before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "bad/token",
        };

        FileBackupTransactionException exception = Assert.Throws<FileBackupTransactionException>(() =>
            FileBackupTransaction.RunWithBackup(
                path,
                "asset-test",
                () => "unreachable",
                options));

        Assert.Equal(ProtocolConstants.ErrorBackupFailed, exception.ErrorCode);
        Assert.Contains("asset-test 백업 생성에 실패했습니다", exception.Message);
    }

    [Fact]
    public void RunWithBackup_WhenRestoreFails_ThrowsBackupRestoreFailedWithBackupLocation()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "Thing.asset");
        File.WriteAllText(path, "before");

        var options = new FileBackupTransactionOptions
        {
            BackupIdFactory = () => "restorefail",
        };

        string backupPath = FileBackupTransaction.BuildBackupPath(path, "restorefail");

        FileBackupTransactionException exception = Assert.Throws<FileBackupTransactionException>(() =>
            FileBackupTransaction.RunWithBackup<object?>(
                path,
                "asset-test",
                () =>
                {
                    File.WriteAllText(path, "partial");
                    File.Delete(backupPath);
                    throw new InvalidOperationException("boom");
                },
                options));

        Assert.Equal(ProtocolConstants.ErrorBackupRestoreFailed, exception.ErrorCode);
        Assert.Contains(backupPath, exception.Message);
        Assert.Contains("수동 복구", exception.Message);
    }
}
