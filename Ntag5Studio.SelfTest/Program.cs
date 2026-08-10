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
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-converter-1180x800.png"));

    tabs.SelectedIndex = 2;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-direct-write-1180x800.png"));

    form.Size = new Size(940, 650);
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-user-memory-940x650.png"));

    tabs.SelectedIndex = 1;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-converter-940x650.png"));

    tabs.SelectedIndex = 2;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-direct-write-940x650.png"));
    form.Close();
    Console.WriteLine("RENDER OK  6 previews");
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
