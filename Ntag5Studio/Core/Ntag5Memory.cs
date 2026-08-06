using System.Security.Cryptography;

namespace Ntag5Studio.Core;

public static class Ntag5Memory
{
    public const int FirstUserBlock = 0x0000;
    public const int LastI2cUserBlock = 0x01FE;
    public const int UserBlockCount = LastI2cUserBlock + 1;
    public const int BytesPerBlock = 4;
    public const int UserByteCount = UserBlockCount * BytesPerBlock;

    public static void ValidateImage(byte[]? image, string parameterName = "image")
    {
        ArgumentNullException.ThrowIfNull(image, parameterName);
        if (image.Length != UserByteCount)
        {
            throw new ArgumentException(
                $"NTAG5 用户区备份必须恰好为 {UserByteCount} 字节，当前为 {image.Length} 字节。",
                parameterName);
        }
    }

    public static IReadOnlyList<int> GetChangedBlocks(ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> target)
    {
        if (baseline.Length != UserByteCount || target.Length != UserByteCount)
        {
            throw new ArgumentException($"比较数据必须都是 {UserByteCount} 字节。 ");
        }

        var changed = new List<int>();
        for (var block = FirstUserBlock; block <= LastI2cUserBlock; block++)
        {
            var offset = block * BytesPerBlock;
            if (!baseline.Slice(offset, BytesPerBlock).SequenceEqual(target.Slice(offset, BytesPerBlock)))
            {
                changed.Add(block);
            }
        }

        return changed;
    }

    public static string ToDisplayAscii(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] is >= 0x20 and <= 0x7E ? (char)bytes[i] : '.';
        }

        return new string(chars);
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
