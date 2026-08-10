using System.Text.Json;
using Ntag5Studio.Core;

namespace Ntag5Studio.Services;

public static class BackupService
{
    public const string RawDumpDescription =
        "Raw bytes in I2C user-block order; no file header, trailer or container metadata.";

    public static byte[] Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Ntag5Memory.ValidateImage(bytes);
        return bytes;
    }

    public static string Save(string path, byte[] data, string source)
    {
        return SaveRawImage(path, data, source, "ntag5-user-memory-backup/v1", "NTAG5 raw .bin backup");
    }

    public static string SaveMfdDump(string path, byte[] data, string source)
    {
        return SaveRawImage(path, data, source, "ntag5-user-memory-mfd-dump/v1", "PCR532/libnfc-style raw .mfd dump");
    }

    private static string SaveRawImage(string path, byte[] data, string source, string schema, string format)
    {
        Ntag5Memory.ValidateImage(data);
        File.WriteAllBytes(path, data);

        var metadata = new BackupMetadata(
            Schema: schema,
            Device: "NXP NTA5332 / NTAG 5 boost",
            Region: "I2C user EEPROM blocks 0x0000-0x01FE",
            BlockSize: Ntag5Memory.BytesPerBlock,
            BlockCount: Ntag5Memory.UserBlockCount,
            ByteCount: Ntag5Memory.UserByteCount,
            Format: format,
            Layout: RawDumpDescription,
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
        string Format,
        string Layout,
        DateTimeOffset CreatedLocal,
        string Sha256,
        string Source);
}
