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
    private readonly Button _readButton = new();
    private readonly Button _openButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _writeButton = new();
    private readonly Button _verifyButton = new();
    private readonly Button _cancelButton = new();
    private readonly ProgressBar _progressBar = new();
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
    private readonly TextBox _hexOutputBox = new();
    private readonly TextBox _utf8OutputBox = new();
    private readonly TextBox _decimalOutputBox = new();
    private readonly TextBox _base64OutputBox = new();
    private readonly Button _parseNdefButton = new();
    private readonly RichTextBox _ndefOutputBox = new();
    private readonly TextBox _insertOffsetBox = new();
    private readonly Button _insertBytesButton = new();
    private readonly RichTextBox _logBox = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolTip _toolTip = new();

    private void InitializeLayout()
    {
        SuspendLayout();
        Text = "Ntag5 Studio - NTA5332 用户区工具";
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
        _statusLabel.Text = "就绪 - 当前仅操作用户 EEPROM 0x0000-0x01FE";
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(_statusLabel);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildConnectionBar(), 0, 1);
        root.Controls.Add(BuildActionBar(), 0, 2);
        root.Controls.Add(BuildTabs(), 0, 3);

        Controls.Add(root);
        Controls.Add(statusStrip);
        ResumeLayout(true);
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
            Text = "Ntag5 Studio",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 7)
        };
        var subtitle = new Label
        {
            Text = "NTA5332 / NTAG 5 boost 用户 EEPROM 备份、编辑与校验",
            ForeColor = Color.FromArgb(207, 216, 220),
            AutoSize = true,
            Location = new Point(20, 39)
        };
        var scope = new Label
        {
            Text = "安全范围  511 块 / 2044 字节",
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
            Text = "驱动设备",
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

        ConfigureCommandButton(_readButton, "读取芯片", 96);
        ConfigureCommandButton(_openButton, "打开备份", 96);
        ConfigureCommandButton(_saveButton, "保存备份", 96);
        ConfigureCommandButton(_writeButton, "写入变化", 96, primary: true);
        ConfigureCommandButton(_verifyButton, "校验", 76);
        ConfigureCommandButton(_cancelButton, "取消", 70);
        _cancelButton.Enabled = false;

        bar.Controls.AddRange([_readButton, _openButton, _saveButton, _writeButton, _verifyButton]);
        _progressBar.Size = new Size(180, 22);
        _progressBar.Margin = new Padding(14, 3, 8, 0);
        _progressBar.Style = ProgressBarStyle.Continuous;
        bar.Controls.Add(_progressBar);
        bar.Controls.Add(_cancelButton);
        return bar;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 8, 10, 8),
            Padding = new Point(16, 6)
        };
        tabs.TabPages.Add(BuildMemoryTab());
        tabs.TabPages.Add(BuildConversionTab());
        tabs.TabPages.Add(BuildLogTab());
        return tabs;
    }

    private TabPage BuildMemoryTab()
    {
        var page = new TabPage("用户区") { BackColor = WindowBackground, Padding = new Padding(0) };
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
            Text = "仅访问 I²C 用户块 0x0000-0x01FE。配置区、密码、锁定位、原厂签名及 NFC 计数器均不会读取或写入。"
        };
        return label;
    }

    private TabPage BuildConversionTab()
    {
        var page = new TabPage("编码转换") { BackColor = WindowBackground, Padding = new Padding(10) };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1000, 600),
            SplitterDistance = 570,
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
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inputHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        inputHeader.Controls.Add(new Label { Text = "输入格式", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        _inputKindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _inputKindBox.Width = 150;
        _inputKindBox.Items.AddRange(["十六进制", "UTF-8 文本", "十进制字节", "Base64"]);
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

        var outputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        for (var i = 0; i < 4; i++)
        {
            outputs.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            outputs.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }
        AddOutput(outputs, 0, "十六进制", _hexOutputBox);
        AddOutput(outputs, 2, "UTF-8", _utf8OutputBox);
        AddOutput(outputs, 4, "十进制字节", _decimalOutputBox);
        AddOutput(outputs, 6, "Base64", _base64OutputBox);
        root.Controls.Add(outputs, 0, 3);
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

    private static void AddOutput(TableLayoutPanel table, int row, string title, TextBox box)
    {
        table.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(75, 75, 75), Margin = new Padding(0, 4, 0, 0) }, 0, row);
        box.Multiline = true;
        box.ReadOnly = true;
        box.Dock = DockStyle.Fill;
        box.ScrollBars = ScrollBars.Vertical;
        box.BackColor = Color.White;
        box.Font = new Font("Consolas", 9F);
        table.Controls.Add(box, 0, row + 1);
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
