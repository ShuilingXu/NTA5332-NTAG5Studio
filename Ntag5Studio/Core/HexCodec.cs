using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ntag5Studio.Core;

public enum DataEncodingKind
{
    Hexadecimal,
    Utf8Text,
    DecimalBytes,
    Base64
}

public static partial class HexCodec
{
    public static byte[] Parse(string input, DataEncodingKind kind) => kind switch
    {
        DataEncodingKind.Hexadecimal => ParseHex(input),
        DataEncodingKind.Utf8Text => Encoding.UTF8.GetBytes(input),
        DataEncodingKind.DecimalBytes => ParseDecimalBytes(input),
        DataEncodingKind.Base64 => Convert.FromBase64String(input.Trim()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static byte[] ParseHex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalized = HexPrefixRegex().Replace(input, string.Empty);
        normalized = HexSeparatorRegex().Replace(normalized, string.Empty);

        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length % 2 != 0)
        {
            throw new FormatException("十六进制字符数必须为偶数，例如 01 A0 FF。");
        }

        if (!HexOnlyRegex().IsMatch(normalized))
        {
            throw new FormatException("十六进制输入只能包含 0-9、A-F 和常用分隔符。");
        }

        return Convert.FromHexString(normalized);
    }

    public static byte ParseByte(string? input)
    {
        var bytes = ParseHex(input ?? string.Empty);
        if (bytes.Length != 1)
        {
            throw new FormatException("请输入两位十六进制数，例如 0A。");
        }

        return bytes[0];
    }

    public static byte[] ParseDecimalBytes(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tokens = DecimalSeparatorRegex()
            .Split(input.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return [];
        }

        var result = new byte[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!byte.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
            {
                throw new FormatException($"“{tokens[i]}”不是 0-255 的十进制字节。");
            }
        }

        return result;
    }

    public static string ToSpacedHex(ReadOnlySpan<byte> data) => Convert.ToHexString(data).Chunk(2)
        .Select(pair => new string(pair))
        .Aggregate(new StringBuilder(), (builder, value) =>
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            return builder.Append(value);
        }).ToString();

    public static string ToUtf8(byte[] data) => Encoding.UTF8.GetString(data);

    public static string ToAscii(ReadOnlySpan<byte> data) => Ntag5Memory.ToDisplayAscii(data);

    public static string ToDecimal(ReadOnlySpan<byte> data) => string.Join(" ", data.ToArray());

    [GeneratedRegex(@"0[xX]")]
    private static partial Regex HexPrefixRegex();

    [GeneratedRegex(@"[\s,;:\-_]")]
    private static partial Regex HexSeparatorRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]+$")]
    private static partial Regex HexOnlyRegex();

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex DecimalSeparatorRegex();
}
