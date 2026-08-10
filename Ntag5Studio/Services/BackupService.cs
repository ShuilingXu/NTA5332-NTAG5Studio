using System.Text.Json;
using Ntag5Studio.Core;

namespace Ntag5Studio.Services;

public static class BackupService
{
    public const string RawDumpDescription =
        "Raw bytes in card memory order; no file header, trailer or container metadata.";

    public static LoadedCardDump LoadDump(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new LoadedCardDump(bytes, CardDumpFormat.Detect(bytes.Length));
    }

    public static byte[] Load(string path)
    {
        var dump = LoadDump(path);
        if (!dump.IsNtag5)
        {
            throw new ArgumentException(
                $"该文件是 {dump.DisplayName}（{dump.Bytes.Length} 字节），不是 2044 字节 NTAG5 用户区镜像。",
                nameof(path));
        }

        return dump.Bytes;
    }

    public static string Save(string path, byte[] data, string source)
    {
        return SaveRawImage(path, data, source, "ntag5-user-memory-backup/v1", "NTAG5 raw .bin backup");
    }

    public static string SaveMfdDump(string path, byte[] data, string source)
    {
        return SaveRawImage(path, data, source, "ntag5-user-memory-mfd-dump/v1", "PCR532/libnfc-style raw .mfd dump");
    }

    public static string SaveCardDump(string path, byte[] data, string source, CardDumpKind kind, bool mfdExtension)
    {
        CardDumpFormat.Validate(data, kind);
        if (kind == CardDumpKind.Ntag5UserMemory)
        {
            return mfdExtension ? SaveMfdDump(path, data, source) : Save(path, data, source);
        }

        var displayName = CardDumpFormat.GetDisplayName(kind);
        return SaveRawImage(
            path,
            data,
            source,
            "mifare-classic-raw-dump/v1",
            $"{displayName} raw {(mfdExtension ? ".mfd" : "dump")}",
            kind);
    }

    private static string SaveRawImage(
        string path,
        byte[] data,
        string source,
        string schema,
        string format,
        CardDumpKind kind = CardDumpKind.Ntag5UserMemory)
    {
        CardDumpFormat.Validate(data, kind);
        File.WriteAllBytes(path, data);

        var isNtag5 = kind == CardDumpKind.Ntag5UserMemory;
        var blockSize = CardDumpFormat.GetLogicalBlockSize(kind);
        var blockCount = CardDumpFormat.GetLogicalBlockCount(kind);

        var metadata = new BackupMetadata(
            Schema: schema,
            Device: CardDumpFormat.GetDisplayName(kind),
            Region: isNtag5
                ? "I2C user EEPROM blocks 0x0000-0x01FE"
                : $"Linear MIFARE Classic memory image, {blockCount} blocks",
            BlockSize: blockSize,
            BlockCount: blockCount,
            ByteCount: data.Length,
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
