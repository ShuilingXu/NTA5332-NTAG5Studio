namespace Ntag5Studio.Core;

public enum CardDumpKind
{
    Ntag5UserMemory,
    MifareClassicMini,
    MifareClassic1K,
    MifareClassic2K,
    MifareClassic4K,
    Type2Raw
}

public sealed record LoadedCardDump(byte[] Bytes, CardDumpKind Kind)
{
    public string DisplayName => CardDumpFormat.GetDisplayName(Kind);
    public bool IsNtag5 => Kind == CardDumpKind.Ntag5UserMemory;
    public bool IsMifareClassic => CardDumpFormat.IsMifareClassic(Kind);
    public bool IsType2 => Kind == CardDumpKind.Type2Raw;
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
        64 or 80 or 144 or 164 or 180 or 192 or 212 or 216 or 232 or 256 or 540 or 572 or 924 or 936 or 1020 => CardDumpKind.Type2Raw,
        _ => throw new ArgumentException(
            $"无法识别 {byteCount} 字节的卡片文件。支持 NTAG5 用户区 2044 字节，以及 " +
            "MIFARE Classic S20/S50/2K/S70 原始文件（320/1024/2048/4096 字节）及常见 Type 2 原始页面镜像。")
    };

    public static void Validate(byte[]? data, CardDumpKind kind, string parameterName = "data")
    {
        ArgumentNullException.ThrowIfNull(data, parameterName);
        if (kind == CardDumpKind.Type2Raw)
        {
            if (Detect(data.Length) != CardDumpKind.Type2Raw)
                throw new ArgumentException("文件不是支持的 Type 2 原始页面镜像。", parameterName);
            return;
        }
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

    public static int GetLogicalBlockCount(CardDumpKind kind, int? byteCount = null) =>
        (kind == CardDumpKind.Type2Raw
            ? byteCount ?? throw new ArgumentException("Type 2 页面数需要实际文件长度。", nameof(byteCount))
            : GetByteCount(kind)) / GetLogicalBlockSize(kind);

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
        CardDumpKind.Type2Raw => "Ultralight / NTAG Type 2 原始页面镜像",
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
