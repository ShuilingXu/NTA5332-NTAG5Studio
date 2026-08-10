namespace Ntag5Studio.Core;

public enum CardDumpKind
{
    Ntag5UserMemory,
    MifareClassicMini,
    MifareClassic1K,
    MifareClassic2K,
    MifareClassic4K
}

public sealed record LoadedCardDump(byte[] Bytes, CardDumpKind Kind)
{
    public string DisplayName => CardDumpFormat.GetDisplayName(Kind);
    public bool IsNtag5 => Kind == CardDumpKind.Ntag5UserMemory;
    public bool IsMifareClassic => CardDumpFormat.IsMifareClassic(Kind);
}

public static class CardDumpFormat
{
    public const int MifareBlockSize = 16;

    public static CardDumpKind Detect(int byteCount) => byteCount switch
    {
        Ntag5Memory.UserByteCount => CardDumpKind.Ntag5UserMemory,
        320 => CardDumpKind.MifareClassicMini,
        1024 => CardDumpKind.MifareClassic1K,
        2048 => CardDumpKind.MifareClassic2K,
        4096 => CardDumpKind.MifareClassic4K,
        _ => throw new ArgumentException(
            $"无法识别 {byteCount} 字节的卡片文件。支持 NTAG5 用户区 2044 字节，以及 " +
            "MIFARE Classic S20/S50/2K/S70 原始文件（320/1024/2048/4096 字节）。")
    };

    public static void Validate(byte[]? data, CardDumpKind kind, string parameterName = "data")
    {
        ArgumentNullException.ThrowIfNull(data, parameterName);
        var expectedLength = GetByteCount(kind);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException(
                $"{GetDisplayName(kind)} 文件必须恰好为 {expectedLength} 字节，当前为 {data.Length} 字节。",
                parameterName);
        }
    }

    public static int GetByteCount(CardDumpKind kind) => kind switch
    {
        CardDumpKind.Ntag5UserMemory => Ntag5Memory.UserByteCount,
        CardDumpKind.MifareClassicMini => 320,
        CardDumpKind.MifareClassic1K => 1024,
        CardDumpKind.MifareClassic2K => 2048,
        CardDumpKind.MifareClassic4K => 4096,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static int GetLogicalBlockSize(CardDumpKind kind) =>
        IsMifareClassic(kind) ? MifareBlockSize : Ntag5Memory.BytesPerBlock;

    public static int GetLogicalBlockCount(CardDumpKind kind) =>
        GetByteCount(kind) / GetLogicalBlockSize(kind);

    public static bool IsMifareClassic(CardDumpKind kind) => kind is
        CardDumpKind.MifareClassicMini or
        CardDumpKind.MifareClassic1K or
        CardDumpKind.MifareClassic2K or
        CardDumpKind.MifareClassic4K;

    public static string GetDisplayName(CardDumpKind kind) => kind switch
    {
        CardDumpKind.Ntag5UserMemory => "NTA5332 / NTAG5 用户区",
        CardDumpKind.MifareClassicMini => "MIFARE Classic Mini / S20",
        CardDumpKind.MifareClassic1K => "MIFARE Classic 1K / S50",
        CardDumpKind.MifareClassic2K => "MIFARE Classic 2K",
        CardDumpKind.MifareClassic4K => "MIFARE Classic 4K / S70",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static int GetSectorNumber(CardDumpKind kind, int block)
    {
        ValidateMifareBlock(kind, block);
        return kind == CardDumpKind.MifareClassic4K && block >= 128
            ? 32 + (block - 128) / 16
            : block / 4;
    }

    public static bool IsSectorTrailer(CardDumpKind kind, int block)
    {
        ValidateMifareBlock(kind, block);
        return kind == CardDumpKind.MifareClassic4K && block >= 128
            ? (block - 128) % 16 == 15
            : block % 4 == 3;
    }

    private static void ValidateMifareBlock(CardDumpKind kind, int block)
    {
        if (!IsMifareClassic(kind))
        {
            throw new ArgumentException("当前文件不是 MIFARE Classic 原始文件。", nameof(kind));
        }

        if (block < 0 || block >= GetLogicalBlockCount(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(block));
        }
    }
}
