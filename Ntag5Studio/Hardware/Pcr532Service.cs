using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ntag5Studio.Hardware;

public sealed record Pcr532CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));

    public bool DeviceNotFound => CombinedOutput.Contains("No NFC device found", StringComparison.OrdinalIgnoreCase) ||
                                  CombinedOutput.Contains("Error opening NFC reader", StringComparison.OrdinalIgnoreCase) ||
                                  CombinedOutput.Contains("Unable to open NFC device", StringComparison.OrdinalIgnoreCase);

    public bool Success => ExitCode == 0 && !DeviceNotFound;
}

public sealed record Pcr532CardInfo(string Uid, string Atqa, string Sak, string CardType, string RawOutput)
{
    public string CompactDisplay => Uid.Length == 0
        ? CardType
        : $"{CardType} · UID {Uid}";
}

public sealed partial class Pcr532Service
{
    private static readonly string[] RequiredTools =
    [
        @"nfc-bin\nfc-list.exe",
        @"nfc-bin\mfoc.exe",
        @"nfc-bin\nfc-mfsetuid.exe",
        @"nfc-bin\nfctag.exe",
        @"nfc-bin\qqgui\nfc-mfclassic.exe"
    ];

    public Pcr532Service(string? runtimeDirectory = null)
    {
        RuntimeDirectory = runtimeDirectory ?? FindRuntimeDirectory() ??
            Path.Combine(AppContext.BaseDirectory, "PCR532");
    }

    public string RuntimeDirectory { get; }

    public IReadOnlyList<string> MissingFiles => RequiredTools
        .Where(relativePath => !File.Exists(Path.Combine(RuntimeDirectory, relativePath)))
        .ToArray();

    public bool IsRuntimeAvailable => MissingFiles.Count == 0;

    public static string? FindRuntimeDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "PCR532"),
            Path.Combine(Environment.CurrentDirectory, "PCR532")
        };

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 8 && directory is not null; level++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "PCR532"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, @"nfc-bin\nfc-list.exe")));
    }

    public static IReadOnlyList<string> GetSerialPorts()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is string port && ComPortRegex().IsMatch(port))
                    {
                        ports.Add(port.ToUpperInvariant());
                    }
                }
            }
        }
        catch
        {
            // The editable COM field remains available if registry enumeration is restricted.
        }

        return ports
            .OrderBy(port => int.Parse(port[3..], CultureInfo.InvariantCulture))
            .ToArray();
    }

    public string Configure(string port, int baudRate)
    {
        EnsureRuntime();
        var normalizedPort = NormalizePort(port);
        if (baudRate is not (115200 or 921600))
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate), "PCR532 速度必须为 115200 或 921600。 ");
        }

        var config = BuildConfiguration(normalizedPort, baudRate);
        var path = Path.Combine(RuntimeDirectory, "libnfc.conf");
        File.WriteAllText(path, config, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string BuildConfiguration(string port, int baudRate)
    {
        var normalizedPort = NormalizePort(port);
        if (baudRate is not (115200 or 921600))
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        }

        return string.Join(
            Environment.NewLine,
            "allow_autoscan = true",
            "allow_intrusive_scan = false",
            "device.name = \"PCR532\"",
            $"device.connstring = \"pn532_uart:{normalizedPort}:{baudRate}\"",
            string.Empty);
    }

    public Task<Pcr532CommandResult> DetectCardAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        RunAsync(@"nfc-bin\nfc-list.exe", ["-v", "-t", "1"], progress, cancellationToken);

    public Task<Pcr532CommandResult> ReadClassicWithRecoveryAsync(
        string outputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(
            @"nfc-bin\mfoc.exe",
            ["-O", Path.GetFullPath(outputPath), "-x", "3", "-m", @"nfc-bin\libnfc.db"],
            progress,
            cancellationToken);

    public Task<Pcr532CommandResult> ReadClassicWithKnownKeysAsync(
        string outputPath,
        string? keyFile,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "r", "a", Path.GetFullPath(outputPath) };
        AddOptionalKeyFile(arguments, keyFile);
        return RunAsync(@"nfc-bin\qqgui\nfc-mfclassic.exe", arguments, progress, cancellationToken);
    }

    public Task<Pcr532CommandResult> WriteClassicAsync(
        string inputPath,
        string? keyFile,
        bool includeManufacturerBlock,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            includeManufacturerBlock ? "W" : "w",
            "a",
            Path.GetFullPath(inputPath)
        };
        AddOptionalKeyFile(arguments, keyFile);
        return RunAsync(@"nfc-bin\qqgui\nfc-mfclassic.exe", arguments, progress, cancellationToken);
    }

    public Task<Pcr532CommandResult> SetUidAsync(
        string uid,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(@"nfc-bin\nfc-mfsetuid.exe", [NormalizeUid(uid)], progress, cancellationToken);

    public Task<Pcr532CommandResult> FormatUidCardAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(@"nfc-bin\nfc-mfsetuid.exe", ["-f"], progress, cancellationToken);

    public Task<Pcr532CommandResult> LockUfuidAsync(
        string backupPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(
            @"nfc-bin\mfoc.exe",
            ["-O", Path.GetFullPath(backupPath), "-x", "3", "-U"],
            progress,
            cancellationToken);

    public Task<Pcr532CommandResult> ReadType2Async(
        string outputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(@"nfc-bin\nfctag.exe", ["r", Path.GetFullPath(outputPath)], progress, cancellationToken);

    public Task<Pcr532CommandResult> WriteType2Async(
        string inputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(@"nfc-bin\nfctag.exe", ["w", Path.GetFullPath(inputPath)], progress, cancellationToken);

    public string? GetDriverInstallerPath()
    {
        var candidates = new[]
        {
            Path.Combine(RuntimeDirectory, @"driver\CH341SER.EXE"),
            Path.Combine(RuntimeDirectory, @"driver\DRIVER\Cdc_com.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static Pcr532CardInfo ParseCardInfo(string output)
    {
        var uid = NormalizeHexCapture(UidRegex().Match(output));
        var atqa = NormalizeHexCapture(AtqaRegex().Match(output));
        var sak = NormalizeHexCapture(SakRegex().Match(output));
        var cardType = sak.ToUpperInvariant() switch
        {
            "08" => "MIFARE Classic 1K / S50",
            "09" => "MIFARE Classic Mini / S20",
            "18" => "MIFARE Classic 4K / S70",
            "00" => "ISO14443A Type 2 / Ultralight / NTAG",
            "20" => "ISO14443-4 / CPU 卡",
            _ when output.Contains("MIFARE Classic", StringComparison.OrdinalIgnoreCase) => "MIFARE Classic",
            _ when output.Contains("Ultralight", StringComparison.OrdinalIgnoreCase) => "Ultralight / NTAG",
            _ when uid.Length > 0 => "ISO14443A 卡片",
            _ => "未检测到卡片"
        };

        return new Pcr532CardInfo(uid, atqa, sak, cardType, output);
    }

    public static string NormalizeUid(string uid)
    {
        var normalized = Regex.Replace(uid, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
        if (normalized.Length != 8)
        {
            throw new FormatException("UID 必须为 4 字节（8 个十六进制字符），例如 11223344。 ");
        }

        return normalized;
    }

    private async Task<Pcr532CommandResult> RunAsync(
        string relativeExecutable,
        IEnumerable<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        EnsureRuntime();
        var executable = Path.Combine(RuntimeDirectory, relativeExecutable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException($"PCR532 组件不存在：{relativeExecutable}", executable);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var outputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var shownArguments = string.Join(" ", startInfo.ArgumentList.Select(QuoteForDisplay));
        progress?.Report($"> {Path.GetFileName(executable)} {shownArguments}".TrimEnd());

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new IOException($"无法启动 PCR532 组件：{Path.GetFileName(executable)}");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process may have exited between the check and Kill.
            }
        });

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var outputTask = PumpAsync(process.StandardOutput, standardOutput, progress, cancellationToken);
        var errorTask = PumpAsync(process.StandardError, standardError, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        progress?.Report($"[退出码 {process.ExitCode}]");
        return new Pcr532CommandResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            destination.AppendLine(line);
            progress?.Report(line);
        }
    }

    private static void AddOptionalKeyFile(List<string> arguments, string? keyFile)
    {
        if (string.IsNullOrWhiteSpace(keyFile))
        {
            return;
        }

        var fullPath = Path.GetFullPath(keyFile);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("指定的 MIFARE 密钥文件不存在。", fullPath);
        }

        arguments.Add(fullPath);
        arguments.Add("f");
    }

    private void EnsureRuntime()
    {
        var missing = MissingFiles;
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                $"PCR532 运行组件不完整：{string.Join("、", missing)}。请使用完整发布包。 ");
        }
    }

    private static string NormalizePort(string port)
    {
        var normalized = port.Trim().ToUpperInvariant();
        if (!ComPortRegex().IsMatch(normalized))
        {
            throw new FormatException("COM 口格式无效，例如 COM3。 ");
        }

        return normalized;
    }

    private static string NormalizeHexCapture(Match match) => match.Success
        ? Regex.Replace(match.Groups[1].Value, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant()
        : string.Empty;

    private static string QuoteForDisplay(string value) => value.Any(char.IsWhiteSpace)
        ? $"\"{value}\""
        : value;

    [GeneratedRegex(@"^COM([1-9][0-9]{0,2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComPortRegex();

    [GeneratedRegex(@"UID\s*\(NFCID1\)\s*:\s*([0-9A-Fa-f ]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UidRegex();

    [GeneratedRegex(@"ATQA[^:]*:\s*([0-9A-Fa-f ]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AtqaRegex();

    [GeneratedRegex(@"SAK[^:]*:\s*([0-9A-Fa-f]{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SakRegex();
}
