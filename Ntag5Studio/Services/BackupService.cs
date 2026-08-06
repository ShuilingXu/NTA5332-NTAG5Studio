using System.Text.Json;
using Ntag5Studio.Core;

namespace Ntag5Studio.Services;

public static class BackupService
{
    public static byte[] Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Ntag5Memory.ValidateImage(bytes);
        return bytes;
    }

    public static string Save(string path, byte[] data, string source)
    {
        Ntag5Memory.ValidateImage(data);
        File.WriteAllBytes(path, data);

        var metadata = new BackupMetadata(
            Schema: "ntag5-user-memory-backup/v1",
            Device: "NXP NTA5332 / NTAG 5 boost",
            Region: "I2C user EEPROM blocks 0x0000-0x01FE",
            BlockSize: Ntag5Memory.BytesPerBlock,
            BlockCount: Ntag5Memory.UserBlockCount,
            ByteCount: Ntag5Memory.UserByteCount,
            CreatedLocal: DateTimeOffset.Now,
            Sha256: Ntag5Memory.Sha256(data),
            Source: source);

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path + ".json", json);
        return path;
    }

    public static string SaveAutomaticPreWrite(byte[] data)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Ntag5Studio",
            "Backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"NTA5332-before-write-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
        return Save(path, data, "写入前自动读取的芯片用户区");
    }

    private sealed record BackupMetadata(
        string Schema,
        string Device,
        string Region,
        int BlockSize,
        int BlockCount,
        int ByteCount,
        DateTimeOffset CreatedLocal,
        string Sha256,
        string Source);
}
