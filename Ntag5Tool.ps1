param([switch]$SelfTest)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$bridgeSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class Ntag5Bridge
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint IoctlOpen = 0x04000400;
    private const uint IoctlClose = 0x04000404;
    private const uint IoctlWriteRead = 0x04000410;
    private static readonly IntPtr InvalidHandle = new IntPtr(-1);
    private static IntPtr handle = IntPtr.Zero;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr objectHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr device, uint controlCode,
        byte[] inputBuffer, int inputLength,
        byte[] outputBuffer, int outputLength,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr file, byte[] buffer, int bytesToWrite,
        out int bytesWritten, IntPtr overlapped);

    public static bool IsConnected
    {
        get { return handle != IntPtr.Zero && handle != InvalidHandle; }
    }

    private static void ThrowLastError(string operation)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }

    public static void Connect(string devicePath)
    {
        Disconnect();
        handle = CreateFile(devicePath, GenericRead | GenericWrite, 0,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle == InvalidHandle)
        {
            handle = IntPtr.Zero;
            ThrowLastError("Unable to open the NTAG5 driver device");
        }

        int returned;
        if (!DeviceIoControl(handle, IoctlOpen, null, 0, null, 0,
            out returned, IntPtr.Zero))
        {
            Exception error = new Win32Exception(
                Marshal.GetLastWin32Error(), "Unable to open the SPB controller");
            CloseHandle(handle);
            handle = IntPtr.Zero;
            throw error;
        }
    }

    public static void Disconnect()
    {
        if (!IsConnected) return;
        int returned;
        DeviceIoControl(handle, IoctlClose, null, 0, null, 0,
            out returned, IntPtr.Zero);
        CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    public static byte[] ReadMemory(int blockAddress, int byteCount)
    {
        if (!IsConnected) throw new InvalidOperationException("Driver is not connected");
        if (blockAddress < 0 || blockAddress > 0x1FE ||
            byteCount < 1 || byteCount > 255)
            throw new ArgumentOutOfRangeException();

        byte[] address = new byte[] {
            (byte)((blockAddress >> 8) & 0xFF),
            (byte)(blockAddress & 0xFF)
        };
        byte[] result = new byte[byteCount];
        int returned;
        if (!DeviceIoControl(handle, IoctlWriteRead, address, address.Length,
            result, result.Length, out returned, IntPtr.Zero))
            ThrowLastError("NTAG5 memory read failed");
        if (returned != byteCount)
            throw new InvalidOperationException(
                "Unexpected NTAG5 read length: " + returned + " / " + byteCount);
        return result;
    }

    public static void WriteBlock(int blockAddress, byte[] data)
    {
        if (!IsConnected) throw new InvalidOperationException("Driver is not connected");
        if (blockAddress < 0 || blockAddress > 0x1FE)
            throw new ArgumentOutOfRangeException();
        if (data == null || data.Length != 4)
            throw new ArgumentException("EEPROM writes require exactly four bytes");

        byte[] request = new byte[6];
        request[0] = (byte)((blockAddress >> 8) & 0xFF);
        request[1] = (byte)(blockAddress & 0xFF);
        Array.Copy(data, 0, request, 2, 4);
        int written;
        if (!WriteFile(handle, request, request.Length, out written, IntPtr.Zero))
            ThrowLastError("NTAG5 memory write failed");
        if (written != request.Length)
            throw new InvalidOperationException(
                "Unexpected NTAG5 write length: " + written + " / " + request.Length);
    }
}
'@

Add-Type -TypeDefinition $bridgeSource -Language CSharp

$script:UserBytes = 2044
$script:LastRead = $null
$script:Image = $null

function Convert-HexByte([object]$value) {
    $text = [string]$value
    if ($text -notmatch '^[0-9A-Fa-f]{2}$') {
        throw "Invalid hex byte: $text"
    }
    return [Convert]::ToByte($text, 16)
}

function Read-UserMemory {
    $data = New-Object byte[] $script:UserBytes
    $block = 0
    while ($block -le 0x1FE) {
        $blocks = [Math]::Min(16, 0x1FF - $block)
        $chunk = [Ntag5Bridge]::ReadMemory($block, $blocks * 4)
        [Array]::Copy($chunk, 0, $data, $block * 4, $chunk.Length)
        $block += $blocks
    }
    return $data
}

function Get-GridImage {
    $data = New-Object byte[] $script:UserBytes
    for ($row = 0; $row -lt 0x1FF; $row++) {
        for ($column = 0; $column -lt 4; $column++) {
            $data[$row * 4 + $column] = Convert-HexByte $grid.Rows[$row].Cells[$column + 2].Value
        }
    }
    return $data
}

function Set-GridImage([byte[]]$data) {
    if ($null -eq $data -or $data.Length -ne $script:UserBytes) {
        throw 'Backup must contain exactly 2044 bytes'
    }
    for ($row = 0; $row -lt 0x1FF; $row++) {
        $ascii = New-Object System.Text.StringBuilder
        for ($column = 0; $column -lt 4; $column++) {
            $value = $data[$row * 4 + $column]
            $grid.Rows[$row].Cells[$column + 2].Value = ('{0:X2}' -f $value)
            [void]$ascii.Append($(if ($value -ge 32 -and $value -le 126) { [char]$value } else { '.' }))
        }
        $grid.Rows[$row].Cells[6].Value = $ascii.ToString()
    }
    $script:Image = [byte[]]$data.Clone()
    $grid.Invalidate()
}

function Set-Status([string]$message) {
    $status.Text = $message
    $form.Refresh()
}

function Save-Bytes([byte[]]$data, [string]$path) {
    [IO.File]::WriteAllBytes($path, $data)
}

$form = New-Object Windows.Forms.Form
$form.Text = 'NTA5332 NTAG5 User Memory Tool'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object Drawing.Size(900, 700)
$form.MinimumSize = New-Object Drawing.Size(760, 500)

$top = New-Object Windows.Forms.Panel
$top.Dock = 'Top'
$top.Height = 92
$form.Controls.Add($top)

$pathLabel = New-Object Windows.Forms.Label
$pathLabel.Text = 'Driver path'
$pathLabel.AutoSize = $true
$pathLabel.Location = New-Object Drawing.Point(12, 14)
$top.Controls.Add($pathLabel)

$pathBox = New-Object Windows.Forms.TextBox
$pathBox.Text = '\\.\SPBNFC01'
$pathBox.Location = New-Object Drawing.Point(105, 10)
$pathBox.Size = New-Object Drawing.Size(210, 24)
$top.Controls.Add($pathBox)

$connect = New-Object Windows.Forms.Button
$connect.Text = 'Connect'
$connect.Location = New-Object Drawing.Point(330, 9)
$connect.Size = New-Object Drawing.Size(80, 26)
$top.Controls.Add($connect)

$read = New-Object Windows.Forms.Button
$read.Text = 'Read NTAG5'
$read.Location = New-Object Drawing.Point(420, 9)
$read.Size = New-Object Drawing.Size(100, 26)
$read.Enabled = $false
$top.Controls.Add($read)

$save = New-Object Windows.Forms.Button
$save.Text = 'Save backup'
$save.Location = New-Object Drawing.Point(530, 9)
$save.Size = New-Object Drawing.Size(90, 26)
$save.Enabled = $false
$top.Controls.Add($save)

$open = New-Object Windows.Forms.Button
$open.Text = 'Open backup'
$open.Location = New-Object Drawing.Point(630, 9)
$open.Size = New-Object Drawing.Size(90, 26)
$top.Controls.Add($open)

$write = New-Object Windows.Forms.Button
$write.Text = 'Write changes'
$write.Location = New-Object Drawing.Point(730, 9)
$write.Size = New-Object Drawing.Size(90, 26)
$write.Enabled = $false
$top.Controls.Add($write)

$help = New-Object Windows.Forms.Label
$help.Text = 'Double-click Byte0-Byte3 to edit. Only user EEPROM 0x0000-0x01FE is accessed.'
$help.AutoSize = $true
$help.Location = New-Object Drawing.Point(12, 52)
$top.Controls.Add($help)

$grid = New-Object Windows.Forms.DataGridView
$grid.Dock = 'Fill'
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.AllowUserToResizeRows = $false
$grid.MultiSelect = $false
$grid.RowHeadersVisible = $false
$grid.SelectionMode = 'CellSelect'
$grid.AutoSizeRowsMode = 'None'
$grid.EditMode = 'EditOnKeystrokeOrF2'
$grid.Font = New-Object Drawing.Font('Consolas', 9)
$form.Controls.Add($grid)

foreach ($definition in @(
    @('Block', 55, $true), @('Offset', 65, $true),
    @('Byte0', 58, $false), @('Byte1', 58, $false),
    @('Byte2', 58, $false), @('Byte3', 58, $false),
    @('ASCII', 130, $true)
)) {
    $column = New-Object Windows.Forms.DataGridViewTextBoxColumn
    $column.HeaderText = $definition[0]
    $column.Width = $definition[1]
    $column.ReadOnly = $definition[2]
    $column.SortMode = 'NotSortable'
    [void]$grid.Columns.Add($column)
}

for ($block = 0; $block -lt 0x1FF; $block++) {
    $row = $grid.Rows.Add()
    $grid.Rows[$row].Cells[0].Value = '{0:X3}' -f $block
    $grid.Rows[$row].Cells[1].Value = '{0:X4}' -f ($block * 4)
    for ($column = 0; $column -lt 4; $column++) {
        $grid.Rows[$row].Cells[$column + 2].Value = '00'
    }
    $grid.Rows[$row].Cells[6].Value = '....'
}

$status = New-Object Windows.Forms.StatusBar
$status.Text = 'Disconnected'
$form.Controls.Add($status)

$grid.add_CellValidating({
    param($sender, $event)
    if ($event.ColumnIndex -ge 2 -and $event.ColumnIndex -le 5) {
        try {
            $byte = Convert-HexByte $event.FormattedValue
            $grid.Rows[$event.RowIndex].Cells[$event.ColumnIndex].Value = '{0:X2}' -f $byte
        }
        catch {
            $event.Cancel = $true
            [Windows.Forms.MessageBox]::Show($form,
                'Enter two hex digits, for example 0A.', 'Invalid input',
                'OK', 'Warning') | Out-Null
        }
    }
})

$grid.add_CellDoubleClick({
    param($sender, $event)
    if ($event.ColumnIndex -ge 2 -and $event.ColumnIndex -le 5) {
        $grid.BeginEdit($true)
    }
})

$grid.add_CellEndEdit({
    param($sender, $event)
    if ($event.ColumnIndex -ge 2 -and $event.ColumnIndex -le 5) {
        $ascii = New-Object System.Text.StringBuilder
        for ($column = 2; $column -le 5; $column++) {
            $value = Convert-HexByte $grid.Rows[$event.RowIndex].Cells[$column].Value
            [void]$ascii.Append($(if ($value -ge 32 -and $value -le 126) { [char]$value } else { '.' }))
        }
        $grid.Rows[$event.RowIndex].Cells[6].Value = $ascii.ToString()
        $grid.InvalidateRow($event.RowIndex)
    }
})

$grid.add_CellFormatting({
    param($sender, $event)
    if ($event.RowIndex -lt 0 -or $event.ColumnIndex -lt 2 -or
        $event.ColumnIndex -gt 5 -or $null -eq $script:LastRead) { return }
    try {
        $newValue = Convert-HexByte $grid.Rows[$event.RowIndex].Cells[$event.ColumnIndex].Value
        $oldValue = $script:LastRead[$event.RowIndex * 4 + $event.ColumnIndex - 2]
        if ($newValue -ne $oldValue) {
            $event.CellStyle.BackColor = [Drawing.Color]::LightGoldenrodYellow
        }
    }
    catch { }
})

$connect.Add_Click({
    try {
        if ([Ntag5Bridge]::IsConnected) {
            [Ntag5Bridge]::Disconnect()
            $connect.Text = 'Connect'
            $read.Enabled = $false
            $write.Enabled = $false
            Set-Status 'Disconnected'
        }
        else {
            [Ntag5Bridge]::Connect($pathBox.Text.Trim())
            $connect.Text = 'Disconnect'
            $read.Enabled = $true
            Set-Status 'Connected; default NTAG5 I2C address is 0x54'
        }
    }
    catch {
        [Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
            'Connection failed', 'OK', 'Error') | Out-Null
        Set-Status 'Connection failed'
    }
})

$read.Add_Click({
    try {
        $read.Enabled = $false
        $write.Enabled = $false
        Set-Status 'Reading 2044 bytes of user EEPROM...'
        $bytes = Read-UserMemory
        $script:LastRead = [byte[]]$bytes.Clone()
        Set-GridImage $bytes
        $save.Enabled = $true
        $write.Enabled = $true
        Set-Status 'Read complete; edit Byte0-Byte3, then write with confirmation'
    }
    catch {
        [Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
            'Read failed', 'OK', 'Error') | Out-Null
        Set-Status 'Read failed'
    }
    finally {
        $read.Enabled = [Ntag5Bridge]::IsConnected
    }
})

$save.Add_Click({
    try {
        $bytes = Get-GridImage
        $dialog = New-Object Windows.Forms.SaveFileDialog
        $dialog.Filter = 'NTAG5 user backup (*.bin)|*.bin|All files (*.*)|*.*'
        $dialog.FileName = 'NTA5332-user-2044-bytes.bin'
        if ($dialog.ShowDialog($form) -eq 'OK') {
            Save-Bytes $bytes $dialog.FileName
            Set-Status "Backup saved: $($dialog.FileName)"
        }
    }
    catch {
        [Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
            'Save failed', 'OK', 'Error') | Out-Null
    }
})

$open.Add_Click({
    try {
        $dialog = New-Object Windows.Forms.OpenFileDialog
        $dialog.Filter = 'NTAG5 user backup (*.bin)|*.bin|All files (*.*)|*.*'
        if ($dialog.ShowDialog($form) -eq 'OK') {
            $bytes = [IO.File]::ReadAllBytes($dialog.FileName)
            Set-GridImage $bytes
            $save.Enabled = $true
            $write.Enabled = [Ntag5Bridge]::IsConnected -and $null -ne $script:LastRead
            Set-Status "Backup opened: $($dialog.FileName); read current chip before writing"
        }
    }
    catch {
        [Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
            'Open failed', 'OK', 'Error') | Out-Null
    }
})

$write.Add_Click({
    try {
        if ($null -eq $script:LastRead) {
            throw 'Read the current NTAG5 first to establish a hardware backup.'
        }
        $bytes = Get-GridImage
        $changed = New-Object System.Collections.Generic.List[int]
        for ($block = 0; $block -le 0x1FE; $block++) {
            $different = $false
            for ($column = 0; $column -lt 4; $column++) {
                if ($bytes[$block * 4 + $column] -ne
                    $script:LastRead[$block * 4 + $column]) { $different = $true }
            }
            if ($different) { [void]$changed.Add($block) }
        }
        if ($changed.Count -eq 0) {
            Set-Status 'No changes detected'
            return
        }

        $confirm = [Windows.Forms.MessageBox]::Show(
            $form,
            "Write $($changed.Count) four-byte blocks to EEPROM? This changes the chip.",
            'Confirm write', 'YesNo', 'Warning')
        if ($confirm -ne 'Yes') { return }

        $backupPath = Join-Path ([Environment]::GetFolderPath('Desktop')) `
            ('NTA5332-before-write-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bin')
        Save-Bytes ([byte[]]$script:LastRead.Clone()) $backupPath
        $write.Enabled = $false
        Set-Status "Writing; pre-write backup saved to desktop: $backupPath"

        foreach ($block in $changed) {
            $blockData = New-Object byte[] 4
            [Array]::Copy($bytes, $block * 4, $blockData, 0, 4)
            [Ntag5Bridge]::WriteBlock($block, $blockData)
            Start-Sleep -Milliseconds 10
            $verify = [Ntag5Bridge]::ReadMemory($block, 4)
            for ($column = 0; $column -lt 4; $column++) {
                if ($verify[$column] -ne $blockData[$column]) {
                    throw ('Write verification failed for block 0x{0:X3}' -f $block)
                }
            }
            Set-Status ('Writing and verifying block 0x{0:X3}; remaining {1}' -f
                $block, ($changed.Count - $changed.IndexOf($block) - 1))
        }

        $script:LastRead = [byte[]]$bytes.Clone()
        Set-GridImage $bytes
        Set-Status "Write complete; verified $($changed.Count) blocks"
    }
    catch {
        [Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
            'Write failed', 'OK', 'Error') | Out-Null
        Set-Status 'Write failed; read the chip again before retrying'
    }
    finally {
        $write.Enabled = [Ntag5Bridge]::IsConnected -and $null -ne $script:LastRead
    }
})

$form.Add_FormClosing({
    [Ntag5Bridge]::Disconnect()
})

if ($SelfTest) {
    Write-Output "SELFTEST OK: $($grid.Rows.Count) blocks, $script:UserBytes bytes"
    exit 0
}

[void]$form.ShowDialog()
