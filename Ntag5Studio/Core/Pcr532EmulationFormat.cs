using System.Buffers.Binary;

namespace Ntag5Studio.Core;

public sealed record Pcr532EmulationImage(byte[] Bytes, byte[] NdefMessage, string Profile);

public static class Pcr532EmulationFormat
{
    public static Pcr532EmulationImage Convert(byte[] image, CardDumpKind kind)
    {
        var message = ExtractNdef(image, kind);
        // Standard Type 2 CC capacities: 144, 496 and 872 bytes. Keep room for TLV + terminator.
        var profiles = new[] { (Size: 180, Capacity: 144, Name: "NTAG213"),
            (Size: 540, Capacity: 496, Name: "NTAG215"), (Size: 924, Capacity: 872, Name: "NTAG216") };
        var tlvHeader = message.Length < 255 ? 2 : 4;
        var profile = profiles.FirstOrDefault(p => message.Length + tlvHeader + 1 <= p.Capacity);
        if (profile.Size == 0)
            throw new InvalidDataException($"NDEF 消息为 {message.Length} 字节，超出 PCR532 Type 2 模拟文件最大容量；不会截断内容。");
        var output = new byte[profile.Size];
        // Synthetic memory header, not the original chip UID. The vendor emulator uses its own RF UID.
        output[10] = output[11] = 0xFF;
        output[12] = 0xE1;
        output[13] = 0x10;
        output[14] = (byte)(profile.Capacity / 8);
        output[15] = 0x0F; // Read-only NDEF, matching the vendor emulator's READ-only implementation.
        output[16] = 0x03;
        var offset = 18;
        if (message.Length < 255) output[17] = (byte)message.Length;
        else
        {
            output[17] = 0xFF;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(18, 2), (ushort)message.Length);
            offset = 20;
        }
        message.CopyTo(output, offset);
        output[offset + message.Length] = 0xFE;
        return new(output, message, profile.Name);
    }

    public static byte[] ExtractNdef(byte[] image, CardDumpKind kind)
    {
        CardDumpFormat.Validate(image, kind);
        int offset;
        int end = image.Length;
        if (kind == CardDumpKind.Ntag5UserMemory)
        {
            offset = image[0] switch { 0xE1 => 4, 0xE2 => 8,
                _ => throw new InvalidDataException("NTAG5 文件没有 Type 5 能力容器，无法可靠提取 NDEF。") };
        }
        else if (kind == CardDumpKind.Type2Raw && image[12] == 0xE1)
        {
            offset = 16;
            end = Math.Min(image.Length, 16 + image[14] * 8);
        }
        else throw new InvalidDataException("模拟导出需要包含 NDEF 的 NTAG5 或 Type 2 镜像，不能使用 MIFARE Classic 原始文件。");

        while (offset < end)
        {
            var type = image[offset++];
            if (type == 0) continue;
            if (type == 0xFE) break;
            if (offset >= end) throw new InvalidDataException("TLV 长度不完整。");
            int length = image[offset++];
            if (length == 0xFF)
            {
                if (offset + 2 > end) throw new InvalidDataException("TLV 扩展长度不完整。");
                length = BinaryPrimitives.ReadUInt16BigEndian(image.AsSpan(offset, 2));
                offset += 2;
            }
            if (length > end - offset) throw new InvalidDataException("TLV 声明的长度超出镜像范围。");
            if (type == 3)
            {
                var message = image.AsSpan(offset, length).ToArray();
                ValidateNdef(message);
                // The NDEF message is complete; unrelated EEPROM bytes need not be valid TLVs.
                return message;
            }
            offset += length;
        }
        throw new InvalidDataException("镜像中没有 NDEF 消息，不能生成模拟标签文件。");
    }

    private static void ValidateNdef(ReadOnlySpan<byte> message)
    {
        int offset = 0, records = 0;
        while (offset < message.Length)
        {
            var header = message[offset++];
            if (((header & 0x80) != 0) != (records == 0) || (header & 0x20) != 0)
                throw new InvalidDataException("NDEF 起始标志异常或包含暂不支持的分块记录。");
            if (offset >= message.Length) throw new InvalidDataException("NDEF 记录头不完整。");
            int typeLength = message[offset++];
            int lengthBytes = (header & 0x10) != 0 ? 1 : 4;
            if (lengthBytes > message.Length - offset) throw new InvalidDataException("NDEF 载荷长度不完整。");
            uint payloadLength = lengthBytes == 1 ? message[offset] : BinaryPrimitives.ReadUInt32BigEndian(message.Slice(offset, 4));
            offset += lengthBytes;
            int idLength = 0;
            if ((header & 0x08) != 0)
            {
                if (offset >= message.Length) throw new InvalidDataException("NDEF ID 长度不完整。");
                idLength = message[offset++];
            }
            long remaining = (long)typeLength + idLength + payloadLength;
            if (remaining > message.Length - offset) throw new InvalidDataException("NDEF 记录长度超出消息范围。");
            offset += (int)remaining;
            records++;
            if ((header & 0x40) != 0)
            {
                if (offset != message.Length) throw new InvalidDataException("NDEF 结束记录后仍存在额外数据。");
                return;
            }
        }
        throw new InvalidDataException("NDEF 消息为空或缺少结束记录。");
    }
}
