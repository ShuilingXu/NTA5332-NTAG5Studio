using Ntag5Studio.Core;
using Ntag5Studio.Hardware;
using Ntag5Studio.Services;
using Ntag5Studio.UI;
using System.Reflection;

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
    ("变化块检测", TestChangedBlocks),
    ("Type 5 / NDEF URI 解析", TestNdef),
    ("备份及元数据", TestBackup),
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

static void TestDevicePath()
{
    Assert(SpbNtag5Device.DefaultDevicePath == @"\\.\SPBNFC01", "device path");
}

static void TestVendorIoctls()
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var type = typeof(SpbNtag5Device);
    Assert((uint)type.GetField("IoctlOpen", flags)!.GetRawConstantValue()! == 0x04000400, "open IOCTL");
    Assert((uint)type.GetField("IoctlClose", flags)!.GetRawConstantValue()! == 0x04000404, "close IOCTL");
    Assert((uint)type.GetField("IoctlWriteRead", flags)!.GetRawConstantValue()! == 0x04000410, "write/read IOCTL");
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

    form.Size = new Size(940, 650);
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    SaveControl(form, Path.Combine(outputDirectory, "Ntag5Studio-user-memory-940x650.png"));
    form.Close();
    Console.WriteLine("RENDER OK  3 previews");
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
