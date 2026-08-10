using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Ntag5Studio.Core;
using Ntag5Studio.Hardware;
using Ntag5Studio.Services;

namespace Ntag5Studio.UI;

public sealed partial class MainForm : Form
{
    private readonly SpbNtag5Device _device = new();
    private byte[]? _workingImage;
    private byte[]? _baselineImage;
    private byte[]? _convertedBytes;
    private CancellationTokenSource? _operationCts;
    private bool _busy;
    private bool _loadingGrid;
    private string _imageSource = "尚未载入";
    private Ntag5RuntimeStatus? _runtimeStatus;

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
                Filter = "NTAG5 原始备份 / Dump (*.bin;*.mfd)|*.bin;*.mfd|NTAG5 原始备份 (*.bin)|*.bin|PCR532/libnfc Dump (*.mfd)|*.mfd|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "打开 2044 字节 NTAG5 用户区备份或 MFD dump"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var bytes = BackupService.Load(dialog.FileName);
            SetWorkingImage(bytes, $"备份 {Path.GetFileName(dialog.FileName)}");
            AppendLog($"已打开备份：{dialog.FileName}");
            SetStatus(_baselineImage is null
                ? "备份已载入；写入前将自动读取并备份当前芯片"
                : "备份已载入；黄色单元格表示它与最近芯片读取的差异");
        });
    }

    private void SaveBackup()
    {
        TryUserAction("保存备份", () =>
        {
            var data = GetWorkingSnapshot();
            using var dialog = new SaveFileDialog
            {
                Filter = "NTAG5 原始备份 (*.bin)|*.bin|PCR532/libnfc Dump (*.mfd)|*.mfd|所有文件 (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "bin",
                FileName = $"NTA5332-user-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
                Title = "保存 NTAG5 用户区备份"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var isMfd = Path.GetExtension(dialog.FileName).Equals(".mfd", StringComparison.OrdinalIgnoreCase);
            if (isMfd)
            {
                BackupService.SaveMfdDump(dialog.FileName, data, _imageSource);
            }
            else
            {
                BackupService.Save(dialog.FileName, data, _imageSource);
            }

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
                FileName = $"NTA5332-user-{DateTime.Now:yyyyMMdd-HHmmss}.mfd",
                Title = "导出 PCR532/libnfc 兼容 MFD dump"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            BackupService.SaveMfdDump(dialog.FileName, data, _imageSource);
            AppendLog($"MFD 原始 dump 已保存：{dialog.FileName}");
            AppendLog("MFD 文件内容为 2044 字节线性用户区镜像，不包含自定义文件头。");
            AppendLog($"元数据已保存：{dialog.FileName}.json");
            SetStatus("MFD dump 与校验元数据保存完成");
        });
    }

    private async Task WriteChangesAsync()
    {
        var target = GetWorkingSnapshot();
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
        var expected = GetWorkingSnapshot();
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

    private void SetWorkingImage(byte[] image, string source)
    {
        Ntag5Memory.ValidateImage(image);
        _workingImage = (byte[])image.Clone();
        _imageSource = source;
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
        PopulateGrid();
        RefreshDifferenceDisplay();
        UpdateSelectedBlockDetails();
        UpdateActionAvailability();
    }

    private byte[] GetWorkingSnapshot()
    {
        if (_workingImage is null)
        {
            throw new InvalidOperationException("请先读取芯片或打开一份 2044 字节备份。");
        }

        _memoryGrid.EndEdit();
        return (byte[])_workingImage.Clone();
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
            for (var block = 0; block < Ntag5Memory.UserBlockCount; block++)
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
        for (var column = 0; column < Ntag5Memory.BytesPerBlock; column++)
        {
            row.Cells[column + 2].Value = _workingImage[offset + column].ToString("X2");
        }

        row.Cells[6].Value = Ntag5Memory.ToDisplayAscii(_workingImage.AsSpan(offset, Ntag5Memory.BytesPerBlock));
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

        var value = HexCodec.ParseByte(_memoryGrid.Rows[eventArgs.RowIndex].Cells[eventArgs.ColumnIndex].Value?.ToString());
        _workingImage[eventArgs.RowIndex * Ntag5Memory.BytesPerBlock + eventArgs.ColumnIndex - 2] = value;
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
        var changed = _baselineImage is not null && _workingImage[offset] != _baselineImage[offset];
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
        else
        {
            var count = Ntag5Memory.GetChangedBlocks(_baselineImage, _workingImage).Count;
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
            if (block is < 0 or > Ntag5Memory.LastI2cUserBlock)
            {
                throw new ArgumentOutOfRangeException(nameof(block), "块地址必须在 000-1FE 之间。");
            }

            _memoryGrid.ClearSelection();
            _memoryGrid.CurrentCell = _memoryGrid.Rows[block].Cells[2];
            _memoryGrid.Rows[block].Cells[2].Selected = true;
            _memoryGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, block - 5);
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
            if (offset < 0 || offset + _convertedBytes.Length > Ntag5Memory.UserByteCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"插入范围必须位于 0x0000-0x{Ntag5Memory.UserByteCount - 1:X4}。当前数据为 {_convertedBytes.Length} 字节。");
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
            SetStatus("已插入离线编辑区；使用“写入变化”才会修改芯片");
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
            var data = GetWorkingSnapshot();
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
        _writeButton.Enabled = !_busy && connected && _workingImage is not null;
        _verifyButton.Enabled = !_busy && connected && _workingImage is not null;
        _directPreviewButton.Enabled = !_busy;
        _directWriteButton.Enabled = !_busy && connected;
        _cancelButton.Enabled = _busy;
        _memoryGrid.ReadOnly = _busy || _workingImage is null;
        _connectionStateLabel.Text = connected ? "已连接" : "未连接";
        _connectionStateLabel.ForeColor = connected ? Accent : Color.FromArgb(97, 97, 97);
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
