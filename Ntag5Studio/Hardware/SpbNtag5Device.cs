using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Ntag5Studio.Core;

namespace Ntag5Studio.Hardware;

public sealed class SpbNtag5Device : IDisposable
{
    public const string DefaultDevicePath = @"\\.\SPBNFC01";

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    // The vendor build uses function base 0x100, unlike upstream SpbTestTool's 0x700.
    private const uint IoctlOpen = 0x04000400;
    private const uint IoctlClose = 0x04000404;
    private const uint IoctlWriteRead = 0x04000410;
    private const int ConfigSessionBlockAddress = 0x10A1;
    private const byte Config1RegisterAddress = 0x01;

    private SafeFileHandle? _handle;

    public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };

    public string DevicePath { get; private set; } = DefaultDevicePath;

    public void Connect(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("驱动路径不能为空。", nameof(devicePath));
        }

        Disconnect();
        var handle = CreateFile(
            devicePath.Trim(),
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw CreateDriverException("无法打开 NTAG5 驱动设备", error);
        }

        _handle = handle;
        DevicePath = devicePath.Trim();
        try
        {
            Control(IoctlOpen, null, null, out _);
        }
        catch
        {
            _handle.Dispose();
            _handle = null;
            throw;
        }
    }

    public void Disconnect()
    {
        if (!IsConnected)
        {
            _handle?.Dispose();
            _handle = null;
            return;
        }

        try
        {
            Control(IoctlClose, null, null, out _);
        }
        catch
        {
            // Closing the OS handle is still required if the controller is already unavailable.
        }
        finally
        {
            _handle?.Dispose();
            _handle = null;
        }
    }

    public byte[] ReadUserMemory(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var result = new byte[Ntag5Memory.UserByteCount];
        const int blocksPerRequest = 16;

        for (var block = Ntag5Memory.FirstUserBlock; block <= Ntag5Memory.LastI2cUserBlock;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blockCount = Math.Min(blocksPerRequest, Ntag5Memory.UserBlockCount - block);
            var chunk = ReadMemory(block, blockCount * Ntag5Memory.BytesPerBlock);
            Buffer.BlockCopy(chunk, 0, result, block * Ntag5Memory.BytesPerBlock, chunk.Length);
            block += blockCount;
            progress?.Report(block * 100 / Ntag5Memory.UserBlockCount);
        }

        return result;
    }

    public byte[] ReadMemory(int blockAddress, int byteCount)
    {
        EnsureConnected();
        if (blockAddress is < Ntag5Memory.FirstUserBlock or > Ntag5Memory.LastI2cUserBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(blockAddress));
        }

        var startOffset = blockAddress * Ntag5Memory.BytesPerBlock;
        if (byteCount < 1 || startOffset + byteCount > Ntag5Memory.UserByteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        var address = new[] { (byte)(blockAddress >> 8), (byte)blockAddress };
        var output = new byte[byteCount];
        Control(IoctlWriteRead, address, output, out var returned);
        if (returned != byteCount)
        {
            throw new IOException($"驱动返回了 {returned} 字节，预期 {byteCount} 字节。 ");
        }

        return output;
    }

    public Ntag5RuntimeStatus ReadRuntimeStatus()
    {
        var config1 = ReadRegister(ConfigSessionBlockAddress, Config1RegisterAddress);
        return new Ntag5RuntimeStatus(config1);
    }

    public byte ReadRegister(int blockAddress, byte registerAddress)
    {
        EnsureConnected();
        if (blockAddress is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(blockAddress));
        }

        var request = new[]
        {
            (byte)(blockAddress >> 8),
            (byte)blockAddress,
            registerAddress
        };
        var output = new byte[1];
        Control(IoctlWriteRead, request, output, out var returned);
        if (returned != output.Length)
        {
            throw new IOException($"驱动返回了 {returned} 字节，读取寄存器时预期 1 字节。");
        }

        return output[0];
    }

    public void WriteBlock(int blockAddress, ReadOnlySpan<byte> data)
    {
        EnsureConnected();
        if (blockAddress is < Ntag5Memory.FirstUserBlock or > Ntag5Memory.LastI2cUserBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(blockAddress));
        }

        if (data.Length != Ntag5Memory.BytesPerBlock)
        {
            throw new ArgumentException("每次必须写入恰好一个 4 字节存储块。", nameof(data));
        }

        var request = new byte[2 + Ntag5Memory.BytesPerBlock];
        request[0] = (byte)(blockAddress >> 8);
        request[1] = (byte)blockAddress;
        data.CopyTo(request.AsSpan(2));

        if (!WriteFile(Handle, request, request.Length, out var written, IntPtr.Zero))
        {
            throw CreateDriverException($"写入块 0x{blockAddress:X3} 失败", Marshal.GetLastWin32Error());
        }

        if (written != request.Length)
        {
            throw new IOException($"驱动只接受了 {written}/{request.Length} 字节。 ");
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    private SafeFileHandle Handle => IsConnected
        ? _handle!
        : throw new InvalidOperationException("尚未连接 NTAG5 驱动。 ");

    private void EnsureConnected() => _ = Handle;

    private void Control(uint code, byte[]? input, byte[]? output, out int returned)
    {
        if (!DeviceIoControl(
                Handle,
                code,
                input,
                input?.Length ?? 0,
                output,
                output?.Length ?? 0,
                out returned,
                IntPtr.Zero))
        {
            throw CreateDriverException($"驱动控制操作 0x{code:X8} 失败", Marshal.GetLastWin32Error());
        }
    }

    private static Exception CreateDriverException(string operation, int error)
    {
        var hint = error switch
        {
            2 or 3 => "请确认驱动已安装、设备已在设备管理器中正常枚举，默认路径为 \\\\.\\SPBNFC01。",
            5 => "访问被拒绝，请检查驱动状态或以管理员身份运行。",
            31 => "设备未能完成请求，请检查 ACPI/I2C 资源和模块连接。",
            1117 => "I2C 设备未响应，可能正在 EEPROM 写周期、被 NFC 仲裁锁定或 I2C 接口已禁用。",
            _ => "请检查驱动、ACPI I2C 资源和模块供电。"
        };
        return new Win32Exception(error, $"{operation}。{hint} (Windows 错误 {error})");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[]? inputBuffer,
        int inputLength,
        byte[]? outputBuffer,
        int outputLength,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        int bytesToWrite,
        out int bytesWritten,
        IntPtr overlapped);
}
