using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ntag5Studio.Core;
using Ntag5Studio.Hardware;
using Ntag5Studio.Services;

namespace Ntag5Studio.UI;

public sealed partial class MainForm : Form
{
    private readonly SpbNtag5Device _device = new();
    private readonly Pcr532Service _pcr532 = new();
    private byte[]? _workingImage;
    private byte[]? _baselineImage;
    private byte[]? _convertedBytes;
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _loadingGrid;
    private string _imageSource = "尚未载入";
    private CardDumpKind _workingDumpKind = CardDumpKind.Ntag5UserMemory;
    private Ntag5RuntimeStatus? _runtimeStatus;

    private bool HasNtag5WorkingImage =>
        _workingImage is not null && _workingDumpKind == CardDumpKind.Ntag5UserMemory;

    private sealed record PreparedChipWrite(
        byte[] Target,
        string ConfirmationIntro,
        string NoChangeLog,
        string NoChangeStatus,
        string SuccessSource);

    private sealed record DirectWritePayload(
        byte[] Bytes,
        DataEncodingKind Kind,
        string KindDisplay,
        int Offset,
        int FirstBlock,
        int LastBlock);

    public MainForm()
    {
        InitializeLayout();
        WireEvents();
        RefreshPcrPorts();
        UpdateActionAvailability();
        AppendLog("程序已启动。连接后会读取 CONFIG_1_REG，判断 EEPROM / SRAM 运行状态。");
        AppendLog($"默认驱动路径：{SpbNtag5Device.DefaultDevicePath}");
    }

    private void WireEvents()
    {
        _connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        _readButton.Click += async (_, _) => await ReadChipAsync();
        _runtimeStatusButton.Click += async (_, _) => await RefreshRuntimeStatusAsync(showDetails: true);
        _openButton.Click += (_, _) => OpenBackup();
        _saveButton.Click += (_, _) => SaveBackup();
        _saveMfdButton.Click += (_, _) => SaveMfdDump();
        _writeButton.Click += async (_, _) => await WriteChangesAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _cancelButton.Click += (_, _) => _operationCts?.Cancel();
        _jumpButton.Click += (_, _) => JumpToBlock();
        _jumpBlockBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            JumpToBlock();
            eventArgs.SuppressKeyPress = true;
        };

        _memoryGrid.CellValidating += ValidateHexCell;
        _memoryGrid.CellEndEdit += CommitHexCell;
        _memoryGrid.CellFormatting += FormatChangedCell;
        _memoryGrid.SelectionChanged += (_, _) => UpdateSelectedBlockDetails();
        _memoryGrid.DataError += (_, eventArgs) => eventArgs.Cancel = true;

        _convertButton.Click += (_, _) => ConvertInput();
        _loadImageForConversionButton.Click += (_, _) => LoadCurrentImageIntoConverter();
        _copyOutputButton.Click += (_, _) => CopyCurrentOutput();
        _outputTabs.SelectedIndexChanged += (_, _) => UpdateOutputInfo();
        _parseNdefButton.Click += (_, _) => ParseCurrentNdef();
        _insertBytesButton.Click += (_, _) => InsertConvertedBytes();
        _directPreviewButton.Click += (_, _) => PreviewDirectWritePayload();
        _directWriteButton.Click += async (_, _) => await WriteDirectContentAsync();
        _pcrRefreshPortsButton.Click += (_, _) => RefreshPcrPorts();
        _pcrDetectButton.Click += async (_, _) => await DetectPcr532Async();
        _pcrInstallDriverButton.Click += (_, _) => InstallPcr532Driver();
        _pcrBrowseKeyButton.Click += (_, _) => SelectPcrKeyFile();
        _pcrRecoveryReadButton.Click += async (_, _) => await ReadClassicWithRecoveryAsync();
        _pcrKnownReadButton.Click += async (_, _) => await ReadClassicWithKnownKeysAsync();
        _pcrWriteButton.Click += async (_, _) => await WriteClassicAsync(includeManufacturerBlock: false);
        _pcrMagicWriteButton.Click += async (_, _) => await WriteClassicAsync(includeManufacturerBlock: true);
        _pcrSetUidButton.Click += async (_, _) => await SetPcrUidAsync();
        _pcrFormatUidButton.Click += async (_, _) => await FormatPcrUidCardAsync();
        _pcrLockUfuidButton.Click += async (_, _) => await LockPcrUfuidAsync();
        _pcrType2ReadButton.Click += async (_, _) => await ReadPcrType2Async();
        _pcrType2WriteButton.Click += async (_, _) => await WritePcrType2Async();

        FormClosing += OnFormClosing;
        KeyPreview = true;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Control && eventArgs.KeyCode == Keys.S && _saveButton.Enabled)
            {
                SaveBackup();
                eventArgs.SuppressKeyPress = true;
            }
        };
    }

    private async Task ToggleConnectionAsync()
    {
        if (_device.IsConnected)
        {
            await RunOperationAsync("正在断开驱动...", _ => Task.Run(_device.Disconnect));
            _baselineImage = null;
            _runtimeStatus = null;
            UpdateRuntimeStatusDisplay();
            AppendLog("已断开驱动。比较基准已清除，编辑区数据仍保留。");
            RefreshDifferenceDisplay();
            return;
        }

        var path = _devicePathBox.Text.Trim();
        await RunOperationAsync("正在连接 NTAG5 驱动...", _ => Task.Run(() => _device.Connect(path)));
        if (_device.IsConnected)
        {
            _baselineImage = null;
            AppendLog($"驱动连接成功：{path}");
            AppendLog("I²C 目标地址由 ACPI SPB 资源配置；NTA5332 出厂默认地址为 0x54。");
            SetStatus("已连接，可以读取芯片或打开备份");
            await RefreshRuntimeStatusAsync(showDetails: false);
        }
    }

    private void RefreshPcrPorts()
    {
        var previous = _pcrPortBox.Text.Trim();
        var ports = Pcr532Service.GetSerialPorts();
        _pcrPortBox.Items.Clear();
        foreach (var port in ports)
        {
            _pcrPortBox.Items.Add(port);
        }

        if (previous.Length > 0)
        {
            _pcrPortBox.Text = previous;
        }
        else if (ports.Count > 0)
        {
            _pcrPortBox.SelectedIndex = 0;
        }
        else
        {
            _pcrPortBox.Text = "COM3";
        }

        if (_pcr532.IsRuntimeAvailable)
        {
            _pcrRuntimeLabel.Text = $"组件：就绪 · {Path.GetFileName(_pcr532.RuntimeDirectory)}";
            _pcrRuntimeLabel.ForeColor = Color.FromArgb(0, 105, 92);
        }
        else
        {
            _pcrRuntimeLabel.Text = "组件：缺少 PCR532 运行文件";
            _pcrRuntimeLabel.ForeColor = Color.FromArgb(183, 28, 28);
        }

        AppendPcrLog($"串口列表刷新：{(ports.Count == 0 ? "未发现串口，仍可手动输入 COM 口" : string.Join(", ", ports))}");
        UpdateActionAvailability();
    }

    private async Task DetectPcr532Async()
    {
        await RunOperationAsync("正在检测 PCR532 读卡器和卡片...", async cancellationToken =>
        {
            ConfigurePcr532();
            var result = await _pcr532.DetectCardAsync(CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "检测 PCR532");
            var card = Pcr532Service.ParseCardInfo(result.CombinedOutput);
            _pcrCardLabel.Text = card.Uid.Length == 0
                ? $"卡片：未检测到卡片（读卡器已响应）"
                : $"卡片：{card.CompactDisplay}" +
                  (card.Sak.Length == 0 ? string.Empty : $" · SAK {card.Sak}");
            AppendPcrLog($"识别结果：{card.CardType}；UID={(card.Uid.Length == 0 ? "-" : card.Uid)}；ATQA={(card.Atqa.Length == 0 ? "-" : card.Atqa)}；SAK={(card.Sak.Length == 0 ? "-" : card.Sak)}");
            SetStatus(card.Uid.Length == 0 ? "PCR532 已连接，等待卡片" : $"PCR532 已检测到 {card.CardType}");
        });
    }

    private void ConfigurePcr532()
    {
        var baud = ParsePcrBaudRate();
        var configPath = _pcr532.Configure(_pcrPortBox.Text, baud);
        AppendPcrLog($"libnfc 配置：{configPath} · pn532_uart:{_pcrPortBox.Text.Trim().ToUpperInvariant()}:{baud}");
    }

    private int ParsePcrBaudRate()
    {
        if (!int.TryParse(_pcrBaudBox.SelectedItem?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var baud))
        {
            throw new FormatException("PCR532 速度设置无效。 ");
        }

        return baud;
    }

    private IProgress<string> CreatePcrProgress() =>
        new Progress<string>(AppendPcrLog);

    private static void EnsurePcrSuccess(Pcr532CommandResult result, string operation)
    {
        if (result.DeviceNotFound)
        {
            throw new IOException($"{operation}失败：未找到 PCR532 读卡器或卡片。请检查 CH341 驱动、串口号、波特率和天线连接。 ");
        }

        if (!result.Success)
        {
            var detail = result.CombinedOutput.Trim();
            if (detail.Length > 800)
            {
                detail = detail[^800..];
            }

            throw new IOException($"{operation}失败（退出码 {result.ExitCode}）。{Environment.NewLine}{detail}");
        }
    }

    private void InstallPcr532Driver()
    {
        TryUserAction("安装 PCR532 驱动", () =>
        {
            var installer = _pcr532.GetDriverInstallerPath();
            if (installer is null)
            {
                throw new FileNotFoundException("发布包中没有 CH341/Cdc 驱动安装程序。 ");
            }

            var confirmation = MessageBox.Show(
                this,
                "即将启动 PCR532 的 CH341/Cdc 驱动安装程序。请在 Windows 安装器中按提示完成，是否继续？",
                "安装 PCR532 驱动",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                WorkingDirectory = Path.GetDirectoryName(installer),
                UseShellExecute = true
            });
            AppendPcrLog($"已启动驱动安装程序：{installer}");
            SetStatus("驱动安装程序已启动");
        });
    }

    private void SelectPcrKeyFile()
    {
        TryUserAction("选择密钥文件", () =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "MIFARE 密钥/备份文件 (*.mfd;*.dump;*.bin)|*.mfd;*.dump;*.bin|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Title = "选择 PCR532 使用的 MIFARE 密钥文件"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _pcrKeyFileBox.Text = dialog.FileName;
            AppendPcrLog($"密钥文件：{dialog.FileName}");
            SetStatus("已选择 PCR532 密钥文件");
        });
    }

    private async Task ReadClassicWithRecoveryAsync()
    {
        var path = ShowPcrSaveDialog(
            "保存 MIFARE Classic 自动恢复备份",
            $"MIFARE-recovered-{DateTime.Now:yyyyMMdd-HHmmss}.dump");
        if (path is null)
        {
            return;
        }

        await RunOperationAsync("正在使用 mfoc 恢复密钥并备份 MIFARE Classic...", async cancellationToken =>
        {
            ConfigurePcr532();
            var result = await _pcr532.ReadClassicWithRecoveryAsync(path, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "MIFARE Classic 自动恢复");
            LoadPcrClassicDump(path, "PCR532 mfoc 自动恢复");
            AppendLog($"PCR532 自动恢复备份完成：{path}");
            SetStatus("MIFARE Classic 密钥恢复并备份完成");
        });
    }

    private async Task ReadClassicWithKnownKeysAsync()
    {
        var path = ShowPcrSaveDialog(
            "保存 MIFARE Classic 备份",
            $"MIFARE-{DateTime.Now:yyyyMMdd-HHmmss}.dump");
        if (path is null)
        {
            return;
        }

        var keyFile = GetPcrKeyFile();
        await RunOperationAsync("正在使用现有密钥读取 MIFARE Classic...", async cancellationToken =>
        {
            ConfigurePcr532();
            var result = await _pcr532.ReadClassicWithKnownKeysAsync(path, keyFile, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "MIFARE Classic 读取");
            LoadPcrClassicDump(path, "PCR532 已知密钥读取");
            AppendLog($"PCR532 MIFARE 备份完成：{path}");
            SetStatus("MIFARE Classic 读取完成");
        });
    }

    private async Task WriteClassicAsync(bool includeManufacturerBlock)
    {
        var path = ShowPcrOpenDialog("选择要写入的 MIFARE Classic 文件");
        if (path is null)
        {
            return;
        }

        var dump = BackupService.LoadDump(path);
        if (!dump.IsMifareClassic)
        {
            throw new InvalidOperationException($"该文件是 {dump.DisplayName}，不是 MIFARE Classic 原始文件。 ");
        }

        var keyFile = GetPcrKeyFile();
        var warning = includeManufacturerBlock
            ? "这会使用 libnfc 的解锁写入模式，尝试覆盖 0 块 UID/厂商数据；仅适用于支持后门的 UID/CUID/UFUID 魔术卡。普通原厂卡可能损坏或无法再次选择。"
            : "普通写卡模式不会覆盖 0 块 UID/厂商数据，适用于已有 UID 的普通 MIFARE Classic 卡。";
        var confirmation = MessageBox.Show(
            this,
            $"文件：{path}{Environment.NewLine}容量：{dump.Bytes.Length} 字节{Environment.NewLine}{Environment.NewLine}{warning}{Environment.NewLine}{Environment.NewLine}" +
            (_pcrAutoBackupCheck.Checked ? "写入前会先尝试读取并保存当前卡片备份。" : "已关闭写入前自动备份。") + "\r\n\r\n继续吗？",
            includeManufacturerBlock ? "确认写入 UID/CUID 卡" : "确认写入 MIFARE Classic",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        await RunOperationAsync("正在准备写入 MIFARE Classic...", async cancellationToken =>
        {
            ConfigurePcr532();
            if (_pcrAutoBackupCheck.Checked)
            {
                await CreatePcrClassicBackupAsync(keyFile, "before-write", cancellationToken);
            }

            var result = await _pcr532.WriteClassicAsync(
                path,
                keyFile,
                includeManufacturerBlock,
                CreatePcrProgress(),
                cancellationToken);
            EnsurePcrSuccess(result, includeManufacturerBlock ? "UID/CUID 写入" : "MIFARE Classic 写入");
            AppendLog($"PCR532 MIFARE 写卡完成：{path}");
            SetStatus(includeManufacturerBlock ? "UID/CUID 卡写入完成" : "MIFARE Classic 写入完成");
        });
    }

    private async Task SetPcrUidAsync()
    {
        var uid = Pcr532Service.NormalizeUid(_pcrUidBox.Text);
        var uidBytes = Convert.FromHexString(uid);
        var bcc = (byte)(uidBytes[0] ^ uidBytes[1] ^ uidBytes[2] ^ uidBytes[3]);
        var confirmation = MessageBox.Show(
            this,
            $"将把 UID/CUID 魔术卡的 UID 设置为 {uid}，BCC 将自动计算为 {bcc:X2}。\r\n\r\n" +
            "此操作只适用于支持后门的 MIFARE Classic UID 卡，不适用于普通原厂卡。继续吗？",
            "确认设置 UID",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var keyFile = GetPcrKeyFile();
        await RunOperationAsync("正在设置 MIFARE UID...", async cancellationToken =>
        {
            ConfigurePcr532();
            if (_pcrAutoBackupCheck.Checked)
            {
                await CreatePcrClassicBackupAsync(keyFile, "before-uid", cancellationToken);
            }

            var result = await _pcr532.SetUidAsync(uid, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "设置 UID");
            AppendLog($"PCR532 UID 已设置：{uid}（BCC {bcc:X2}）");
            SetStatus($"UID 设置完成：{uid}");
        });
    }

    private async Task FormatPcrUidCardAsync()
    {
        var confirmation = MessageBox.Show(
            this,
            "格式化 UID 卡会清空可写数据并恢复默认访问控制，操作前会先备份当前卡片（如果勾选自动备份）。\r\n\r\n继续吗？",
            "确认格式化 UID 卡",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var keyFile = GetPcrKeyFile();
        await RunOperationAsync("正在格式化 MIFARE UID 卡...", async cancellationToken =>
        {
            ConfigurePcr532();
            if (_pcrAutoBackupCheck.Checked)
            {
                await CreatePcrClassicBackupAsync(keyFile, "before-format", cancellationToken);
            }

            var result = await _pcr532.FormatUidCardAsync(CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "格式化 UID 卡");
            AppendLog("PCR532 UID 卡格式化完成。");
            SetStatus("UID 卡格式化完成");
        });
    }

    private async Task LockPcrUfuidAsync()
    {
        if (MessageBox.Show(
                this,
                "锁定 UFUID 是不可逆操作，锁定后不能再次修改 0 块 UID。官方工具会先用 mfoc 读取并保存备份，然后执行锁定。\r\n\r\n确定要继续吗？",
                "危险操作：锁定 UFUID",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        if (!ConfirmExactText("二次确认锁定 UFUID", "请输入 LOCK UFUID 以确认不可逆操作：", "LOCK UFUID"))
        {
            return;
        }

        var backupPath = GetPcrBackupPath("UFUID-before-lock");
        await RunOperationAsync("正在备份并锁定 UFUID...", async cancellationToken =>
        {
            ConfigurePcr532();
            var result = await _pcr532.LockUfuidAsync(backupPath, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "锁定 UFUID");
            if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
            {
                throw new IOException("UFUID 命令返回成功，但没有生成备份文件，已停止报告为成功。 ");
            }

            AppendPcrLog($"UFUID 锁定前备份：{backupPath}");
            AppendLog($"PCR532 UFUID 已锁定；锁定前备份：{backupPath}");
            SetStatus("UFUID 已锁定，之后不能修改 UID");
        });
    }

    private async Task ReadPcrType2Async()
    {
        var path = ShowPcrSaveDialog(
            "保存 Ultralight / NTAG 原始备份",
            $"Type2-{DateTime.Now:yyyyMMdd-HHmmss}.mfd");
        if (path is null)
        {
            return;
        }

        await RunOperationAsync("正在读取 Ultralight / NTAG Type 2...", async cancellationToken =>
        {
            ConfigurePcr532();
            var result = await _pcr532.ReadType2Async(path, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "Type 2 读取");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new IOException("Type 2 命令返回成功，但没有生成备份文件。 ");
            }

            AppendLog($"PCR532 Type 2 备份完成：{path}（{new FileInfo(path).Length} 字节）");
            SetStatus("Ultralight / NTAG 备份完成");
        });
    }

    private async Task WritePcrType2Async()
    {
        var path = ShowPcrOpenDialog("选择 Ultralight / NTAG 原始备份");
        if (path is null)
        {
            return;
        }

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new InvalidOperationException("Type 2 备份文件为空。 ");
        }

        if (MessageBox.Show(
                this,
                $"将恢复文件 {path}（{fileInfo.Length} 字节）到当前 Type 2 卡片。\r\n" +
                "备份中的锁定位、OTP 或配置页可能具有不可逆影响，请确认文件来源可信。\r\n\r\n继续吗？",
                "确认恢复 Type 2 备份",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunOperationAsync("正在恢复 Ultralight / NTAG Type 2...", async cancellationToken =>
        {
            ConfigurePcr532();
            if (_pcrAutoBackupCheck.Checked)
            {
                var backupPath = GetPcrBackupPath("Type2-before-write", ".mfd");
                var backup = await _pcr532.ReadType2Async(backupPath, CreatePcrProgress(), cancellationToken);
                EnsurePcrSuccess(backup, "Type 2 写入前备份");
                AppendPcrLog($"Type 2 写入前备份：{backupPath}");
            }

            var result = await _pcr532.WriteType2Async(path, CreatePcrProgress(), cancellationToken);
            EnsurePcrSuccess(result, "Type 2 写入");
            AppendLog($"PCR532 Type 2 恢复完成：{path}");
            SetStatus("Ultralight / NTAG 恢复完成");
        });
    }

    private async Task<string> CreatePcrClassicBackupAsync(
        string? keyFile,
        string label,
        CancellationToken cancellationToken)
    {
        var backupPath = GetPcrBackupPath($"MIFARE-{label}", ".dump");
        var result = await _pcr532.ReadClassicWithKnownKeysAsync(
            backupPath,
            keyFile,
            CreatePcrProgress(),
            cancellationToken);
        EnsurePcrSuccess(result, "MIFARE 写入前备份");
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
        {
            throw new IOException("MIFARE 写入前命令返回成功，但没有生成备份文件。 ");
        }

        try
        {
            var dump = BackupService.LoadDump(backupPath);
            if (!dump.IsMifareClassic)
            {
                throw new InvalidDataException("PCR532 生成的备份不是 MIFARE Classic 文件。 ");
            }

            BackupService.SaveCardDump(backupPath, dump.Bytes, "PCR532 写入前自动备份", dump.Kind, mfdExtension: false);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("PCR532 生成的备份文件大小异常。 ", exception);
        }

        AppendPcrLog($"MIFARE 写入前备份：{backupPath}");
        return backupPath;
    }

    private void LoadPcrClassicDump(string path, string source)
    {
        var dump = BackupService.LoadDump(path);
        if (!dump.IsMifareClassic)
        {
            throw new InvalidDataException($"PCR532 输出文件大小为 {dump.Bytes.Length} 字节，不是支持的 MIFARE Classic 原始文件。 ");
        }

        _baselineImage = (byte[])dump.Bytes.Clone();
        SetWorkingImage(dump.Bytes, $"{source} · {Path.GetFileName(path)}", dump.Kind);
        _mainTabs.SelectedIndex = 0;
        AppendPcrLog($"已载入工作区：{dump.DisplayName} / {dump.Bytes.Length} 字节");
    }

    private string? GetPcrKeyFile()
    {
        var path = _pcrKeyFileBox.Text.Trim();
        return path.Length == 0 ? null : path;
    }

    private string GetPcrBackupPath(string prefix, string extension = ".dump")
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Ntag5Studio",
            "PCR532Backups");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmssfff}{extension}");
    }

    private string? ShowPcrSaveDialog(string title, string fileName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = "原始卡片文件 (*.dump;*.mfd;*.bin)|*.dump;*.mfd;*.bin|所有文件 (*.*)|*.*",
            AddExtension = true,
            DefaultExt = "dump",
            OverwritePrompt = true,
            FileName = fileName
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private string? ShowPcrOpenDialog(string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "原始卡片文件 (*.dump;*.mfd;*.bin)|*.dump;*.mfd;*.bin|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private bool ConfirmExactText(string title, string message, string expected)
    {
        using var dialog = new Form();
        using var prompt = new Label();
        using var input = new TextBox();
        using var ok = new Button();
        using var cancel = new Button();
        dialog.Text = title;
        dialog.StartPosition = FormStartPosition.CenterParent;
        dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
        dialog.MinimizeBox = false;
        dialog.MaximizeBox = false;
        dialog.ClientSize = new Size(440, 150);
        prompt.Text = message;
        prompt.AutoSize = false;
        prompt.SetBounds(16, 14, 408, 38);
        input.SetBounds(16, 58, 408, 26);
        input.Font = new Font("Consolas", 10F);
        ok.Text = "确认";
        ok.DialogResult = DialogResult.OK;
        ok.SetBounds(258, 105, 78, 30);
        ok.Enabled = false;
        cancel.Text = "取消";
        cancel.DialogResult = DialogResult.Cancel;
        cancel.SetBounds(346, 105, 78, 30);
        input.TextChanged += (_, _) => ok.Enabled = string.Equals(input.Text.Trim(), expected, StringComparison.Ordinal);
        dialog.Controls.AddRange([prompt, input, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK && string.Equals(input.Text.Trim(), expected, StringComparison.Ordinal);
    }

    private void AppendPcrLog(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendPcrLog), message);
            return;
        }

        _pcrLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _pcrLogBox.SelectionStart = _pcrLogBox.TextLength;
        _pcrLogBox.ScrollToCaret();
    }

    private async Task RefreshRuntimeStatusAsync(bool showDetails)
    {
        await RunOperationAsync("正在读取芯片运行状态...", async cancellationToken =>
        {
            var runtimeStatus = await ReadAndApplyRuntimeStatusAsync(cancellationToken, logResult: true);
            SetStatus(runtimeStatus.IsSramMirrorActive
                ? "检测到 SRAM 镜像：0x0000-0x003F 为易失性 SRAM"
                : $"运行状态：{runtimeStatus.ArbiterModeDisplay}；用户区当前映射到 EEPROM");

            if (showDetails)
            {
                MessageBox.Show(
                    this,
                    BuildRuntimeStatusDetails(runtimeStatus),
                    "NTA5332 运行状态",
                    MessageBoxButtons.OK,
                    runtimeStatus.IsSramMirrorActive ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        });
    }

    private async Task<Ntag5RuntimeStatus> ReadAndApplyRuntimeStatusAsync(
        CancellationToken cancellationToken,
        bool logResult = false)
    {
        var runtimeStatus = await Task.Run(_device.ReadRuntimeStatus, cancellationToken);
        _runtimeStatus = runtimeStatus;
        UpdateRuntimeStatusDisplay();
        if (logResult)
        {
            AppendLog(
                $"运行状态：CONFIG_1_REG=0x{runtimeStatus.Config1Register:X2}，" +
                $"{runtimeStatus.ArbiterModeDisplay}，SRAM {(runtimeStatus.SramEnabled ? "启用" : "关闭")}；" +
                runtimeStatus.UserMemoryMappingDisplay);
        }

        return runtimeStatus;
    }

    private void UpdateRuntimeStatusDisplay()
    {
        if (_runtimeStatus is null)
        {
            _runtimeStateLabel.Text = "存储：未检测";
            _runtimeStateLabel.ForeColor = Color.FromArgb(97, 97, 97);
            return;
        }

        _runtimeStateLabel.Text = _runtimeStatus.CompactDisplay;
        _runtimeStateLabel.ForeColor = _runtimeStatus.IsSramMirrorActive
            ? Color.FromArgb(183, 86, 0)
            : Accent;
        _toolTip.SetToolTip(_runtimeStateLabel, BuildRuntimeStatusDetails(_runtimeStatus));
    }

    private static string BuildRuntimeStatusDetails(Ntag5RuntimeStatus runtimeStatus) =>
        $"当前模式：{runtimeStatus.ArbiterModeDisplay}{Environment.NewLine}" +
        $"SRAM：{(runtimeStatus.SramEnabled ? "已启用" : "未启用")}{Environment.NewLine}" +
        $"用户区映射：{runtimeStatus.UserMemoryMappingDisplay}{Environment.NewLine}" +
        $"主机用途：{runtimeStatus.UseCaseDisplay}{Environment.NewLine}" +
        $"透传方向：{runtimeStatus.TransferDirectionDisplay}{Environment.NewLine}" +
        $"CONFIG_1_REG：0x{runtimeStatus.Config1Register:X2}{Environment.NewLine}{Environment.NewLine}" +
        (runtimeStatus.IsSramMirrorActive
            ? "注意：前 256 字节断电后会丢失，不属于持久化 EEPROM。"
            : "当前普通用户区读写访问的是持久化 EEPROM。");

    private async Task ReadChipAsync()
    {
        await RunOperationAsync("正在读取 2044 字节用户区...", async cancellationToken =>
        {
            var runtimeStatus = await ReadAndApplyRuntimeStatusAsync(cancellationToken);
            var progress = new Progress<int>(value => _progressBar.Value = value);
            var bytes = await Task.Run(
                () => _device.ReadUserMemory(progress, cancellationToken),
                cancellationToken);

            _baselineImage = (byte[])bytes.Clone();
            SetWorkingImage(bytes, $"芯片读取 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AppendLog($"读取完成：{Ntag5Memory.UserBlockCount} 块，SHA-256 {Ntag5Memory.Sha256(bytes)}");
            AppendLog($"本次读取映射：{runtimeStatus.UserMemoryMappingDisplay}");
            SetStatus("读取完成；黄色单元格将表示后续编辑与芯片基准的差异");
        });
    }

    private void OpenBackup()
    {
        TryUserAction("打开备份", () =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "原始卡片文件 (*.bin;*.mfd;*.dump)|*.bin;*.mfd;*.dump|NTAG5 原始备份 (*.bin)|*.bin|PCR532/libnfc 文件 (*.mfd;*.dump)|*.mfd;*.dump|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "打开 NTAG5 或 MIFARE Classic 原始卡片文件"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var dump = BackupService.LoadDump(dialog.FileName);
            if (_baselineImage is null || _baselineImage.Length != dump.Bytes.Length || !dump.IsNtag5)
            {
                _baselineImage = dump.IsNtag5 ? null : (byte[])dump.Bytes.Clone();
            }

            SetWorkingImage(
                dump.Bytes,
                $"{dump.DisplayName} · {Path.GetFileName(dialog.FileName)}",
                dump.Kind);
            AppendLog($"已打开：{dialog.FileName}");
            AppendLog($"识别格式：{dump.DisplayName}，{dump.Bytes.Length} 字节。");

            if (dump.IsNtag5)
            {
                SetStatus(_baselineImage is null
                    ? "NTAG5 备份已载入；写入前将自动读取并备份当前芯片"
                    : "NTAG5 备份已载入；黄色单元格表示它与最近芯片读取的差异");
                return;
            }

            SetStatus($"已载入 {dump.DisplayName}；可离线查看、编辑、转换并按原尺寸保存");
            MessageBox.Show(
                this,
                $"已识别为 {dump.DisplayName} 原始文件，共 {dump.Bytes.Length} 字节。\r\n\r\n" +
                "可以离线查看、编辑、编码转换并按原尺寸保存。NTA5332 不是 MIFARE Classic 读写器，因此不会启用“写入变化”和芯片校验。",
                "S50 / MIFARE Classic 文件已载入",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private void SaveBackup()
    {
        TryUserAction("保存备份", () =>
        {
            var data = GetWorkingSnapshot();
            using var dialog = new SaveFileDialog
            {
                Filter = "原始卡片文件 (*.bin;*.mfd;*.dump)|*.bin;*.mfd;*.dump|NTAG5 原始备份 (*.bin)|*.bin|PCR532/libnfc 文件 (*.mfd;*.dump)|*.mfd;*.dump|所有文件 (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "bin",
                FileName = $"NTA5332-user-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
                Title = $"保存 {CardDumpFormat.GetDisplayName(_workingDumpKind)} 原始文件"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var isMfd = Path.GetExtension(dialog.FileName).Equals(".mfd", StringComparison.OrdinalIgnoreCase);
            BackupService.SaveCardDump(dialog.FileName, data, _imageSource, _workingDumpKind, isMfd);

            AppendLog($"备份已保存：{dialog.FileName}");
            AppendLog($"元数据已保存：{dialog.FileName}.json");
            SetStatus("备份与校验元数据保存完成");
        });
    }

    private void SaveMfdDump()
    {
        TryUserAction("导出 MFD", () =>
        {
            var data = GetWorkingSnapshot();
            using var dialog = new SaveFileDialog
            {
                Filter = "PCR532/libnfc Dump (*.mfd)|*.mfd|所有文件 (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "mfd",
                FileName = $"{(_workingDumpKind == CardDumpKind.Ntag5UserMemory ? "NTA5332-user" : "MIFARE-dump")}-{DateTime.Now:yyyyMMdd-HHmmss}.mfd",
                Title = "导出 PCR532/libnfc 兼容 MFD dump"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            BackupService.SaveCardDump(dialog.FileName, data, _imageSource, _workingDumpKind, mfdExtension: true);
            AppendLog($"MFD 原始 dump 已保存：{dialog.FileName}");
            AppendLog($"文件内容为 {data.Length} 字节 {CardDumpFormat.GetDisplayName(_workingDumpKind)} 原始镜像，不包含自定义文件头。");
            AppendLog($"元数据已保存：{dialog.FileName}.json");
            SetStatus("MFD dump 与校验元数据保存完成");
        });
    }

    private async Task WriteChangesAsync()
    {
        var target = GetNtag5WorkingSnapshot();
        await ExecuteChipWriteAsync(
            "写入前正在读取当前芯片...",
            _ => new PreparedChipWrite(
                target,
                "目标来源：当前编辑区。",
                "写入检查：芯片与编辑区完全一致，无需写入。",
                "无需写入：芯片内容与编辑区一致",
                $"芯片写入 {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
    }

    private async Task ExecuteChipWriteAsync(string initialStatus, Func<byte[], PreparedChipWrite> prepareWrite)
    {
        _baselineImage = null;
        RefreshDifferenceDisplay();

        await RunOperationAsync(initialStatus, async cancellationToken =>
        {
            var runtimeStatus = await ReadAndApplyRuntimeStatusAsync(cancellationToken, logResult: true);
            var readProgress = new Progress<int>(value => _progressBar.Value = value / 5);
            var current = await Task.Run(
                () => _device.ReadUserMemory(readProgress, cancellationToken),
                cancellationToken);
            var prepared = prepareWrite(current);
            Ntag5Memory.ValidateImage(prepared.Target);
            var changedBlocks = Ntag5Memory.GetChangedBlocks(current, prepared.Target);

            if (changedBlocks.Count == 0)
            {
                ApplyChipImage(prepared.Target, prepared.SuccessSource);
                AppendLog(prepared.NoChangeLog);
                SetStatus(prepared.NoChangeStatus);
                return;
            }

            var firstBlock = changedBlocks[0];
            var lastBlock = changedBlocks[^1];
            var touchesMirroredSram = runtimeStatus.IsSramMirrorActive &&
                                      changedBlocks.Any(block => block <= Ntag5Memory.LastSramMirrorBlock);
            var mappingWarning = touchesMirroredSram
                ? "\r\n\r\n警告：变化包含 0x000-0x03F，这些块当前映射到 SRAM，断电后不会保留。"
                : string.Empty;
            var confirmation = MessageBox.Show(
                this,
                $"{prepared.ConfirmationIntro}\r\n\r\n" +
                $"将写入 {changedBlocks.Count} 个变化块（0x{firstBlock:X3} 至 0x{lastBlock:X3} 范围内）。\r\n\r\n" +
                "程序会先把当前芯片完整备份到“文档\\Ntag5Studio\\Backups”，然后逐块写入并回读校验。" +
                mappingWarning + "\r\n\r\n继续写入吗？",
                "确认写入 NTAG5 用户区",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                _baselineImage = (byte[])current.Clone();
                RefreshDifferenceDisplay();
                AppendLog("用户取消写入，芯片未发生变化。");
                SetStatus("已取消写入");
                return;
            }

            var backupPath = BackupService.SaveAutomaticPreWrite(current);
            AppendLog($"写入前自动备份：{backupPath}");

            for (var index = 0; index < changedBlocks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = changedBlocks[index];
                var blockData = prepared.Target.AsSpan(
                    block * Ntag5Memory.BytesPerBlock,
                    Ntag5Memory.BytesPerBlock).ToArray();

                SetStatus($"正在写入并校验块 0x{block:X3}（{index + 1}/{changedBlocks.Count}）");
                await Task.Run(() => _device.WriteBlock(block, blockData), cancellationToken);
                await Task.Delay(12, cancellationToken);
                var verified = await Task.Run(
                    () => _device.ReadMemory(block, Ntag5Memory.BytesPerBlock),
                    cancellationToken);

                if (!verified.AsSpan().SequenceEqual(blockData))
                {
                    throw new IOException(
                        $"块 0x{block:X3} 回读校验失败。期望 {HexCodec.ToSpacedHex(blockData)}，" +
                        $"实际 {HexCodec.ToSpacedHex(verified)}。请重新读取芯片后再判断是否重试。");
                }

                _progressBar.Value = 20 + (index + 1) * 80 / changedBlocks.Count;
                AppendLog($"已写入并校验块 0x{block:X3}：{HexCodec.ToSpacedHex(blockData)}");
            }

            ApplyChipImage(prepared.Target, prepared.SuccessSource);
            AppendLog($"写入完成：{changedBlocks.Count} 个块全部回读一致。");
            SetStatus($"写入完成并通过校验：{changedBlocks.Count} 个变化块");
        });
    }

    private async Task VerifyAsync()
    {
        var expected = GetNtag5WorkingSnapshot();
        await RunOperationAsync("正在读取芯片并校验...", async cancellationToken =>
        {
            await ReadAndApplyRuntimeStatusAsync(cancellationToken);
            var progress = new Progress<int>(value => _progressBar.Value = value);
            var actual = await Task.Run(
                () => _device.ReadUserMemory(progress, cancellationToken),
                cancellationToken);
            var differences = Ntag5Memory.GetChangedBlocks(actual, expected);
            _baselineImage = (byte[])actual.Clone();
            RefreshDifferenceDisplay();

            if (differences.Count == 0)
            {
                AppendLog("完整校验通过：芯片 2044 字节与编辑区一致。");
                SetStatus("完整校验通过：2044 字节一致");
                MessageBox.Show(this, "芯片用户区与编辑区完全一致。", "校验通过", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppendLog($"校验发现 {differences.Count} 个不同块；首个为 0x{differences[0]:X3}。");
            SetStatus($"校验不一致：{differences.Count} 个块不同，已用黄色标出");
            MessageBox.Show(
                this,
                $"发现 {differences.Count} 个不同块。\r\n首个不同块：0x{differences[0]:X3}\r\n\r\n差异已在用户区表格中以黄色标出。",
                "校验结果",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        });
    }

    private async Task RunOperationAsync(string initialStatus, Func<CancellationToken, Task> operation)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _operationCts = new CancellationTokenSource();
        _progressBar.Value = 0;
        SetStatus(initialStatus);
        UpdateActionAvailability();

        try
        {
            await operation(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("操作已取消。若取消发生在写入过程中，请重新读取芯片确认当前内容。");
            SetStatus("操作已取消");
        }
        catch (Exception exception)
        {
            AppendLog($"错误：{exception.Message}");
            SetStatus("操作失败，请查看日志");
            MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            _busy = false;
            UpdateActionAvailability();
        }
    }

    private void SetWorkingImage(
        byte[] image,
        string source,
        CardDumpKind kind = CardDumpKind.Ntag5UserMemory)
    {
        CardDumpFormat.Validate(image, kind);
        _workingImage = (byte[])image.Clone();
        _imageSource = source;
        _workingDumpKind = kind;
        PopulateGrid();
        RefreshDifferenceDisplay();
        UpdateSelectedBlockDetails();
        UpdateActionAvailability();
    }

    private void ApplyChipImage(byte[] image, string source)
    {
        Ntag5Memory.ValidateImage(image);
        _workingImage = (byte[])image.Clone();
        _baselineImage = (byte[])image.Clone();
        _imageSource = source;
        _workingDumpKind = CardDumpKind.Ntag5UserMemory;
        PopulateGrid();
        RefreshDifferenceDisplay();
        UpdateSelectedBlockDetails();
        UpdateActionAvailability();
    }

    private byte[] GetWorkingSnapshot()
    {
        if (_workingImage is null)
        {
            throw new InvalidOperationException("请先读取芯片或打开一份支持的原始卡片文件。");
        }

        _memoryGrid.EndEdit();
        return (byte[])_workingImage.Clone();
    }

    private byte[] GetNtag5WorkingSnapshot()
    {
        var data = GetWorkingSnapshot();
        if (!HasNtag5WorkingImage)
        {
            throw new InvalidOperationException(
                $"当前载入的是 {CardDumpFormat.GetDisplayName(_workingDumpKind)} 文件（{data.Length} 字节）。" +
                "NTA5332 只能写入 2044 字节 NTAG5 用户区镜像；S50 文件只能离线编辑和保存。");
        }

        Ntag5Memory.ValidateImage(data);
        return data;
    }

    private void PopulateGrid()
    {
        if (_workingImage is null)
        {
            return;
        }

        _loadingGrid = true;
        try
        {
            var rowCount = (_workingImage.Length + Ntag5Memory.BytesPerBlock - 1) / Ntag5Memory.BytesPerBlock;
            if (_memoryGrid.Rows.Count != rowCount)
            {
                _memoryGrid.Rows.Clear();
                _memoryGrid.Columns[0].HeaderText = CardDumpFormat.IsMifareClassic(_workingDumpKind) ? "块.组" : "块";
                for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
                {
                    var offset = rowIndex * Ntag5Memory.BytesPerBlock;
                    var blockLabel = CardDumpFormat.IsMifareClassic(_workingDumpKind)
                        ? $"0x{rowIndex / 4:X2}.{rowIndex % 4}"
                        : $"0x{rowIndex:X3}";
                    _memoryGrid.Rows.Add(blockLabel, $"0x{offset:X4}", "--", "--", "--", "--", "....");
                }
            }

            for (var block = 0; block < rowCount; block++)
            {
                RefreshGridRow(block);
            }
        }
        finally
        {
            _loadingGrid = false;
        }
    }

    private void RefreshGridRow(int block)
    {
        if (_workingImage is null)
        {
            return;
        }

        var row = _memoryGrid.Rows[block];
        var offset = block * Ntag5Memory.BytesPerBlock;
        if (offset >= _workingImage.Length)
        {
            return;
        }

        for (var column = 0; column < Ntag5Memory.BytesPerBlock; column++)
        {
            var available = offset + column < _workingImage.Length;
            row.Cells[column + 2].Value = available ? _workingImage[offset + column].ToString("X2") : "--";
            row.Cells[column + 2].ReadOnly = !available;
        }

        var byteCount = Math.Min(Ntag5Memory.BytesPerBlock, _workingImage.Length - offset);
        row.Cells[6].Value = Ntag5Memory.ToDisplayAscii(_workingImage.AsSpan(offset, byteCount));
    }

    private void ValidateHexCell(object? sender, DataGridViewCellValidatingEventArgs eventArgs)
    {
        if (_loadingGrid || _workingImage is null || eventArgs.ColumnIndex is < 2 or > 5)
        {
            return;
        }

        try
        {
            _ = HexCodec.ParseByte(eventArgs.FormattedValue?.ToString());
        }
        catch (FormatException exception)
        {
            eventArgs.Cancel = true;
            MessageBox.Show(this, exception.Message, "十六进制输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CommitHexCell(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (_loadingGrid || _workingImage is null || eventArgs.RowIndex < 0 || eventArgs.ColumnIndex is < 2 or > 5)
        {
            return;
        }

        var offset = eventArgs.RowIndex * Ntag5Memory.BytesPerBlock + eventArgs.ColumnIndex - 2;
        if (offset >= _workingImage.Length)
        {
            return;
        }

        var value = HexCodec.ParseByte(_memoryGrid.Rows[eventArgs.RowIndex].Cells[eventArgs.ColumnIndex].Value?.ToString());
        _workingImage[offset] = value;
        _memoryGrid.Rows[eventArgs.RowIndex].Cells[eventArgs.ColumnIndex].Value = value.ToString("X2");
        RefreshGridRow(eventArgs.RowIndex);
        RefreshDifferenceDisplay();
        UpdateSelectedBlockDetails();
    }

    private void FormatChangedCell(object? sender, DataGridViewCellFormattingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex is < 2 or > 5 || _workingImage is null)
        {
            return;
        }

        var offset = eventArgs.RowIndex * Ntag5Memory.BytesPerBlock + eventArgs.ColumnIndex - 2;
        var changed = _baselineImage is not null &&
                      _baselineImage.Length == _workingImage.Length &&
                      offset < _workingImage.Length &&
                      _workingImage[offset] != _baselineImage[offset];
        eventArgs.CellStyle.BackColor = changed ? Warning : Color.White;
    }

    private void RefreshDifferenceDisplay()
    {
        _sourceValueLabel.Text = _imageSource;
        _hashValueLabel.Text = _workingImage is null ? "-" : ShortHash(Ntag5Memory.Sha256(_workingImage));
        if (_workingImage is null)
        {
            _differenceValueLabel.Text = "-";
        }
        else if (_baselineImage is null)
        {
            _differenceValueLabel.Text = "无芯片比较基准";
        }
        else if (_baselineImage.Length != _workingImage.Length)
        {
            _differenceValueLabel.Text = "比较基准长度不同";
        }
        else
        {
            var blockSize = CardDumpFormat.GetLogicalBlockSize(_workingDumpKind);
            var count = 0;
            for (var offset = 0; offset < _workingImage.Length; offset += blockSize)
            {
                if (!_workingImage.AsSpan(offset, Math.Min(blockSize, _workingImage.Length - offset))
                    .SequenceEqual(_baselineImage.AsSpan(offset, Math.Min(blockSize, _baselineImage.Length - offset))))
                {
                    count++;
                }
            }

            _differenceValueLabel.Text = count == 0 ? "0（完全一致）" : $"{count} 个块";
        }

        _memoryGrid.Invalidate();
    }

    private void UpdateSelectedBlockDetails()
    {
        if (_workingImage is null || _memoryGrid.CurrentCell is null)
        {
            _selectedBlockLabel.Text = "未选择";
            _selectedBlockDetailsBox.Clear();
            return;
        }

        var block = _memoryGrid.CurrentCell.RowIndex;
        var offset = block * Ntag5Memory.BytesPerBlock;
        if (CardDumpFormat.IsMifareClassic(_workingDumpKind))
        {
            var mifareBlock = block / 4;
            var blockOffset = mifareBlock * CardDumpFormat.MifareBlockSize;
            var blockBytes = _workingImage.AsSpan(blockOffset, CardDumpFormat.MifareBlockSize);
            var sector = CardDumpFormat.GetSectorNumber(_workingDumpKind, mifareBlock);
            var trailer = CardDumpFormat.IsSectorTrailer(_workingDumpKind, mifareBlock);
            var cardLabel = _workingDumpKind == CardDumpKind.MifareClassic1K ? "S50" : "MIFARE";
            _selectedBlockLabel.Text = $"{cardLabel} 块 0x{mifareBlock:X2} / 扇区 {sector} / 偏移 0x{blockOffset:X4}";
            _selectedBlockDetailsBox.Text =
                $"Hex    {HexCodec.ToSpacedHex(blockBytes)}{Environment.NewLine}" +
                $"ASCII  {Ntag5Memory.ToDisplayAscii(blockBytes)}{Environment.NewLine}" +
                (trailer
                    ? $"类型   扇区 Trailer（Key A / Access / Key B）{Environment.NewLine}" +
                      $"Key A  {HexCodec.ToSpacedHex(blockBytes[..6])}{Environment.NewLine}" +
                      $"Access {HexCodec.ToSpacedHex(blockBytes.Slice(6, 4))}{Environment.NewLine}" +
                      $"Key B  {HexCodec.ToSpacedHex(blockBytes.Slice(10, 6))}"
                    : $"类型   数据块{Environment.NewLine}" +
                      $"UInt32 LE  {BinaryPrimitives.ReadUInt32LittleEndian(blockBytes)}{Environment.NewLine}" +
                      $"UInt32 BE  {BinaryPrimitives.ReadUInt32BigEndian(blockBytes)}");
            return;
        }

        var bytes = _workingImage.AsSpan(offset, Ntag5Memory.BytesPerBlock);
        _selectedBlockLabel.Text = $"块 0x{block:X3} / 偏移 0x{offset:X4}";
        _selectedBlockDetailsBox.Text =
            $"Hex    {HexCodec.ToSpacedHex(bytes)}{Environment.NewLine}" +
            $"ASCII  {Ntag5Memory.ToDisplayAscii(bytes)}{Environment.NewLine}" +
            $"UInt32 LE  {BinaryPrimitives.ReadUInt32LittleEndian(bytes)}{Environment.NewLine}" +
            $"UInt32 BE  {BinaryPrimitives.ReadUInt32BigEndian(bytes)}";
    }

    private void JumpToBlock()
    {
        TryUserAction("跳转", () =>
        {
            var block = ParseHexNumber(_jumpBlockBox.Text, "块地址");
            var maxBlock = CardDumpFormat.IsMifareClassic(_workingDumpKind)
                ? CardDumpFormat.GetLogicalBlockCount(_workingDumpKind) - 1
                : Ntag5Memory.LastI2cUserBlock;
            if (block < 0 || block > maxBlock)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(block),
                    CardDumpFormat.IsMifareClassic(_workingDumpKind)
                        ? $"块地址必须在 00-{maxBlock:X2} 之间。"
                        : "块地址必须在 000-1FE 之间。");
            }

            _memoryGrid.ClearSelection();
            var row = CardDumpFormat.IsMifareClassic(_workingDumpKind) ? block * 4 : block;
            _memoryGrid.CurrentCell = _memoryGrid.Rows[row].Cells[2];
            _memoryGrid.Rows[row].Cells[2].Selected = true;
            _memoryGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, row - 5);
        });
    }

    private void ConvertInput()
    {
        TryUserAction("编码转换", () =>
        {
            var kind = (DataEncodingKind)_inputKindBox.SelectedIndex;
            var bytes = HexCodec.Parse(_conversionInputBox.Text, kind);
            _convertedBytes = bytes;
            _hexOutputBox.Text = HexCodec.ToSpacedHex(bytes);
            _utf8OutputBox.Text = HexCodec.ToUtf8(bytes);
            _asciiOutputBox.Text = HexCodec.ToAscii(bytes);
            _utf16LeOutputBox.Text = HexCodec.ToUtf16LittleEndian(bytes);
            _utf16BeOutputBox.Text = HexCodec.ToUtf16BigEndian(bytes);
            _gb18030OutputBox.Text = HexCodec.ToGb18030(bytes);
            _decimalOutputBox.Text = HexCodec.ToDecimal(bytes);
            _binaryOutputBox.Text = HexCodec.ToBinary(bytes);
            _base64OutputBox.Text = Convert.ToBase64String(bytes);
            _urlPercentOutputBox.Text = HexCodec.ToUrlPercent(bytes);
            UpdateOutputInfo();
            SetStatus($"编码转换完成：{bytes.Length} 字节");
        });
    }

    private void CopyCurrentOutput()
    {
        TryUserAction("复制转换结果", () =>
        {
            var output = _outputTabs.SelectedTab?.Controls.OfType<RichTextBox>().FirstOrDefault();
            if (output is null || output.TextLength == 0)
            {
                throw new InvalidOperationException("当前结果为空，请先执行转换。");
            }

            Clipboard.SetText(output.Text);
            SetStatus($"已复制 {_outputTabs.SelectedTab?.Text} 结果");
        });
    }

    private void UpdateOutputInfo()
    {
        var output = _outputTabs.SelectedTab?.Controls.OfType<RichTextBox>().FirstOrDefault();
        var format = _outputTabs.SelectedTab?.Text ?? "结果";
        _outputInfoLabel.Text = output is null
            ? "转换后可切换下方结果标签"
            : $"{format}  |  {output.TextLength:N0} 个字符";
    }

    private void LoadCurrentImageIntoConverter()
    {
        TryUserAction("载入当前用户区", () =>
        {
            var data = GetWorkingSnapshot();
            _inputKindBox.SelectedIndex = (int)DataEncodingKind.Hexadecimal;
            _conversionInputBox.Text = HexCodec.ToSpacedHex(data);
            ConvertInput();
        });
    }

    private void InsertConvertedBytes()
    {
        TryUserAction("插入转换结果", () =>
        {
            if (_workingImage is null)
            {
                throw new InvalidOperationException("请先读取芯片或打开备份，再把转换结果插入编辑区。");
            }

            ConvertInput();
            if (_convertedBytes is null || _convertedBytes.Length == 0)
            {
                throw new InvalidOperationException("没有可插入的字节。");
            }

            var offset = ParseHexNumber(_insertOffsetBox.Text, "偏移");
            if (offset < 0 || _workingImage is null || offset + _convertedBytes.Length > _workingImage.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"插入范围必须位于 0x0000-0x{_workingImage?.Length - 1:X4}。当前数据为 {_convertedBytes.Length} 字节。");
            }

            var confirmation = MessageBox.Show(
                this,
                $"将 {_convertedBytes.Length} 字节写入离线编辑区偏移 0x{offset:X4}。\r\n这不会立即写入芯片，是否继续？",
                "插入到编辑区",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            Buffer.BlockCopy(_convertedBytes, 0, _workingImage, offset, _convertedBytes.Length);
            var firstBlock = offset / Ntag5Memory.BytesPerBlock;
            var lastBlock = (offset + _convertedBytes.Length - 1) / Ntag5Memory.BytesPerBlock;
            _loadingGrid = true;
            try
            {
                for (var block = firstBlock; block <= lastBlock; block++)
                {
                    RefreshGridRow(block);
                }
            }
            finally
            {
                _loadingGrid = false;
            }

            _imageSource = "编码转换插入后的编辑结果";
            RefreshDifferenceDisplay();
            AppendLog($"离线编辑：在偏移 0x{offset:X4} 插入 {_convertedBytes.Length} 字节。尚未写入芯片。");
            SetStatus(HasNtag5WorkingImage
                ? "已插入离线编辑区；使用“写入变化”才会修改芯片"
                : "已插入 S50 离线文件；使用“保存备份”或“导出MFD”保存修改");
        });
    }

    private void PreviewDirectWritePayload()
    {
        TryUserAction("直接写入预览", () =>
        {
            var payload = BuildDirectWritePayload();
            _directPreviewBox.Text = BuildDirectWritePreview(payload);
            _directWriteInfoLabel.Text =
                $"{payload.Bytes.Length} 字节，偏移 0x{payload.Offset:X4}，影响块 0x{payload.FirstBlock:X3}-0x{payload.LastBlock:X3}";
            SetStatus("直接写入预览已生成");
        });
    }

    private async Task WriteDirectContentAsync()
    {
        DirectWritePayload payload;
        try
        {
            payload = BuildDirectWritePayload();
            _directPreviewBox.Text = BuildDirectWritePreview(payload);
            _directWriteInfoLabel.Text =
                $"{payload.Bytes.Length} 字节，偏移 0x{payload.Offset:X4}，影响块 0x{payload.FirstBlock:X3}-0x{payload.LastBlock:X3}";
        }
        catch (Exception exception)
        {
            AppendLog($"直接写入准备失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "直接写入准备失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await ExecuteChipWriteAsync(
            "直接写入前正在读取当前 TAG...",
            current =>
            {
                var target = (byte[])current.Clone();
                Buffer.BlockCopy(payload.Bytes, 0, target, payload.Offset, payload.Bytes.Length);
                return new PreparedChipWrite(
                    target,
                    $"目标来源：直接输入（{payload.KindDisplay}）。\r\n" +
                    $"将把 {payload.Bytes.Length} 字节覆盖到偏移 0x{payload.Offset:X4}，影响块 0x{payload.FirstBlock:X3}-0x{payload.LastBlock:X3}。\r\n" +
                    "其它用户区字节会以刚刚读取到的当前 TAG 内容保留。",
                    "直接写入检查：输入内容已与当前 TAG 一致，无需写入。",
                    "无需写入：直接输入内容与当前 TAG 一致",
                    $"直接写入 {payload.Bytes.Length} 字节 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            });
    }

    private DirectWritePayload BuildDirectWritePayload()
    {
        var kind = (DataEncodingKind)_directInputKindBox.SelectedIndex;
        var bytes = HexCodec.Parse(_directInputBox.Text, kind);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("请输入要写入 TAG 的内容。");
        }

        var address = ParseHexNumber(_directAddressBox.Text, _directAddressModeBox.SelectedIndex == 1 ? "块地址" : "偏移");
        var offset = _directAddressModeBox.SelectedIndex == 1
            ? checked(address * Ntag5Memory.BytesPerBlock)
            : address;

        if (offset < 0 || offset + bytes.Length > Ntag5Memory.UserByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"写入范围必须位于 0x0000-0x{Ntag5Memory.UserByteCount - 1:X4}。当前内容为 {bytes.Length} 字节，起始偏移为 0x{offset:X4}。");
        }

        var firstBlock = offset / Ntag5Memory.BytesPerBlock;
        var lastBlock = (offset + bytes.Length - 1) / Ntag5Memory.BytesPerBlock;
        var kindDisplay = _directInputKindBox.SelectedItem?.ToString() ?? kind.ToString();
        return new DirectWritePayload(bytes, kind, kindDisplay, offset, firstBlock, lastBlock);
    }

    private static string BuildDirectWritePreview(DirectWritePayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"输入格式：{payload.KindDisplay}");
        builder.AppendLine($"字节数：{payload.Bytes.Length}");
        builder.AppendLine($"目标偏移：0x{payload.Offset:X4}");
        builder.AppendLine($"受影响块：0x{payload.FirstBlock:X3} - 0x{payload.LastBlock:X3}");
        builder.AppendLine();
        builder.AppendLine("写入字节（Hex）：");
        builder.AppendLine(HexCodec.ToSpacedHex(payload.Bytes));
        builder.AppendLine();
        builder.AppendLine("UTF-8 预览：");
        builder.AppendLine(HexCodec.ToUtf8(payload.Bytes));
        builder.AppendLine();
        builder.AppendLine("ASCII 预览：");
        builder.AppendLine(HexCodec.ToAscii(payload.Bytes));
        builder.AppendLine();
        builder.AppendLine("说明：写入时会先读取当前 TAG，保留目标范围以外的所有用户区字节。");
        return builder.ToString();
    }

    private void ParseCurrentNdef()
    {
        TryUserAction("Type 5 / NDEF 解析", () =>
        {
            var data = GetNtag5WorkingSnapshot();
            _ndefOutputBox.Text = NdefParser.ParseType5Image(data);
            SetStatus("Type 5 TLV / NDEF 解析完成");
        });
    }

    private void UpdateActionAvailability()
    {
        var connected = _device.IsConnected;
        _connectButton.Enabled = !_busy;
        _connectButton.Text = connected ? "断开" : "连接";
        _devicePathBox.Enabled = !_busy && !connected;
        _readButton.Enabled = !_busy && connected;
        _runtimeStatusButton.Enabled = !_busy && connected;
        _openButton.Enabled = !_busy;
        _saveButton.Enabled = !_busy && _workingImage is not null;
        _saveMfdButton.Enabled = !_busy && _workingImage is not null;
        _writeButton.Enabled = !_busy && connected && HasNtag5WorkingImage;
        _verifyButton.Enabled = !_busy && connected && HasNtag5WorkingImage;
        _parseNdefButton.Enabled = !_busy && HasNtag5WorkingImage;
        _insertBytesButton.Enabled = !_busy && _workingImage is not null;
        _directPreviewButton.Enabled = !_busy;
        _directWriteButton.Enabled = !_busy && connected;
        var pcrReady = !_busy && _pcr532.IsRuntimeAvailable;
        _pcrPortBox.Enabled = !_busy;
        _pcrBaudBox.Enabled = !_busy;
        _pcrRefreshPortsButton.Enabled = !_busy;
        _pcrDetectButton.Enabled = pcrReady;
        _pcrInstallDriverButton.Enabled = !_busy && _pcr532.GetDriverInstallerPath() is not null;
        _pcrBrowseKeyButton.Enabled = !_busy;
        _pcrRecoveryReadButton.Enabled = pcrReady;
        _pcrKnownReadButton.Enabled = pcrReady;
        _pcrWriteButton.Enabled = pcrReady;
        _pcrMagicWriteButton.Enabled = pcrReady;
        _pcrSetUidButton.Enabled = pcrReady;
        _pcrFormatUidButton.Enabled = pcrReady;
        _pcrLockUfuidButton.Enabled = pcrReady;
        _pcrType2ReadButton.Enabled = pcrReady;
        _pcrType2WriteButton.Enabled = pcrReady;
        _pcrAutoBackupCheck.Enabled = !_busy;
        _cancelButton.Enabled = _busy;
        _memoryGrid.ReadOnly = _busy || _workingImage is null;
        _connectionStateLabel.Text = connected ? "已连接" : "未连接";
        _connectionStateLabel.ForeColor = connected ? Accent : Color.FromArgb(97, 97, 97);
        _toolTip.SetToolTip(
            _writeButton,
            HasNtag5WorkingImage
                ? "写入当前 NTAG5 用户区编辑结果"
                : "S50 / MIFARE Classic 文件仅支持离线编辑和保存，不能通过 NTA5332 写入");
        _toolTip.SetToolTip(
            _verifyButton,
            HasNtag5WorkingImage
                ? "读取并校验 NTAG5 用户区"
                : "S50 / MIFARE Classic 文件不能用 NTAG5 I2C 接口校验");
    }

    private void TryUserAction(string title, Action action)
    {
        try
        {
            action();
            UpdateActionAvailability();
        }
        catch (Exception exception)
        {
            AppendLog($"{title}失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, title + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    private static string ShortHash(string hash) => hash.Length <= 20 ? hash : hash[..20] + "...";

    private static int ParseHexNumber(string text, string fieldName)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (!int.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"{fieldName}必须为十六进制数，例如 01A 或 0x01A。");
        }

        return value;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_busy)
        {
            _operationCts?.Cancel();
            eventArgs.Cancel = true;
            MessageBox.Show(this, "正在取消当前操作，请在状态栏显示完成后再关闭。", "操作进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _device.Dispose();
    }
}
