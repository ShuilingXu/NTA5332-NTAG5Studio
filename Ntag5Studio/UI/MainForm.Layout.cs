using Ntag5Studio.Hardware;

namespace Ntag5Studio.UI;

public sealed partial class MainForm
{
    private static readonly Color WindowBackground = Color.FromArgb(244, 246, 247);
    private static readonly Color HeaderBackground = Color.FromArgb(38, 50, 56);
    private static readonly Color Accent = Color.FromArgb(0, 121, 107);
    private static readonly Color AccentDark = Color.FromArgb(0, 77, 64);
    private static readonly Color Warning = Color.FromArgb(255, 243, 205);
    private static readonly Color Border = Color.FromArgb(208, 214, 218);

    private readonly TextBox _devicePathBox = new();
    private readonly Button _connectButton = new();
    private readonly Label _connectionStateLabel = new();
    private readonly Label _runtimeStateLabel = new();
    private readonly Button _readButton = new();
    private readonly Button _runtimeStatusButton = new();
    private readonly Button _openButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveMfdButton = new();
    private readonly Button _writeButton = new();
    private readonly Button _verifyButton = new();
    private readonly Button _cancelButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly TableLayoutPanel _rootLayout = new();
    private readonly TabControl _mainTabs = new();
    private readonly DataGridView _memoryGrid = new();
    private readonly Label _sourceValueLabel = new();
    private readonly Label _hashValueLabel = new();
    private readonly Label _differenceValueLabel = new();
    private readonly TextBox _jumpBlockBox = new();
    private readonly Button _jumpButton = new();
    private readonly Label _selectedBlockLabel = new();
    private readonly TextBox _selectedBlockDetailsBox = new();
    private readonly ComboBox _inputKindBox = new();
    private readonly TextBox _conversionInputBox = new();
    private readonly Button _convertButton = new();
    private readonly Button _loadImageForConversionButton = new();
    private readonly RichTextBox _hexOutputBox = new();
    private readonly RichTextBox _utf8OutputBox = new();
    private readonly RichTextBox _asciiOutputBox = new();
    private readonly RichTextBox _utf16LeOutputBox = new();
    private readonly RichTextBox _utf16BeOutputBox = new();
    private readonly RichTextBox _gb18030OutputBox = new();
    private readonly RichTextBox _decimalOutputBox = new();
    private readonly RichTextBox _binaryOutputBox = new();
    private readonly RichTextBox _base64OutputBox = new();
    private readonly RichTextBox _urlPercentOutputBox = new();
    private readonly TabControl _outputTabs = new();
    private readonly Button _copyOutputButton = new();
    private readonly Label _outputInfoLabel = new();
    private readonly Button _parseNdefButton = new();
    private readonly RichTextBox _ndefOutputBox = new();
    private readonly TextBox _insertOffsetBox = new();
    private readonly Button _insertBytesButton = new();
    private readonly ComboBox _directInputKindBox = new();
    private readonly ComboBox _directAddressModeBox = new();
    private readonly TextBox _directAddressBox = new();
    private readonly TextBox _directInputBox = new();
    private readonly Button _directPreviewButton = new();
    private readonly Button _directWriteButton = new();
    private readonly Label _directWriteInfoLabel = new();
    private readonly RichTextBox _directPreviewBox = new();
    private readonly ComboBox _pcrPortBox = new();
    private readonly ComboBox _pcrBaudBox = new();
    private readonly Button _pcrRefreshPortsButton = new();
    private readonly Button _pcrDetectButton = new();
    private readonly Button _pcrInstallDriverButton = new();
    private readonly Label _pcrRuntimeLabel = new();
    private readonly Label _pcrCardLabel = new();
    private readonly TextBox _pcrKeyFileBox = new();
    private readonly Button _pcrBrowseKeyButton = new();
    private readonly CheckBox _pcrAutoBackupCheck = new();
    private readonly Button _pcrRecoveryReadButton = new();
    private readonly Button _pcrKnownReadButton = new();
    private readonly Button _pcrWriteButton = new();
    private readonly Button _pcrMagicWriteButton = new();
    private readonly TextBox _pcrUidBox = new();
    private readonly Button _pcrSetUidButton = new();
    private readonly Button _pcrFormatUidButton = new();
    private readonly Button _pcrLockUfuidButton = new();
    private readonly Button _pcrType2ReadButton = new();
    private readonly Button _pcrType2WriteButton = new();
    private readonly RichTextBox _pcrLogBox = new();
    private readonly RichTextBox _logBox = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolTip _toolTip = new();
    private Control? _ntagConnectionBar;
    private Control? _ntagActionBar;

    private void InitializeLayout()
    {
        SuspendLayout();
        Text = "NFC Studio - NTA5332 与 PCR532 工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 650);
        Size = new Size(1180, 800);
        BackColor = WindowBackground;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            SizingGrip = false,
            BackColor = Color.White
        };
        _statusLabel.Text = "就绪 - 连接后将自动检测 EEPROM / SRAM 映射";
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(_statusLabel);

        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.ColumnCount = 1;
        _rootLayout.RowCount = 5;
        _rootLayout.Margin = Padding.Empty;
        _rootLayout.Padding = Padding.Empty;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _ntagConnectionBar = BuildConnectionBar();
        _ntagActionBar = BuildActionBar();
        _rootLayout.Controls.Add(BuildMenu(), 0, 0);
        _rootLayout.Controls.Add(BuildHeader(), 0, 1);
        _rootLayout.Controls.Add(_ntagConnectionBar, 0, 2);
        _rootLayout.Controls.Add(_ntagActionBar, 0, 3);
        _rootLayout.Controls.Add(BuildTabs(), 0, 4);

        Controls.Add(_rootLayout);
        Controls.Add(statusStrip);
        ResumeLayout(true);
    }

    private Control BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8, 2, 0, 2)
        };

        var fileMenu = new ToolStripMenuItem("文件");
        var openItem = new ToolStripMenuItem("打开卡片文件...");
        openItem.Click += (_, _) => OpenBackup();
        var saveItem = new ToolStripMenuItem("保存当前文件...") { ShortcutKeyDisplayString = "Ctrl+S" };
        saveItem.Click += (_, _) => SaveBackup();
        var exportItem = new ToolStripMenuItem("导出 MFD...");
        exportItem.Click += (_, _) => SaveMfdDump();
        var pcrExportItem = new ToolStripMenuItem("导出到 PCR532 文件目录...");
        pcrExportItem.Click += (_, _) => ExportToPcr532();
        var pcrFolderItem = new ToolStripMenuItem("打开 PCR532 文件目录");
        pcrFolderItem.Click += (_, _) => OpenPcr532DumpFolder();
        var emulateExportItem = new ToolStripMenuItem("导出 PCR532 模拟标签文件（NDEF）...");
        emulateExportItem.Click += (_, _) => ExportPcr532Emulation();
        fileMenu.DropDownItems.AddRange([openItem, saveItem, exportItem, pcrExportItem, emulateExportItem, pcrFolderItem]);

        var deviceMenu = new ToolStripMenuItem("设备");
        var ntagDeviceItem = new ToolStripMenuItem("NTAG5 内置模块");
        ntagDeviceItem.Click += (_, _) => _mainTabs.SelectedIndex = 0;
        var pcrDeviceItem = new ToolStripMenuItem("PCR532 外接读卡器");
        pcrDeviceItem.Click += (_, _) => _mainTabs.SelectedIndex = 1;
        deviceMenu.DropDownItems.AddRange([ntagDeviceItem, pcrDeviceItem]);

        var workspaceMenu = new ToolStripMenuItem("工作区");
        AddWorkspaceMenuItem(workspaceMenu, "NTAG5 用户区", 0);
        AddWorkspaceMenuItem(workspaceMenu, "PCR532 / MIFARE", 1);
        AddWorkspaceMenuItem(workspaceMenu, "NTAG5 直接写入", 2);
        AddWorkspaceMenuItem(workspaceMenu, "编码与 NDEF", 3);
        AddWorkspaceMenuItem(workspaceMenu, "操作日志", 4);

        menu.Items.AddRange([fileMenu, deviceMenu, workspaceMenu]);
        MainMenuStrip = menu;
        return menu;
    }

    private void AddWorkspaceMenuItem(ToolStripMenuItem parent, string text, int tabIndex)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => _mainTabs.SelectedIndex = tabIndex;
        parent.DropDownItems.Add(item);
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = HeaderBackground,
            Padding = new Padding(18, 8, 18, 6),
            Margin = Padding.Empty
        };
        var title = new Label
        {
            Text = "NFC Studio",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 7)
        };
        var subtitle = new Label
        {
            Text = "NTA5332 内置模块与 PCR532 外接读卡器统一工作台",
            ForeColor = Color.FromArgb(207, 216, 220),
            AutoSize = true,
            Location = new Point(20, 39)
        };
        var scope = new Label
        {
            Text = "NTAG5 · MIFARE · Type 2",
            ForeColor = Color.White,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(scope);
        panel.Resize += (_, _) => scope.Location = new Point(
            Math.Max(20, panel.ClientSize.Width - scope.PreferredWidth - 20), 24);
        return panel;
    }

    private Control BuildConnectionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 9, 14, 6),
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        bar.Controls.Add(new Label
        {
            Text = "NTAG5 内置模块",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        });
        _devicePathBox.Text = SpbNtag5Device.DefaultDevicePath;
        _devicePathBox.Width = 260;
        _devicePathBox.Margin = new Padding(0, 2, 8, 0);
        _toolTip.SetToolTip(_devicePathBox, "供应商驱动二进制暴露的默认路径；仅在驱动被定制时修改");
        bar.Controls.Add(_devicePathBox);

        ConfigureCommandButton(_connectButton, "连接", 82, primary: true);
        bar.Controls.Add(_connectButton);
        _connectionStateLabel.Text = "未连接";
        _connectionStateLabel.ForeColor = Color.FromArgb(97, 97, 97);
        _connectionStateLabel.AutoSize = true;
        _connectionStateLabel.Margin = new Padding(8, 6, 0, 0);
        bar.Controls.Add(_connectionStateLabel);
        _runtimeStateLabel.Text = "存储：未检测";
        _runtimeStateLabel.ForeColor = Color.FromArgb(97, 97, 97);
        _runtimeStateLabel.AutoSize = true;
        _runtimeStateLabel.Margin = new Padding(18, 6, 0, 0);
        _toolTip.SetToolTip(_runtimeStateLabel, "连接后读取 CONFIG_1_REG，判断 SRAM 是否启用及用户区映射状态");
        bar.Controls.Add(_runtimeStateLabel);
        return bar;
    }

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(14, 7, 14, 5),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(250, 251, 251),
            BorderStyle = BorderStyle.FixedSingle
        };

        ConfigureCommandButton(_readButton, "读取芯片", 88);
        ConfigureCommandButton(_runtimeStatusButton, "运行状态", 86);
        ConfigureCommandButton(_openButton, "打开备份", 88);
        ConfigureCommandButton(_saveButton, "保存备份", 88);
        ConfigureCommandButton(_saveMfdButton, "导出MFD", 86);
        ConfigureCommandButton(_writeButton, "写入变化", 90, primary: true);
        ConfigureCommandButton(_verifyButton, "校验", 64);
        ConfigureCommandButton(_cancelButton, "取消", 64);
        _cancelButton.Enabled = false;

        bar.Controls.AddRange([_readButton, _runtimeStatusButton, _openButton, _saveButton, _saveMfdButton, _writeButton, _verifyButton]);
        _progressBar.Size = new Size(160, 22);
        _progressBar.Margin = new Padding(14, 3, 8, 0);
        _progressBar.Style = ProgressBarStyle.Continuous;
        bar.Controls.Add(_progressBar);
        bar.Controls.Add(_cancelButton);
        return bar;
    }

    private Control BuildTabs()
    {
        _mainTabs.Dock = DockStyle.Fill;
        _mainTabs.Margin = new Padding(10, 8, 10, 8);
        _mainTabs.Padding = new Point(16, 6);
        _mainTabs.TabPages.Add(BuildMemoryTab());
        _mainTabs.TabPages.Add(BuildPcr532Tab());
        _mainTabs.TabPages.Add(BuildDirectWriteTab());
        _mainTabs.TabPages.Add(BuildConversionTab());
        _mainTabs.TabPages.Add(BuildLogTab());
        _mainTabs.SelectedIndexChanged += (_, _) => UpdateWorkspaceChrome();
        return _mainTabs;
    }

    private void UpdateWorkspaceChrome()
    {
        var showNtagToolbar = _mainTabs.SelectedIndex != 1;
        if (_ntagConnectionBar is not null)
        {
            _ntagConnectionBar.Visible = showNtagToolbar;
        }

        if (_ntagActionBar is not null)
        {
            _ntagActionBar.Visible = showNtagToolbar;
        }

        if (_rootLayout.RowStyles.Count >= 4)
        {
            _rootLayout.RowStyles[2].Height = showNtagToolbar ? 50 : 0;
            _rootLayout.RowStyles[3].Height = showNtagToolbar ? 48 : 0;
        }
    }

    private TabPage BuildMemoryTab()
    {
        var page = new TabPage("NTAG5 用户区") { BackColor = WindowBackground, Padding = new Padding(0) };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1000, 600),
            SplitterDistance = 700,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 260,
            BackColor = Border
        };
        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = WindowBackground;
        split.Panel1.Controls.Add(BuildMemoryGrid());
        split.Panel2.Controls.Add(BuildMemorySidebar());
        page.Controls.Add(split);
        return page;
    }

    private Control BuildMemoryGrid()
    {
        _memoryGrid.Dock = DockStyle.Fill;
        _memoryGrid.BackgroundColor = Color.White;
        _memoryGrid.BorderStyle = BorderStyle.None;
        _memoryGrid.AllowUserToAddRows = false;
        _memoryGrid.AllowUserToDeleteRows = false;
        _memoryGrid.AllowUserToResizeRows = false;
        _memoryGrid.RowHeadersVisible = false;
        _memoryGrid.MultiSelect = true;
        _memoryGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _memoryGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _memoryGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _memoryGrid.RowTemplate.Height = 24;
        _memoryGrid.Font = new Font("Consolas", 9.5F);
        _memoryGrid.EnableHeadersVisualStyles = false;
        _memoryGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(236, 239, 241);
        _memoryGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(38, 50, 56);
        _memoryGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _memoryGrid.ColumnHeadersHeight = 32;
        _memoryGrid.GridColor = Color.FromArgb(230, 233, 235);
        _memoryGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(178, 223, 219);
        _memoryGrid.DefaultCellStyle.SelectionForeColor = Color.Black;

        AddGridColumn("块", 62, true);
        AddGridColumn("偏移", 72, true);
        AddGridColumn("B0", 58, false);
        AddGridColumn("B1", 58, false);
        AddGridColumn("B2", 58, false);
        AddGridColumn("B3", 58, false);
        var ascii = new DataGridViewTextBoxColumn
        {
            HeaderText = "ASCII",
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 100
        };
        _memoryGrid.Columns.Add(ascii);

        for (var block = 0; block < 0x1FF; block++)
        {
            _memoryGrid.Rows.Add($"0x{block:X3}", $"0x{block * 4:X4}", "--", "--", "--", "--", "....");
        }

        _memoryGrid.ReadOnly = true;
        return _memoryGrid;
    }

    private Control BuildMemorySidebar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            BackColor = WindowBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(BuildImageSummary(), 0, 0);
        panel.Controls.Add(BuildJumpPanel(), 0, 1);
        panel.Controls.Add(BuildSelectedBlockPanel(), 0, 2);
        panel.Controls.Add(BuildSafetyPanel(), 0, 3);
        return panel;
    }

    private Control BuildImageSummary()
    {
        var group = NewGroup("当前数据");
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSummaryRow(table, "来源", _sourceValueLabel, "尚未载入");
        AddSummaryRow(table, "SHA-256", _hashValueLabel, "-");
        AddSummaryRow(table, "变化块", _differenceValueLabel, "-");
        group.Controls.Add(table);
        return group;
    }

    private Control BuildJumpPanel()
    {
        var group = NewGroup("跳转到块");
        group.AutoSize = false;
        group.Height = 78;
        group.MinimumSize = new Size(0, 78);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 40, Padding = new Padding(5), WrapContents = false };
        _jumpBlockBox.Text = "000";
        _jumpBlockBox.Width = 100;
        _jumpBlockBox.Font = new Font("Consolas", 9.5F);
        _jumpBlockBox.Margin = new Padding(0, 2, 6, 0);
        ConfigureCommandButton(_jumpButton, "跳转", 68);
        flow.Controls.Add(_jumpBlockBox);
        flow.Controls.Add(_jumpButton);
        group.Controls.Add(flow);
        return group;
    }

    private Control BuildSelectedBlockPanel()
    {
        var group = NewGroup("所选块");
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(4) };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _selectedBlockLabel.Text = "未选择";
        _selectedBlockLabel.AutoSize = true;
        _selectedBlockLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _selectedBlockDetailsBox.Multiline = true;
        _selectedBlockDetailsBox.ReadOnly = true;
        _selectedBlockDetailsBox.Dock = DockStyle.Fill;
        _selectedBlockDetailsBox.BackColor = Color.White;
        _selectedBlockDetailsBox.Font = new Font("Consolas", 9F);
        _selectedBlockDetailsBox.ScrollBars = ScrollBars.Vertical;
        table.Controls.Add(_selectedBlockLabel, 0, 0);
        table.Controls.Add(_selectedBlockDetailsBox, 0, 1);
        group.Controls.Add(table);
        return group;
    }

    private static Control BuildSafetyPanel()
    {
        var label = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(270, 0),
            ForeColor = Color.FromArgb(85, 85, 85),
            Padding = new Padding(4, 8, 4, 2),
            Text = "仅写入 I²C 用户块 0x0000-0x01FE。运行状态查询只读会话寄存器；如启用 SRAM 镜像，0x0000-0x003F 为易失性 SRAM。"
        };
        return label;
    }

    private TabPage BuildPcr532Tab()
    {
        var page = new TabPage("PCR532 / MIFARE") { BackColor = WindowBackground, Padding = new Padding(10) };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildPcrConnectionPanel(), 0, 0);
        root.Controls.Add(BuildPcrOperationsPanel(), 0, 1);
        root.Controls.Add(BuildPcrLogPanel(), 0, 2);
        page.Controls.Add(root);
        return page;
    }

    private Control BuildPcrConnectionPanel()
    {
        var group = NewGroup("PCR532 设备");
        group.Margin = new Padding(0, 0, 0, 8);
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(4, 4, 4, 2)
        };
        flow.Controls.Add(new Label { Text = "串口", AutoSize = true, Margin = new Padding(0, 7, 7, 0) });
        _pcrPortBox.Width = 92;
        _pcrPortBox.DropDownStyle = ComboBoxStyle.DropDown;
        _pcrPortBox.Font = new Font("Consolas", 9.5F);
        _pcrPortBox.Margin = new Padding(0, 3, 8, 0);
        flow.Controls.Add(_pcrPortBox);

        flow.Controls.Add(new Label { Text = "速度", AutoSize = true, Margin = new Padding(2, 7, 7, 0) });
        _pcrBaudBox.Items.AddRange(["115200", "921600"]);
        _pcrBaudBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _pcrBaudBox.SelectedIndex = 0;
        _pcrBaudBox.Width = 98;
        _pcrBaudBox.Font = new Font("Consolas", 9.5F);
        _pcrBaudBox.Margin = new Padding(0, 3, 8, 0);
        flow.Controls.Add(_pcrBaudBox);

        ConfigureCommandButton(_pcrRefreshPortsButton, "刷新串口", 88);
        ConfigureCommandButton(_pcrDetectButton, "检测设备/卡片", 124, primary: true);
        ConfigureCommandButton(_pcrInstallDriverButton, "安装CH341驱动", 122);
        flow.Controls.Add(_pcrRefreshPortsButton);
        flow.Controls.Add(_pcrDetectButton);
        flow.Controls.Add(_pcrInstallDriverButton);

        _pcrRuntimeLabel.Text = "运行组件：检测中";
        _toolTip.SetToolTip(_pcrRuntimeLabel, _pcr532.RuntimeDirectory);
        _pcrRuntimeLabel.AutoSize = true;
        _pcrRuntimeLabel.ForeColor = Color.FromArgb(90, 90, 90);
        _pcrRuntimeLabel.Margin = new Padding(8, 7, 0, 0);
        flow.Controls.Add(_pcrRuntimeLabel);
        group.Controls.Add(flow);
        return group;
    }

    private Control BuildPcrOperationsPanel()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        table.Controls.Add(BuildPcrClassicGroup(), 0, 0);
        table.Controls.Add(BuildPcrUidGroup(), 1, 0);
        table.Controls.Add(BuildPcrType2Group(), 2, 0);
        return table;
    }

    private Control BuildPcrClassicGroup()
    {
        var group = NewGroup("MIFARE Classic");
        group.Margin = new Padding(0, 0, 8, 8);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var keyRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        keyRow.Controls.Add(new Label { Text = "密钥文件", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        _pcrKeyFileBox.ReadOnly = true;
        _pcrKeyFileBox.PlaceholderText = "可选 .mfd/.dump";
        _pcrKeyFileBox.Width = 190;
        _pcrKeyFileBox.Margin = new Padding(0, 3, 6, 0);
        ConfigureCommandButton(_pcrBrowseKeyButton, "选择", 62);
        keyRow.Controls.Add(_pcrKeyFileBox);
        keyRow.Controls.Add(_pcrBrowseKeyButton);
        root.Controls.Add(keyRow, 0, 0);

        _pcrAutoBackupCheck.Text = "写入、改 UID 和格式化前自动备份";
        _pcrAutoBackupCheck.Checked = true;
        _pcrAutoBackupCheck.AutoSize = true;
        _pcrAutoBackupCheck.Margin = new Padding(0, 4, 0, 0);
        root.Controls.Add(_pcrAutoBackupCheck, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 5, 0, 0) };
        ConfigureCommandButton(_pcrRecoveryReadButton, "恢复密钥并备份", 142, primary: true);
        ConfigureCommandButton(_pcrKnownReadButton, "按现有密钥读取", 142);
        ConfigureCommandButton(_pcrWriteButton, "写入普通卡", 142);
        ConfigureCommandButton(_pcrMagicWriteButton, "写入 UID/CUID 卡", 142);
        buttons.Controls.AddRange([_pcrRecoveryReadButton, _pcrKnownReadButton, _pcrWriteButton, _pcrMagicWriteButton]);
        root.Controls.Add(buttons, 0, 2);
        group.Controls.Add(root);
        return group;
    }

    private Control BuildPcrUidGroup()
    {
        var group = NewGroup("UID / UFUID 魔术卡");
        group.Margin = new Padding(0, 0, 8, 8);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var uidRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        uidRow.Controls.Add(new Label { Text = "UID", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        _pcrUidBox.Text = "11223344";
        _pcrUidBox.Width = 100;
        _pcrUidBox.MaxLength = 11;
        _pcrUidBox.CharacterCasing = CharacterCasing.Upper;
        _pcrUidBox.Font = new Font("Consolas", 10F);
        _pcrUidBox.Margin = new Padding(0, 3, 6, 0);
        ConfigureCommandButton(_pcrSetUidButton, "设置 UID", 88);
        uidRow.Controls.Add(_pcrUidBox);
        uidRow.Controls.Add(_pcrSetUidButton);
        root.Controls.Add(uidRow, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 5, 0, 0) };
        ConfigureCommandButton(_pcrFormatUidButton, "格式化 UID 卡", 122);
        ConfigureCommandButton(_pcrLockUfuidButton, "锁定 UFUID", 122);
        _pcrLockUfuidButton.BackColor = Color.FromArgb(255, 245, 245);
        _pcrLockUfuidButton.FlatAppearance.BorderColor = Color.FromArgb(198, 40, 40);
        _pcrLockUfuidButton.ForeColor = Color.FromArgb(160, 25, 25);
        actions.Controls.AddRange([_pcrFormatUidButton, _pcrLockUfuidButton]);
        root.Controls.Add(actions, 0, 1);

        _pcrCardLabel.Text = "卡片：尚未检测";
        _pcrCardLabel.Dock = DockStyle.Fill;
        _pcrCardLabel.AutoEllipsis = true;
        _pcrCardLabel.ForeColor = Color.FromArgb(75, 75, 75);
        _pcrCardLabel.Padding = new Padding(0, 8, 0, 0);
        root.Controls.Add(_pcrCardLabel, 0, 2);
        group.Controls.Add(root);
        return group;
    }

    private Control BuildPcrType2Group()
    {
        var group = NewGroup("Ultralight / NTAG (Type 2)");
        group.Margin = new Padding(0, 0, 0, 8);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4, 8, 4, 4)
        };
        ConfigureCommandButton(_pcrType2ReadButton, "读取原始备份", 144, primary: true);
        ConfigureCommandButton(_pcrType2WriteButton, "恢复原始备份", 144);
        buttons.Controls.AddRange([_pcrType2ReadButton, _pcrType2WriteButton]);
        var type2Note = new Label { Text = "读取后可编辑并原样保存。\r\nNTAG5：仅离线文件互通。", AutoSize = true, MaximumSize = new Size(200, 0), Margin = new Padding(0, 8, 0, 0) };
        _toolTip.SetToolTip(type2Note, Pcr532Service.RadioCompatibilityNote);
        buttons.Controls.Add(type2Note);
        group.Controls.Add(buttons);
        return group;
    }

    private Control BuildPcrLogPanel()
    {
        var group = NewGroup("PCR532 输出");
        group.AutoSize = false;
        group.Margin = Padding.Empty;
        _pcrLogBox.Dock = DockStyle.Fill;
        _pcrLogBox.ReadOnly = true;
        _pcrLogBox.BackColor = Color.FromArgb(250, 250, 250);
        _pcrLogBox.BorderStyle = BorderStyle.FixedSingle;
        _pcrLogBox.Font = new Font("Consolas", 9.5F);
        _pcrLogBox.WordWrap = false;
        group.Controls.Add(_pcrLogBox);
        return group;
    }

    private TabPage BuildConversionTab()
    {
        var page = new TabPage("编码与 NDEF") { BackColor = WindowBackground, Padding = new Padding(10) };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1000, 600),
            SplitterDistance = 620,
            SplitterWidth = 8,
            BackColor = Border
        };
        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = Color.White;
        split.Panel1.Controls.Add(BuildConverterPanel());
        split.Panel2.Controls.Add(BuildNdefPanel());
        page.Controls.Add(split);
        return page;
    }

    private Control BuildConverterPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inputHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        inputHeader.Controls.Add(new Label { Text = "输入格式", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        _inputKindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _inputKindBox.Width = 190;
        _inputKindBox.DropDownWidth = 220;
        _inputKindBox.Items.AddRange([
            "十六进制",
            "UTF-8 文本",
            "ASCII 文本",
            "UTF-16 LE 文本",
            "UTF-16 BE 文本",
            "GB18030 文本",
            "十进制字节",
            "二进制位串",
            "Base64",
            "URL 百分号编码"
        ]);
        _inputKindBox.SelectedIndex = 0;
        inputHeader.Controls.Add(_inputKindBox);
        root.Controls.Add(inputHeader, 0, 0);

        _conversionInputBox.Multiline = true;
        _conversionInputBox.Dock = DockStyle.Fill;
        _conversionInputBox.ScrollBars = ScrollBars.Vertical;
        _conversionInputBox.AcceptsReturn = true;
        _conversionInputBox.Font = new Font("Consolas", 10F);
        _conversionInputBox.PlaceholderText = "例如：E1 40 80 09 或输入普通文本";
        root.Controls.Add(_conversionInputBox, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 3) };
        ConfigureCommandButton(_convertButton, "转换", 86, primary: true);
        ConfigureCommandButton(_loadImageForConversionButton, "载入当前用户区", 130);
        buttons.Controls.Add(_convertButton);
        buttons.Controls.Add(_loadImageForConversionButton);
        root.Controls.Add(buttons, 0, 2);

        var outputTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        ConfigureCommandButton(_copyOutputButton, "复制当前结果", 112);
        _outputInfoLabel.Text = "转换后可切换下方结果标签";
        _outputInfoLabel.AutoSize = true;
        _outputInfoLabel.ForeColor = Color.FromArgb(90, 90, 90);
        _outputInfoLabel.Margin = new Padding(8, 7, 0, 0);
        outputTools.Controls.Add(_copyOutputButton);
        outputTools.Controls.Add(_outputInfoLabel);
        root.Controls.Add(outputTools, 0, 3);

        _outputTabs.Dock = DockStyle.Fill;
        _outputTabs.Padding = new Point(12, 6);
        _outputTabs.Appearance = TabAppearance.Normal;
        _outputTabs.Font = new Font("Microsoft YaHei UI", 8.5F);
        _outputTabs.TabPages.Clear();
        AddOutputTab("十六进制", _hexOutputBox, wrap: false);
        AddOutputTab("UTF-8", _utf8OutputBox, wrap: true);
        AddOutputTab("ASCII", _asciiOutputBox, wrap: true);
        AddOutputTab("UTF-16 LE", _utf16LeOutputBox, wrap: true);
        AddOutputTab("UTF-16 BE", _utf16BeOutputBox, wrap: true);
        AddOutputTab("GB18030", _gb18030OutputBox, wrap: true);
        AddOutputTab("十进制", _decimalOutputBox, wrap: false);
        AddOutputTab("二进制", _binaryOutputBox, wrap: false);
        AddOutputTab("Base64", _base64OutputBox, wrap: true);
        AddOutputTab("URL 百分号", _urlPercentOutputBox, wrap: false);
        root.Controls.Add(_outputTabs, 0, 4);
        return root;
    }

    private Control BuildNdefPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ConfigureCommandButton(_parseNdefButton, "解析当前 Type 5 / NDEF", 180, primary: true);
        header.Controls.Add(_parseNdefButton);
        root.Controls.Add(header, 0, 0);

        _ndefOutputBox.Dock = DockStyle.Fill;
        _ndefOutputBox.ReadOnly = true;
        _ndefOutputBox.BorderStyle = BorderStyle.FixedSingle;
        _ndefOutputBox.BackColor = Color.White;
        _ndefOutputBox.Font = new Font("Consolas", 9.5F);
        root.Controls.Add(_ndefOutputBox, 0, 1);

        var insert = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        insert.Controls.Add(new Label { Text = "插入到编辑区偏移 (Hex)", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        _insertOffsetBox.Text = "0000";
        _insertOffsetBox.Width = 75;
        _insertOffsetBox.Font = new Font("Consolas", 9.5F);
        _insertOffsetBox.Margin = new Padding(0, 3, 8, 0);
        ConfigureCommandButton(_insertBytesButton, "插入", 72);
        insert.Controls.Add(_insertOffsetBox);
        insert.Controls.Add(_insertBytesButton);
        root.Controls.Add(insert, 0, 2);
        return root;
    }

    private TabPage BuildDirectWriteTab()
    {
        var page = new TabPage("NTAG5 直接写入") { BackColor = WindowBackground, Padding = new Padding(10) };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1000, 600),
            SplitterDistance = 620,
            SplitterWidth = 8,
            BackColor = Border
        };
        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = Color.White;
        split.Panel1.Controls.Add(BuildDirectWriteInputPanel());
        split.Panel2.Controls.Add(BuildDirectWritePreviewPanel());
        page.Controls.Add(split);
        return page;
    }

    private Control BuildDirectWriteInputPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        header.Controls.Add(new Label { Text = "输入格式", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        _directInputKindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _directInputKindBox.Width = 170;
        _directInputKindBox.DropDownWidth = 220;
        _directInputKindBox.Items.AddRange([
            "十六进制",
            "UTF-8 文本",
            "ASCII 文本",
            "UTF-16 LE 文本",
            "UTF-16 BE 文本",
            "GB18030 文本",
            "十进制字节",
            "二进制位串",
            "Base64",
            "URL 百分号编码"
        ]);
        _directInputKindBox.SelectedIndex = 1;
        header.Controls.Add(_directInputKindBox);

        header.Controls.Add(new Label { Text = "地址类型", AutoSize = true, Margin = new Padding(12, 7, 8, 0) });
        _directAddressModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _directAddressModeBox.Width = 130;
        _directAddressModeBox.Items.AddRange(["偏移 Hex", "块地址 Hex"]);
        _directAddressModeBox.SelectedIndex = 0;
        header.Controls.Add(_directAddressModeBox);

        header.Controls.Add(new Label { Text = "位置", AutoSize = true, Margin = new Padding(12, 7, 8, 0) });
        _directAddressBox.Text = "0000";
        _directAddressBox.Width = 80;
        _directAddressBox.Font = new Font("Consolas", 9.5F);
        _directAddressBox.Margin = new Padding(0, 3, 0, 0);
        header.Controls.Add(_directAddressBox);
        root.Controls.Add(header, 0, 0);

        _directInputBox.Multiline = true;
        _directInputBox.Dock = DockStyle.Fill;
        _directInputBox.ScrollBars = ScrollBars.Both;
        _directInputBox.AcceptsReturn = true;
        _directInputBox.AcceptsTab = true;
        _directInputBox.Font = new Font("Consolas", 10F);
        _directInputBox.PlaceholderText = "直接输入字符串、十六进制字节、Base64 等内容。写入时会先读取当前 TAG，只覆盖目标范围。";
        root.Controls.Add(_directInputBox, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 8, 0, 4) };
        ConfigureCommandButton(_directPreviewButton, "生成预览", 92);
        ConfigureCommandButton(_directWriteButton, "直接写入 TAG", 124, primary: true);
        buttons.Controls.Add(_directPreviewButton);
        buttons.Controls.Add(_directWriteButton);
        root.Controls.Add(buttons, 0, 2);

        _directWriteInfoLabel.Text = "默认 UTF-8 文本；偏移/块地址均按十六进制输入。";
        _directWriteInfoLabel.AutoSize = true;
        _directWriteInfoLabel.ForeColor = Color.FromArgb(90, 90, 90);
        _directWriteInfoLabel.Margin = new Padding(0, 5, 0, 0);
        root.Controls.Add(_directWriteInfoLabel, 0, 3);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(85, 85, 85),
            Text = "兼容 PCR532/libnfc 的原始 dump 思路：按 TAG 用户区线性地址写入。程序仍会自动备份、只写受影响的 4 字节块，并逐块回读校验。",
            Padding = new Padding(0, 8, 0, 0)
        };
        root.Controls.Add(note, 0, 4);
        return root;
    }

    private Control BuildDirectWritePreviewPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "写入预览",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 65, 70)
        }, 0, 0);

        _directPreviewBox.Dock = DockStyle.Fill;
        _directPreviewBox.ReadOnly = true;
        _directPreviewBox.BorderStyle = BorderStyle.FixedSingle;
        _directPreviewBox.BackColor = Color.White;
        _directPreviewBox.Font = new Font("Consolas", 9.5F);
        _directPreviewBox.Text = "输入内容后点击“生成预览”。";
        root.Controls.Add(_directPreviewBox, 0, 1);
        return root;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("操作日志") { BackColor = Color.White, Padding = new Padding(10) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var clear = new Button();
        ConfigureCommandButton(clear, "清空日志", 90);
        clear.Click += (_, _) => _logBox.Clear();
        header.Controls.Add(clear);
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = Color.FromArgb(250, 250, 250);
        _logBox.Font = new Font("Consolas", 9.5F);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_logBox, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private void AddGridColumn(string title, int width, bool readOnly)
    {
        _memoryGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private static GroupBox NewGroup(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = true,
        Padding = new Padding(8),
        Margin = new Padding(0, 0, 0, 10),
        ForeColor = Color.FromArgb(55, 65, 70),
        BackColor = Color.White
    };

    private static void AddSummaryRow(TableLayoutPanel table, string title, Label value, string initialText)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(100, 100, 100), Margin = new Padding(0, 4, 5, 4) }, 0, row);
        value.Text = initialText;
        value.AutoSize = true;
        value.MaximumSize = new Size(190, 0);
        value.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(value, 1, row);
    }

    private void AddOutputTab(string title, RichTextBox box, bool wrap)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(6) };
        box.ReadOnly = true;
        box.Dock = DockStyle.Fill;
        box.ScrollBars = wrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
        box.WordWrap = wrap;
        box.DetectUrls = false;
        box.BackColor = Color.White;
        box.Font = new Font(wrap ? "Microsoft YaHei UI" : "Consolas", 10F);
        page.Controls.Add(box);
        _outputTabs.TabPages.Add(page);
    }

    private static void ConfigureCommandButton(Button button, string text, int width, bool primary = false)
    {
        button.Text = text;
        button.Size = new Size(width, 30);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? AccentDark : Border;
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(45, 55, 60);
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }
}
