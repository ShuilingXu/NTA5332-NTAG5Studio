using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ntag5Studio.Core;

public enum DataEncodingKind
{
    Hexadecimal,
    Utf8Text,
    AsciiText,
    Utf16LittleEndian,
    Utf16BigEndian,
    Gb18030Text,
    DecimalBytes,
    BinaryBits,
    Base64,
    UrlPercent
}

public static partial class HexCodec
{
    private static readonly Encoding Gb18030 = CreateGb18030();

    public static byte[] Parse(string input, DataEncodingKind kind) => kind switch
    {
        DataEncodingKind.Hexadecimal => ParseHex(input),
        DataEncodingKind.Utf8Text => Encoding.UTF8.GetBytes(input),
        DataEncodingKind.AsciiText => ParseAscii(input),
        DataEncodingKind.Utf16LittleEndian => Encoding.Unicode.GetBytes(input),
        DataEncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode.GetBytes(input),
        DataEncodingKind.Gb18030Text => Gb18030.GetBytes(input),
        DataEncodingKind.DecimalBytes => ParseDecimalBytes(input),
        DataEncodingKind.BinaryBits => ParseBinaryBits(input),
        DataEncodingKind.Base64 => Convert.FromBase64String(input.Trim()),
        DataEncodingKind.UrlPercent => ParseUrlPercent(input),
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

    public static byte[] ParseBinaryBits(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalized = BinaryPrefixRegex().Replace(input, string.Empty);
        normalized = BinarySeparatorRegex().Replace(normalized, string.Empty);
        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length % 8 != 0 || !BinaryOnlyRegex().IsMatch(normalized))
        {
            throw new FormatException("二进制输入必须只包含 0/1，并且每 8 位组成一个字节。");
        }

        var result = new byte[normalized.Length / 8];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(normalized.Substring(i * 8, 8), 2);
        }

        return result;
    }

    public static byte[] ParseAscii(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Any(character => character > 0x7F))
        {
            throw new FormatException("ASCII 文本只能包含 U+0000-U+007F 字符；中文请选 UTF-8 或 GB18030。");
        }

        return Encoding.ASCII.GetBytes(input);
    }

    public static byte[] ParseUrlPercent(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var result = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '%')
            {
                if (i + 2 >= input.Length || !byte.TryParse(
                        input.Substring(i + 1, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    throw new FormatException("URL 百分号编码必须使用 %XX 形式，例如 %E4%B8%AD。");
                }

                result.Add(value);
                i += 2;
            }
            else if (input[i] == '+')
            {
                result.Add(0x20);
            }
            else if (input[i] <= 0x7F)
            {
                result.Add((byte)input[i]);
            }
            else
            {
                throw new FormatException("URL 百分号输入中的非 ASCII 字符必须先进行百分号编码。");
            }
        }

        return result.ToArray();
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

    public static string ToUtf16LittleEndian(byte[] data) => Encoding.Unicode.GetString(data);

    public static string ToUtf16BigEndian(byte[] data) => Encoding.BigEndianUnicode.GetString(data);

    public static string ToGb18030(byte[] data) => Gb18030.GetString(data);

    public static string ToDecimal(ReadOnlySpan<byte> data) => string.Join(" ", data.ToArray());

    public static string ToBinary(ReadOnlySpan<byte> data) => string.Join(
        " ",
        data.ToArray().Select(value => Convert.ToString(value, 2).PadLeft(8, '0')));

    public static string ToUrlPercent(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(data.Length * 3);
        foreach (var value in data)
        {
            if (value is >= (byte)'A' and <= (byte)'Z' ||
                value is >= (byte)'a' and <= (byte)'z' ||
                value is >= (byte)'0' and <= (byte)'9' ||
                value is (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static Encoding CreateGb18030()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(54936);
    }

    [GeneratedRegex(@"0[xX]")]
    private static partial Regex HexPrefixRegex();

    [GeneratedRegex(@"[\s,;:\-_]")]
    private static partial Regex HexSeparatorRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]+$")]
    private static partial Regex HexOnlyRegex();

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex DecimalSeparatorRegex();

    [GeneratedRegex(@"0[bB]")]
    private static partial Regex BinaryPrefixRegex();

    [GeneratedRegex(@"[\s,;:_-]")]
    private static partial Regex BinarySeparatorRegex();

    [GeneratedRegex(@"^[01]+$")]
    private static partial Regex BinaryOnlyRegex();
}
