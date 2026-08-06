using System.Buffers.Binary;
using System.Text;

namespace Ntag5Studio.Core;

public static class NdefParser
{
    private static readonly string[] UriPrefixes =
    [
        "", "http://www.", "https://www.", "http://", "https://", "tel:", "mailto:",
        "ftp://anonymous:anonymous@", "ftp://ftp.", "ftps://", "sftp://", "smb://", "nfs://",
        "ftp://", "dav://", "news:", "telnet://", "imap:", "rtsp://", "urn:", "pop:",
        "sip:", "sips:", "tftp:", "btspp://", "btl2cap://", "btgoep://", "tcpobex://",
        "irdaobex://", "file://", "urn:epc:id:", "urn:epc:tag:", "urn:epc:pat:",
        "urn:epc:raw:", "urn:epc:", "urn:nfc:"
    ];

    public static string ParseType5Image(byte[] data)
    {
        Ntag5Memory.ValidateImage(data);
        var output = new StringBuilder();
        var tlvOffset = 0;

        if (data[0] is 0xE1 or 0xE2)
        {
            var extended = data[0] == 0xE2;
            tlvOffset = extended ? 8 : 4;
            output.AppendLine("NFC Forum Type 5 Capability Container");
            output.AppendLine($"  Magic: 0x{data[0]:X2} ({(extended ? "扩展" : "标准")}格式)");
            output.AppendLine($"  版本/访问控制: 0x{data[1]:X2}");
            output.AppendLine($"  容量字段: 0x{data[2]:X2}");
            output.AppendLine($"  功能字段: 0x{data[3]:X2}");
        }
        else
        {
            output.AppendLine("未检测到 Type 5 Capability Container，将从偏移 0 尝试解析 TLV。");
        }

        output.AppendLine();
        ParseTlvs(data, tlvOffset, output);
        return output.ToString().TrimEnd();
    }

    private static void ParseTlvs(ReadOnlySpan<byte> data, int offset, StringBuilder output)
    {
        var tlvNumber = 0;
        while (offset < data.Length)
        {
            var typeOffset = offset;
            var type = data[offset++];
            if (type == 0x00)
            {
                continue;
            }

            if (type == 0xFE)
            {
                output.AppendLine($"终止 TLV @ 0x{typeOffset:X4}");
                return;
            }

            if (offset >= data.Length)
            {
                output.AppendLine($"TLV @ 0x{typeOffset:X4} 缺少长度字段。");
                return;
            }

            var length = data[offset++];
            if (length == 0xFF)
            {
                if (offset + 2 > data.Length)
                {
                    output.AppendLine($"TLV @ 0x{typeOffset:X4} 的扩展长度不完整。");
                    return;
                }

                length = 0;
                var extendedLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
                offset += 2;
                if (offset + extendedLength > data.Length)
                {
                    output.AppendLine($"TLV @ 0x{typeOffset:X4} 声明 {extendedLength} 字节，但数据已结束。");
                    return;
                }

                tlvNumber++;
                AppendTlv(output, tlvNumber, type, typeOffset, data.Slice(offset, extendedLength));
                offset += extendedLength;
                continue;
            }

            if (offset + length > data.Length)
            {
                output.AppendLine($"TLV @ 0x{typeOffset:X4} 声明 {length} 字节，但数据已结束。");
                return;
            }

            tlvNumber++;
            AppendTlv(output, tlvNumber, type, typeOffset, data.Slice(offset, length));
            offset += length;
        }

        output.AppendLine("已到达用户区末尾，未发现终止 TLV。");
    }

    private static void AppendTlv(StringBuilder output, int number, byte type, int offset, ReadOnlySpan<byte> value)
    {
        var name = type switch
        {
            0x01 => "锁定控制",
            0x02 => "内存控制",
            0x03 => "NDEF 消息",
            0xFD => "专有数据",
            _ => $"未知类型 0x{type:X2}"
        };

        output.AppendLine($"TLV {number}: {name} @ 0x{offset:X4}, {value.Length} 字节");
        if (type == 0x03)
        {
            ParseNdef(value, output);
        }
        else
        {
            output.AppendLine($"  数据: {PreviewHex(value)}");
        }
    }

    private static void ParseNdef(ReadOnlySpan<byte> message, StringBuilder output)
    {
        var offset = 0;
        var recordNumber = 0;
        while (offset < message.Length)
        {
            var headerOffset = offset;
            var header = message[offset++];
            var shortRecord = (header & 0x10) != 0;
            var hasId = (header & 0x08) != 0;
            var typeNameFormat = header & 0x07;

            if (offset >= message.Length)
            {
                output.AppendLine("  NDEF 记录头不完整。");
                return;
            }

            var typeLength = message[offset++];
            uint payloadLength;
            if (shortRecord)
            {
                if (offset >= message.Length)
                {
                    output.AppendLine("  NDEF 短记录缺少载荷长度。");
                    return;
                }

                payloadLength = message[offset++];
            }
            else
            {
                if (offset + 4 > message.Length)
                {
                    output.AppendLine("  NDEF 记录缺少四字节载荷长度。");
                    return;
                }

                payloadLength = BinaryPrimitives.ReadUInt32BigEndian(message.Slice(offset, 4));
                offset += 4;
            }

            var idLength = 0;
            if (hasId)
            {
                if (offset >= message.Length)
                {
                    output.AppendLine("  NDEF 记录缺少 ID 长度。");
                    return;
                }

                idLength = message[offset++];
            }

            var required = (long)typeLength + idLength + payloadLength;
            if (required > int.MaxValue || offset + required > message.Length)
            {
                output.AppendLine($"  NDEF 记录 @ 0x{headerOffset:X4} 的长度超出消息边界。");
                return;
            }

            var type = message.Slice(offset, typeLength);
            offset += typeLength;
            var id = message.Slice(offset, idLength);
            offset += idLength;
            var payload = message.Slice(offset, (int)payloadLength);
            offset += (int)payloadLength;

            recordNumber++;
            var typeText = Encoding.ASCII.GetString(type);
            output.AppendLine($"  记录 {recordNumber}: TNF={typeNameFormat}, 类型={typeText}, 载荷={payload.Length} 字节");
            if (!id.IsEmpty)
            {
                output.AppendLine($"    ID: {PreviewHex(id)}");
            }

            output.AppendLine($"    {DescribePayload(typeNameFormat, typeText, payload)}");
            if ((header & 0x40) != 0)
            {
                return;
            }
        }
    }

    private static string DescribePayload(int tnf, string type, ReadOnlySpan<byte> payload)
    {
        if (tnf == 1 && type == "U" && !payload.IsEmpty)
        {
            var prefixIndex = payload[0];
            var prefix = prefixIndex < UriPrefixes.Length ? UriPrefixes[prefixIndex] : string.Empty;
            return $"URI: {prefix}{Encoding.UTF8.GetString(payload[1..])}";
        }

        if (tnf == 1 && type == "T" && !payload.IsEmpty)
        {
            var status = payload[0];
            var languageLength = status & 0x3F;
            if (1 + languageLength > payload.Length)
            {
                return "文本记录格式不完整。";
            }

            var language = Encoding.ASCII.GetString(payload.Slice(1, languageLength));
            var textBytes = payload[(1 + languageLength)..];
            var encoding = (status & 0x80) != 0 ? Encoding.BigEndianUnicode : Encoding.UTF8;
            return $"文本 ({language}): {encoding.GetString(textBytes)}";
        }

        var utf8 = Encoding.UTF8.GetString(payload);
        return $"数据: {PreviewHex(payload)} | UTF-8: {utf8}";
    }

    private static string PreviewHex(ReadOnlySpan<byte> data)
    {
        const int maxBytes = 48;
        var shown = data[..Math.Min(data.Length, maxBytes)];
        var text = HexCodec.ToSpacedHex(shown);
        return data.Length > maxBytes ? text + " ..." : text;
    }
}
