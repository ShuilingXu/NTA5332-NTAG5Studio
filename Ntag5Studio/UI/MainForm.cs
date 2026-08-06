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

    public MainForm()
    {
        InitializeLayout();
        WireEvents();
        UpdateActionAvailability();
        AppendLog("程序已启动。硬件范围固定为用户 EEPROM 0x0000-0x01FE（2044 字节）。");
        AppendLog($"默认驱动路径：{SpbNtag5Device.DefaultDevicePath}");
    }

    private void WireEvents()
    {
        _connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        _readButton.Click += async (_, _) => await ReadChipAsync();
        _openButton.Click += (_, _) => OpenBackup();
        _saveButton.Click += (_, _) => SaveBackup();
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
        _parseNdefButton.Click += (_, _) => ParseCurrentNdef();
        _insertBytesButton.Click += (_, _) => InsertConvertedBytes();

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
        }
    }

    private async Task ReadChipAsync()
    {
        await RunOperationAsync("正在读取 2044 字节用户区...", async cancellationToken =>
        {
            var progress = new Progress<int>(value => _progressBar.Value = value);
            var bytes = await Task.Run(
                () => _device.ReadUserMemory(progress, cancellationToken),
                cancellationToken);

            _baselineImage = (byte[])bytes.Clone();
            SetWorkingImage(bytes, $"芯片读取 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AppendLog($"读取完成：{Ntag5Memory.UserBlockCount} 块，SHA-256 {Ntag5Memory.Sha256(bytes)}");
            SetStatus("读取完成；黄色单元格将表示后续编辑与芯片基准的差异");
        });
    }

    private void OpenBackup()
    {
        TryUserAction("打开备份", () =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "NTAG5 原始备份 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "打开 2044 字节 NTAG5 用户区备份"
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
                Filter = "NTAG5 原始备份 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                AddExtension = true,
                DefaultExt = "bin",
                FileName = $"NTA5332-user-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
                Title = "保存 NTAG5 用户区备份"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            BackupService.Save(dialog.FileName, data, _imageSource);
            AppendLog($"备份已保存：{dialog.FileName}");
            AppendLog($"元数据已保存：{dialog.FileName}.json");
            SetStatus("备份与校验元数据保存完成");
        });
    }

    private async Task WriteChangesAsync()
    {
        var target = GetWorkingSnapshot();
        _baselineImage = null;
        RefreshDifferenceDisplay();

        await RunOperationAsync("写入前正在读取当前芯片...", async cancellationToken =>
        {
            var readProgress = new Progress<int>(value => _progressBar.Value = value / 5);
            var current = await Task.Run(
                () => _device.ReadUserMemory(readProgress, cancellationToken),
                cancellationToken);
            var changedBlocks = Ntag5Memory.GetChangedBlocks(current, target);

            if (changedBlocks.Count == 0)
            {
                _baselineImage = (byte[])current.Clone();
                RefreshDifferenceDisplay();
                AppendLog("写入检查：芯片与编辑区完全一致，无需写入。");
                SetStatus("无需写入：芯片内容与编辑区一致");
                return;
            }

            var firstBlock = changedBlocks[0];
            var lastBlock = changedBlocks[^1];
            var confirmation = MessageBox.Show(
                this,
                $"将写入 {changedBlocks.Count} 个变化块（0x{firstBlock:X3} 至 0x{lastBlock:X3} 范围内）。\r\n\r\n" +
                "程序会先把当前芯片完整备份到“文档\\Ntag5Studio\\Backups”，然后逐块写入并回读校验。\r\n\r\n继续写入吗？",
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
                var blockData = target.AsSpan(
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

            _baselineImage = (byte[])target.Clone();
            RefreshDifferenceDisplay();
            AppendLog($"写入完成：{changedBlocks.Count} 个块全部回读一致。");
            SetStatus($"写入完成并通过校验：{changedBlocks.Count} 个变化块");
        });
    }

    private async Task VerifyAsync()
    {
        var expected = GetWorkingSnapshot();
        await RunOperationAsync("正在读取芯片并校验...", async cancellationToken =>
        {
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
            _decimalOutputBox.Text = HexCodec.ToDecimal(bytes);
            _base64OutputBox.Text = Convert.ToBase64String(bytes);
            SetStatus($"编码转换完成：{bytes.Length} 字节");
        });
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
        _openButton.Enabled = !_busy;
        _saveButton.Enabled = !_busy && _workingImage is not null;
        _writeButton.Enabled = !_busy && connected && _workingImage is not null;
        _verifyButton.Enabled = !_busy && connected && _workingImage is not null;
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
