# Ntag5 Studio

Ntag5 Studio 是为本目录中的 NXP NTA5332 / NTAG 5 boost 驱动制作的 Windows 桌面工具。它通过供应商驱动 `\\.\SPBNFC01` 访问安装在机器内部 I²C 总线上的 NTAG5 模块。

供应商驱动虽然派生自 Microsoft SpbTestTool，但实际使用函数基址 `0x100`：打开、关闭和写后读控制码分别为 `0x04000400`、`0x04000404`、`0x04000410`。

NTA5332 内置模块本身不是 PN532 读卡器，不能通过 `SPBNFC01` 路径操作外部 M1/MIFARE 卡。本版本同时集成 PCR532 外接读卡器支持，通过 PCR532/libnfc 组件操作用户合法持有或获授权测试的 MIFARE Classic、Ultralight 和 NTAG Type 2 标签。

## 主要功能

- 读取 NTA5332 的 I²C 用户 EEPROM：块 `0x0000-0x01FE`，共 511 块、2044 字节。
- 保存和打开原始 `.bin` 备份，并生成包含 SHA-256、时间和区域信息的 `.json` 元数据。
- 支持导出和打开原始 `.mfd/.dump` 文件：NTA5332 用户区为 2044 字节；PCR532 中常见的 MIFARE Classic S20/S50/2K/S70 文件分别为 320/1024/2048/4096 字节，均按原始线性字节保存，不添加自定义文件头。
- 以 4 字节块为单位编辑十六进制内容，ASCII 预览和差异高亮。
- 写入前自动读取并备份当前芯片，只写变化块，每块等待 EEPROM 周期后回读校验。
- 支持十六进制、UTF-8、ASCII、UTF-16 LE/BE、GB18030、十进制字节、二进制、Base64 和 URL 百分号编码。
- 转换结果使用大尺寸标签页展示，可快速切换并复制当前格式的完整内容。
- “直接写入”页可直接输入字符串或字节内容，选择编码和偏移后写入 TAG，不需要先导入备份。
- 离线解析 NFC Forum Type 5 Capability Container、TLV 和常见 NDEF Text/URI 记录。
- 完整校验编辑区与芯片，并保留可复制的操作日志。
- 连接后自动读取会话寄存器 `0x10A1` 的 `CONFIG_1_REG`，显示 SRAM 启用状态、正常/镜像/透传/PHDC 模式及用户区映射；“运行状态”按钮可手动刷新。
- 集成 PCR532：设备/卡片检测、MIFARE Classic 已知密钥读取、`mfoc` 密钥恢复与备份、普通写卡、UID/CUID 卡写入与 UID 设置、UFUID 锁定，以及 Ultralight/NTAG Type 2 原始备份和恢复。

## 启动

1. 在目标机器上右键以管理员身份运行 `install.bat` 安装驱动。
2. 在设备管理器确认 `NTAG5` 设备正常，硬件 ID 应为 `ACPI\NTAG5332`。
3. 双击 `Start-Ntag5Studio.bat`。
4. 保持驱动路径为 `\\.\SPBNFC01`，单击“连接”，再单击“读取芯片”。

发布版依赖 Microsoft Windows Desktop Runtime 10。当前机器已经安装 .NET SDK 10，因此可以直接运行。

## PCR532 / MIFARE

1. 把 PCR532 连接到 USB；首次使用可在“设备”菜单进入 PCR532 页面并运行 CH341 驱动安装器。
2. 选择设备管理器中对应的 `COM` 口，默认波特率为 `115200`。只有明确配置为高速固件时才选 `921600`。
3. 单击“检测设备/卡片”确认读卡器、UID 和卡型。程序为每条命令生成 `pn532_uart:COMx:波特率` 的 libnfc 配置。
4. MIFARE Classic 可使用 `mfoc` 恢复密钥并备份，也可选择已有密钥 dump 后读取。S20/S50/2K/S70 文件分别保持 320/1024/2048/4096 字节原始格式。
5. “写入普通卡”不覆盖制造商块 0；“写入 UID/CUID 卡”会覆盖块 0，只适用于明确支持该命令的可改 UID 测试卡。
6. UID 设置和格式化仅适用于兼容的 UID/CUID 魔术卡。普通原厂卡、CPU 卡和不兼容代际的魔术卡不会因此变为可改 UID 卡。
7. “锁定 UFUID”使用 PCR532 官方组件兼容的 `mfoc -U` 流程。锁定不可逆，程序要求两次确认并在操作前保存备份。
8. Type 2 恢复会写入原始页面镜像；锁定位、OTP、配置页或密码页一旦写入可能不可逆，必须确认卡型、容量和镜像来源完全匹配。

PCR532 命令通过发布包中的外部 libnfc 工具执行，每次操作独立打开串口。请先退出 PCR532 官方软件或其它占用同一串口的程序。若报告找不到设备，检查 CH341 驱动、COM 口、波特率、供电和天线连接。

## 备份与恢复

- “保存备份”按当前文件类型保存原始 `.bin/.dump` 文件：NTAG5 为 2044 字节，S50 为 1024 字节，便于其它十六进制工具打开。
- “导出MFD”按当前文件类型保存原始镜像。S50 文件导出后仍为 1024 字节，能被 PCR532/libnfc 类工具按 MIFARE Classic 1K dump 读取。
- 打开 S50/MIFARE Classic 文件时，程序提供离线查看、编辑、编码转换和原尺寸保存；NTA5332 模块不是 MIFARE Classic 读写器，因此该文件不会启用 NTAG5 芯片写入、校验和 Type 5/NDEF 操作。
- “打开备份”只载入离线编辑区，不会立即修改硬件。
- “写入变化”会重新读取当前芯片，比较目标内容，弹出块数确认，并先将当前内容保存到：
  `文档\Ntag5Studio\Backups`
- “直接写入”会先读取当前 TAG，再把你输入的内容覆盖到指定偏移，自动保留其余字节。
- 任何回读不一致都会立即停止写入并报告具体块地址。失败或取消后应重新读取芯片。

## 安全边界

程序只写入用户块 `0x0000-0x01FE`。运行状态检测只读会话寄存器 `0x10A1` 的 Byte 1，不写入配置或会话寄存器。以下区域不会被写入：

- NFC 专用计数器块 `0x01FF`
- 配置区 `0x1000` 起
- SRAM `0x2000-0x203F`
- 密码、锁定位和原厂签名

如果检测到 `SRAM 镜像模式`，用户块 `0x0000-0x003F` 实际访问的是 256 字节易失性 SRAM，断电后不会保留；`0x0040-0x01FE` 仍访问 EEPROM。写入前程序会重新检测，并在变化涉及镜像区时提示。

NTA5332 数据手册明确规定 EEPROM 块为 4 字节，I²C 写 EEPROM 时 N=3，即地址后必须发送 4 个数据字节；本工具严格按此格式写入。

## 常见问题

- “找不到文件/设备”：确认驱动已安装且设备已枚举；默认路径必须为 `\\.\SPBNFC01`。
- “I²C 设备未响应”：检查模块供电、ACPI SPB 资源、I²C 地址和硬件连接；NFC 仲裁、EEPROM 写周期或禁用 I²C 也会导致失败。
- 写入后内容不同：程序会停止并显示第一个失败块。不要连续重试，先重新读取并查看自动备份。
- 受密码或写保护的用户块不会被本工具绕过，底层 NACK 会作为写入错误显示。

## 开发与验证

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\Ntag5Studio\Ntag5Studio.csproj -c Release
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\Ntag5Studio.SelfTest\Ntag5Studio.SelfTest.csproj -c Release
```

### 1.4.0 本机 PCR532 互通（2026-10-08）

- 自动发现本机 `D:\soft\PCR532`，也可通过发布目录中的 `PCR532.path.txt` 或环境变量 `PCR532_HOME` 指定其它安装位置。
- 每次命令通过 `LIBNFC_DEVICE` / `LIBNFC_DEFAULT_DEVICE` 显式选择界面上的串口，避免 PCR532 DLL 读取旧 COM3 配置；当前程序不改写原程序的配置文件。
- 支持常见 Type 2 原始页面镜像（64/80/144/164/180/192/212/216/232/256/540/572/924/936/1020 字节），读取后可查看、编辑、编码转换、解析 Type 2 NDEF 并按原始字节数保存。容量识别表示文件布局，不能单凭文件长度确认芯片型号。
- “文件 → 导出到 PCR532 文件目录”默认导出到 `D:\soft\PCR532\nfc-data\dumpfiles`，保持原始字节，不添加文件头，不补齐或截断数据。PCR532 附带的 `ChameleonMiniGUI.exe` 文件编辑器可打开 `.bin/.dump/.mfd`。
- 已向原程序的 `Templates` 目录安装 `NTAG5-2044B-offline.txt` 模板；在原文件编辑器选择该模板和“4字节一组”，可离线查看 NTAG5 的原始用户区。
- NTAG5 的 ISO15693 / Type 5 射频协议不受 PN532 支持。NTAG5 文件只能在 PCR532 编辑器中离线打开；不能当成 MIFARE / Type 2 镜像写到外部卡片。NTA5332 内置模块也不能用来读取外部 MIFARE 卡。规格参见 [NXP PN532 协议支持表](https://www.nxp.com/docs/en/product-selector-guide/75016728.pdf) 和 [NTA5332 数据手册](https://www.nxp.com/docs/en/data-sheet/NTA5332.pdf)。

本机实测：NTAG5 正常枚举并成功读取全部 2044 字节 EEPROM；PCR532 成功打开 COM8 / 115200，检测时未发现 ISO14443A 卡片。PCR532 原目录中的 30 个备份文件载入、导出后逐字节一致。另调用原程序文件编辑器实际使用的二进制解析/保存组件，验证 NTAG5 的 2044 字节镜像可完整读取并原样保存。14 项自检通过。验证过程未写芯片或外部卡片，真实射频写卡尚未验证。

### 1.4.1 PCR532 模拟 NDEF 导出

新增“文件 → 导出 PCR532 模拟标签文件（NDEF）”，将 NTAG5 / Type 2 镜像中的 NDEF 消息转换为原模拟组件需要的 Type 2 页面布局，按容量选择 NTAG213/215/216。NDEF 全部字节原样保留；原始备份不变。模拟文件只用于模拟，不保留原 NTAG5 的 UID/射频协议或 EEPROM 的其它区域，不能用作实体卡恢复备份。

本机实际的 125 字节 NDEF 已转换成 180 字节文件，并通过 COM8 启动 PCR532 原 `nfcemulatetag.exe`，进入模拟等待状态。手机识别和具体业务功能尚未实测。15 项自检通过；详见 `PCR532-Compatibility.md`。
