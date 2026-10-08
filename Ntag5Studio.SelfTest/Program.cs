using Ntag5Studio.Core;
using Ntag5Studio.Hardware;
using Ntag5Studio.Services;
using Ntag5Studio.UI;
using System.Reflection;
using System.Text;

if (args.Length == 2 && args[0] == "--render-previews")
{
    RenderPreviews(args[1]);
    return;
}

if (args.Length == 3 && args[0] == "--hardware-check")
{
    Directory.CreateDirectory(args[2]);
    using var device = new SpbNtag5Device();
    device.Connect(SpbNtag5Device.DefaultDevicePath);
    Console.WriteLine("NTAG5 " + device.ReadRuntimeStatus().UserMemoryMappingDisplay);
    var image = device.ReadUserMemory();
    var imagePath = Path.Combine(args[2], "NTAG5-hardware-read.bin");
    BackupService.Save(imagePath, image, "Read-only hardware compatibility verification");
    Console.WriteLine($"NTAG5 READ OK {image.Length} bytes SHA256 {Ntag5Memory.Sha256(image)}");
    var pcr = new Pcr532Service(args[1]);
    pcr.Configure("COM8", 115200);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var result = await pcr.DetectCardAsync(null, timeout.Token);
    File.WriteAllText(Path.Combine(args[2], "PCR532-detect.txt"), result.CombinedOutput);
    Assert(result.Success, "PCR532 real reader detection");
    Console.WriteLine(result.CombinedOutput);
    return;
}

if (args.Length == 3 && args[0] == "--verify-pcr-files")
{
    Directory.CreateDirectory(args[2]);
    var count = 0;
    foreach (var path in Directory.GetFiles(args[1], "*.dump"))
    {
        var original = BackupService.LoadDump(path);
        var output = Path.Combine(args[2], Path.GetFileName(path));
        BackupService.SaveCardDump(output, original.Bytes, "PCR532 compatibility round trip", original.Kind, false);
        Assert(File.ReadAllBytes(path).SequenceEqual(BackupService.LoadDump(output).Bytes), "real PCR532 byte equality");
        Console.WriteLine($"PASS {Path.GetFileName(path)} {original.DisplayName} {original.Bytes.Length} bytes");
        count++;
    }
    Console.WriteLine($"PCR532 ROUNDTRIP OK {count} files");
    return;
}

if (args.Length == 3 && args[0] == "--export-emulation")
{
    var source = BackupService.LoadDump(args[1]);
    var converted = Pcr532EmulationFormat.Convert(source.Bytes, source.Kind);
    BackupService.SaveCardDump(args[2], converted.Bytes, "NDEF emulator-only conversion; not a physical card backup", CardDumpKind.Type2Raw, false);
    Assert(Pcr532EmulationFormat.ExtractNdef(converted.Bytes, CardDumpKind.Type2Raw).SequenceEqual(converted.NdefMessage), "real image NDEF preserved");
    Console.WriteLine($"EMULATION EXPORT OK {converted.Profile} {converted.Bytes.Length} bytes, NDEF {converted.NdefMessage.Length} bytes SHA256 {Ntag5Memory.Sha256(converted.NdefMessage)}");
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("用户区边界", TestMemoryBounds),
    ("十六进制解析", TestHexCodec),
    ("十进制字节解析", TestDecimalCodec),
    ("扩展编码往返", TestAdditionalEncodings),
    ("变化块检测", TestChangedBlocks),
    ("Type 5 / NDEF URI 解析", TestNdef),
    ("备份及元数据", TestBackup),
    ("MFD 原始 dump", TestMfdDump),
    ("S50 文件识别", TestMifareClassicDump),
    ("Type 2 文件与 NDEF", TestType2Dump),
    ("PCR532 模拟格式转换", TestEmulationFormat),
    ("PCR532 配置与识别", TestPcr532Service),
    ("运行状态解析", TestRuntimeStatus),
    ("供应商设备路径", TestDevicePath),
    ("供应商驱动控制码", TestVendorIoctls)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS  {test.Name}");
}

Console.WriteLine($"SELFTEST OK  {tests.Length} tests, {Ntag5Memory.UserBlockCount} blocks, {Ntag5Memory.UserByteCount} bytes");
return;

static void TestMemoryBounds()
{
    Assert(Ntag5Memory.FirstUserBlock == 0x0000, "first block");
    Assert(Ntag5Memory.LastI2cUserBlock == 0x01FE, "last I2C block");
    Assert(Ntag5Memory.UserBlockCount == 511, "block count");
    Assert(Ntag5Memory.UserByteCount == 2044, "byte count");
}

static void TestHexCodec()
{
    var bytes = HexCodec.ParseHex("0xE1 40,80:09");
    Assert(bytes.SequenceEqual(new byte[] { 0xE1, 0x40, 0x80, 0x09 }), "hex parse result");
    Assert(HexCodec.ToSpacedHex(bytes) == "E1 40 80 09", "hex formatting");
}

static void TestDecimalCodec()
{
    var bytes = HexCodec.ParseDecimalBytes("0 64 128 255");
    Assert(bytes.SequenceEqual(new byte[] { 0, 64, 128, 255 }), "decimal parse result");
}

static void TestAdditionalEncodings()
{
    var utf8 = Encoding.UTF8.GetBytes("中文");
    Assert(
        HexCodec.Parse("%E4%B8%AD%E6%96%87", DataEncodingKind.UrlPercent).SequenceEqual(utf8),
        "URL percent decode");
    Assert(HexCodec.ToUrlPercent(utf8) == "%E4%B8%AD%E6%96%87", "URL percent encode");
    Assert(
        HexCodec.Parse("01000001 01000010", DataEncodingKind.BinaryBits).SequenceEqual(new byte[] { 0x41, 0x42 }),
        "binary decode");
    Assert(HexCodec.ToBinary(new byte[] { 0x41, 0x42 }) == "01000001 01000010", "binary encode");

    var utf16Le = HexCodec.Parse("中文", DataEncodingKind.Utf16LittleEndian);
    Assert(HexCodec.ToUtf16LittleEndian(utf16Le) == "中文", "UTF-16 LE round trip");
    var utf16Be = HexCodec.Parse("中文", DataEncodingKind.Utf16BigEndian);
    Assert(HexCodec.ToUtf16BigEndian(utf16Be) == "中文", "UTF-16 BE round trip");
    var gb18030 = HexCodec.Parse("中文", DataEncodingKind.Gb18030Text);
    Assert(HexCodec.ToGb18030(gb18030) == "中文", "GB18030 round trip");
    Assert(HexCodec.Parse("ABC", DataEncodingKind.AsciiText).SequenceEqual(new byte[] { 0x41, 0x42, 0x43 }), "ASCII encode");
}

static void TestChangedBlocks()
{
    var baseline = new byte[Ntag5Memory.UserByteCount];
    var target = (byte[])baseline.Clone();
    target[0] = 1;
    target[40] = 2;
    var changed = Ntag5Memory.GetChangedBlocks(baseline, target);
    Assert(changed.SequenceEqual(new[] { 0, 10 }), "changed blocks");
}

static void TestNdef()
{
    var image = new byte[Ntag5Memory.UserByteCount];
    byte[] deliveryPrefix =
    [
        0xE1, 0x40, 0x80, 0x09,
        0x03, 0x10, 0xD1, 0x01,
        0x0C, 0x55, 0x01, 0x6E,
        0x78, 0x70, 0x2E, 0x63,
        0x6F, 0x6D, 0x2F, 0x6E,
        0x66, 0x63, 0xFE, 0x00
    ];
    deliveryPrefix.CopyTo(image, 0);
    var parsed = NdefParser.ParseType5Image(image);
    Assert(parsed.Contains("http://www.nxp.com/nfc", StringComparison.Ordinal), "URI decode");
    Assert(parsed.Contains("终止 TLV", StringComparison.Ordinal), "terminator");
}

static void TestBackup()
{
    var image = Enumerable.Range(0, Ntag5Memory.UserByteCount).Select(value => (byte)value).ToArray();
    var path = Path.Combine(Environment.CurrentDirectory, "Ntag5Studio.SelfTest.tmp.bin");
    var metadataPath = path + ".json";
    try
    {
        BackupService.Save(path, image, "self-test");
        Assert(BackupService.Load(path).SequenceEqual(image), "backup round trip");
        Assert(File.ReadAllText(metadataPath).Contains(Ntag5Memory.Sha256(image), StringComparison.Ordinal), "metadata hash");
    }
    finally
    {
        File.Delete(path);
        File.Delete(metadataPath);
    }
}

static void TestMfdDump()
{
    var image = Enumerable.Range(0, Ntag5Memory.UserByteCount).Select(value => (byte)(255 - value)).ToArray();
    var path = Path.Combine(Environment.CurrentDirectory, "Ntag5Studio.SelfTest.tmp.mfd");
    var metadataPath = path + ".json";
    try
    {
        BackupService.SaveMfdDump(path, image, "self-test mfd");
        var dump = File.ReadAllBytes(path);
        Assert(dump.Length == Ntag5Memory.UserByteCount, "mfd length");
        Assert(dump.SequenceEqual(image), "mfd raw image");
        Assert(BackupService.Load(path).SequenceEqual(image), "mfd round trip");
        var metadata = File.ReadAllText(metadataPath);
        Assert(metadata.Contains("raw .mfd dump", StringComparison.Ordinal), "mfd metadata format");
        Assert(metadata.Contains(BackupService.RawDumpDescription, StringComparison.Ordinal), "mfd metadata layout");
    }
    finally
    {
        File.Delete(path);
        File.Delete(metadataPath);
    }
}

static void TestMifareClassicDump()
{
    var image = new byte[1024];
    for (var i = 0; i < image.Length; i++)
    {
        image[i] = (byte)(i & 0xFF);
    }

    var path = Path.Combine(Environment.CurrentDirectory, "Ntag5Studio.SelfTest.tmp-s50.dump");
    var metadataPath = path + ".json";
    try
    {
        File.WriteAllBytes(path, image);
        var dump = BackupService.LoadDump(path);
        Assert(dump.Kind == CardDumpKind.MifareClassic1K, "S50 kind");
        Assert(dump.Bytes.SequenceEqual(image), "S50 raw bytes");
        Assert(CardDumpFormat.GetLogicalBlockCount(dump.Kind) == 64, "S50 blocks");
        Assert(CardDumpFormat.GetSectorNumber(dump.Kind, 3) == 0, "S50 first sector");
        Assert(CardDumpFormat.IsSectorTrailer(dump.Kind, 3), "S50 trailer");
        Assert(!CardDumpFormat.IsSectorTrailer(dump.Kind, 4), "S50 data block");

        BackupService.SaveCardDump(path, image, "self-test S50", dump.Kind, mfdExtension: true);
        Assert(File.ReadAllBytes(path).SequenceEqual(image), "S50 save round trip");
        Assert(File.ReadAllText(metadataPath).Contains("MIFARE Classic 1K / S50", StringComparison.Ordinal), "S50 metadata");
    }
    finally
    {
        File.Delete(path);
        File.Delete(metadataPath);
    }
}

static void TestPcr532Service()
{
    var config = Pcr532Service.BuildConfiguration("com7", 115200);
    Assert(config.Contains("pn532_uart:COM7:115200", StringComparison.Ordinal), "PCR532 connstring");
    Assert(config.Contains("allow_intrusive_scan = false", StringComparison.Ordinal), "PCR532 safe scan");
    Assert(Pcr532Service.NormalizeUid("11 22-33:44") == "11223344", "UID normalization");

    var card = Pcr532Service.ParseCardInfo(
        "NFC device: pn532_uart\r\nUID (NFCID1): 11 22 33 44\r\nATQA (SENS_RES): 00 04\r\nSAK (SEL_RES): 08");
    Assert(card.CardType.Contains("S50", StringComparison.Ordinal), "S50 card detection");
    Assert(card.Uid == "11223344", "card UID parse");
    Assert(card.Sak == "08", "card SAK parse");
}

static void TestType2Dump()
{
    foreach (var length in new[] { 64, 80, 144, 164, 180, 192, 212, 216, 232, 256, 540, 572, 924, 936, 1020 })
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var path = Path.Combine(Environment.CurrentDirectory, $"ntag5-test-{Guid.NewGuid()}.dump");
        try
        {
            Assert(CardDumpFormat.Detect(length) == CardDumpKind.Type2Raw, "Type 2 kind");
            BackupService.SaveCardDump(path, bytes, "test", CardDumpKind.Type2Raw, true);
            Assert(BackupService.LoadDump(path).Bytes.SequenceEqual(bytes), "Type 2 byte-preserving round trip");
            Assert(CardDumpFormat.GetLogicalBlockCount(CardDumpKind.Type2Raw, length) == length / 4, "page count");
        }
        finally { File.Delete(path); File.Delete(path + ".json"); }
    }
    var image = new byte[180];
    new byte[] { 0xE1, 0x10, 0x12, 0x00 }.CopyTo(image, 12);
    // URI message for https://example.com.
    new byte[] { 0x03, 0x10, 0xD1, 0x01, 0x0C, 0x55, 0x04,
        0x65, 0x78, 0x61, 0x6D, 0x70, 0x6C, 0x65, 0x2E, 0x63, 0x6F, 0x6D, 0xFE }.CopyTo(image, 16);
    Assert(NdefParser.ParseType2Image(image).Contains("https://example.com"), "Type 2 URI");
    image[12] = 0;
    Assert(NdefParser.ParseType2Image(image).Contains("没有 Type 2"), "missing CC");
    foreach (var length in new[] { 0, 63, 181, 2043, 2045 })
    {
        try { CardDumpFormat.Detect(length); throw new InvalidOperationException("accepted malformed dump"); }
        catch (ArgumentException) { }
    }
}

static void TestEmulationFormat()
{
    foreach (var (payloadSize, expectedSize) in new[] { (12, 180), (200, 540), (500, 924), (860, 924) })
    {
        var image = new byte[Ntag5Memory.UserByteCount];
        new byte[] { 0xE1, 0x40, 0x80, 0x09 }.CopyTo(image, 0);
        var message = new byte[7 + payloadSize];
        message[0] = 0xC2; // one MIME record with four-byte payload length
        message[1] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(2, 4), (uint)payloadSize);
        message[6] = (byte)'x';
        image[4] = 3;
        image[5] = 0xFF;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(6, 2), (ushort)message.Length);
        message.CopyTo(image, 8);
        image[8 + message.Length] = 0xFE;
        var converted = Pcr532EmulationFormat.Convert(image, CardDumpKind.Ntag5UserMemory);
        Assert(converted.Bytes.Length == expectedSize, "emulation profile capacity");
        Assert(converted.Bytes[12] == 0xE1 && converted.Bytes[15] == 0x0F, "read-only Type 2 CC at page 3");
        Assert(Pcr532EmulationFormat.ExtractNdef(converted.Bytes, CardDumpKind.Type2Raw).SequenceEqual(message), "NDEF records unchanged");
        // The source remains untouched.
        Assert(image[0] == 0xE1 && image[4] == 3, "Type 5 original unchanged");
        image[8] = 0x42; // missing MB
        ExpectInvalid(() => Pcr532EmulationFormat.Convert(image, CardDumpKind.Ntag5UserMemory));
    }
    var empty = new byte[Ntag5Memory.UserByteCount];
    ExpectInvalid(() => Pcr532EmulationFormat.Convert(empty, CardDumpKind.Ntag5UserMemory));
    empty[0] = 0xE1;
    empty[4] = 3; empty[5] = 0xFF; empty[6] = 0xFF; empty[7] = 0xFF;
    ExpectInvalid(() => Pcr532EmulationFormat.Convert(empty, CardDumpKind.Ntag5UserMemory));
    ExpectInvalid(() => Pcr532EmulationFormat.Convert(new byte[1024], CardDumpKind.MifareClassic1K));
    // Complete valid message that cannot fit in the largest profile must not be truncated.
    var oversized = new byte[Ntag5Memory.UserByteCount];
    oversized[0] = 0xE1; oversized[4] = 3; oversized[5] = 0xFF;
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(oversized.AsSpan(6, 2), 868);
    oversized[8] = 0xC2; oversized[9] = 1;
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(10, 4), 861);
    oversized[14] = (byte)'x';
    ExpectInvalid(() => Pcr532EmulationFormat.Convert(oversized, CardDumpKind.Ntag5UserMemory));
}

static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Expected invalid emulation input to be rejected");
}

static void TestDevicePath()
{
    Assert(SpbNtag5Device.DefaultDevicePath == @"\\.\SPBNFC01", "device path");
}

static void TestRuntimeStatus()
{
    var eeprom = new Ntag5RuntimeStatus(0x00);
    Assert(!eeprom.SramEnabled, "SRAM disabled");
    Assert(eeprom.ArbiterMode == Ntag5ArbiterMode.Normal, "normal mode");
    Assert(!eeprom.IsSramMirrorActive, "EEPROM mapping");

    var mirror = new Ntag5RuntimeStatus(0x06);
    Assert(mirror.SramEnabled, "SRAM enabled");
    Assert(mirror.ArbiterMode == Ntag5ArbiterMode.SramMirror, "mirror mode");
    Assert(mirror.IsSramMirrorActive, "SRAM mirror active");
    Assert(mirror.UserMemoryMappingDisplay.Contains("0x0000-0x003F：SRAM", StringComparison.Ordinal), "mirror mapping text");

    var passThrough = new Ntag5RuntimeStatus(0x0B);
    Assert(passThrough.ArbiterMode == Ntag5ArbiterMode.SramPassThrough, "pass-through mode");
    Assert(passThrough.TransferDirectionNfcToI2c, "pass-through direction");
}

static void TestVendorIoctls()
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var type = typeof(SpbNtag5Device);
    Assert((uint)type.GetField("IoctlOpen", flags)!.GetRawConstantValue()! == 0x04000400, "open IOCTL");
    Assert((uint)type.GetField("IoctlClose", flags)!.GetRawConstantValue()! == 0x04000404, "close IOCTL");
    Assert((uint)type.GetField("IoctlWriteRead", flags)!.GetRawConstantValue()! == 0x04000410, "write/read IOCTL");
    Assert((int)type.GetField("ConfigSessionBlockAddress", flags)!.GetRawConstantValue()! == 0x10A1, "CONFIG session block");
    Assert((byte)type.GetField("Config1RegisterAddress", flags)!.GetRawConstantValue()! == 0x01, "CONFIG_1 register address");
}

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
}

static void RenderPreviews(string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
    form.Show();
    Application.DoEvents();
    var tabs = FindControl<TabControl>(form) ?? throw new InvalidOperationException("TabControl not found");

    form.Size = new Size(1180, 800);
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-user-memory-1180x800.png"));

    tabs.SelectedIndex = 1;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-pcr532-1180x800.png"));

    tabs.SelectedIndex = 2;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-direct-write-1180x800.png"));

    tabs.SelectedIndex = 3;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-converter-1180x800.png"));

    form.Size = new Size(940, 650);
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-user-memory-940x650.png"));

    tabs.SelectedIndex = 1;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-pcr532-940x650.png"));

    tabs.SelectedIndex = 2;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-direct-write-940x650.png"));

    tabs.SelectedIndex = 3;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-converter-940x650.png"));
    form.Close();
    Console.WriteLine("RENDER OK  8 previews");
}

static T? FindControl<T>(Control parent) where T : Control
{
    foreach (Control child in parent.Controls)
    {
        if (child is T match)
        {
            return match;
        }

        var nested = FindControl<T>(child);
        if (nested is not null)
        {
            return nested;
        }
    }

    return null;
}

static void SaveControl(Control control, string path)
{
    using var bitmap = new Bitmap(control.Width, control.Height);
    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
}
