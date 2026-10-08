# PCR532 与 NTAG5 Studio 文件互通

启动仓库根目录的 `Start-Ntag5Studio.bat`（1.4.1）。已配置本机 PCR532 安装路径 `D:\soft\PCR532`，串口 COM8，115200。

## PCR532 → NTAG5 Studio

“文件 → 打开卡片文件”选择 PCR532 保存的 `.dump/.bin/.mfd`。支持 Classic S20/S50/2K/S70 和常见 Type 2 原始页面镜像，包括本机实际的 180 字节 NTAG 文件。编辑后保存仍保留原始容量及布局，PCR532 可继续打开。

## NTAG5 Studio → PCR532

“文件 → 导出到 PCR532 文件目录”将当前镜像保存到 `D:\soft\PCR532\nfc-data\dumpfiles`。在 PCR532 附带的 `ChameleonMiniGUI.exe`（文件编辑器）点击“打开”，选择导出的文件。NTAG5 镜像选择“4字节一组”；模板选择 `NTAG5-2044B-offline`，安装模板后需重新打开编辑器。

已保存本机真实 NTAG5 读取样例：`D:\soft\PCR532\nfc-data\dumpfiles\NTAG5-compatibility-2044B.dump`。

## 硬件范围

NTAG5 / NTA5332 使用 ISO15693（Type 5），PCR532 / PN532 支持 ISO14443A/B，不能直接射频读写 NTAG5。双方可交换原始文件；NTAG5 的射频读写无法通过软件补上。不要把 NTAG5 文件作为 MIFARE 或 Type 2 文件恢复到其它芯片。

## PCR532 模拟 NFC Tag（1.4.1）

“文件 → 导出 PCR532 模拟标签文件（NDEF）”提取当前 NTAG5 / Type 2 镜像中的第一条完整 NDEF 消息，按容量生成 NTAG213/215/216 布局（180/540/924 字节）。保留 NDEF 消息全部字节，重建只读 Type 2 能力容器和 TLV，不保留原 UID、原厂签名、密码、Type 5 协议或其余 EEPROM。容量不足、TLV 损坏、NDEF 不完整和分块记录会报错，绝不截断内容。

本机已生成：`D:\soft\PCR532\nfc-data\dumpfiles\NTAG5-NDEF-emulate-NTAG213.dump`，180 字节，其中 NDEF 为 125 字节。可在 PCR532 原程序“模拟 NFC Tag”功能中选择此文件，或双击 `D:\soft\PCR532\Start-NTAG5-NDEF-Emulation.cmd`，手机贴近 PCR532 天线。此文件仅供模拟，不是实体卡恢复备份。

已检查本机原组件：`nfcemulatetag.exe` 读取输入文件作为完整页面内存，READ 命令按页面号 × 4 返回 16 字节；支持读取和 HALT，不支持实体 NTAG 的完整命令集。其模拟射频 UID 固定为 `08 00 B0 0B`，不会采用导出文件中的 UID。NTAG5 原文件的 CC / TLV 在偏移 0 / 4，必须转换成 Type 2 的 12 / 16，不能直接加载原 2044 字节文件期待得到有效 NDEF。

用生成文件在 COM8 启动原模拟组件，成功输出 `Emulating NDEF tag now, please touch it with a second NFC device`。已停止测试并释放串口。尚未完成手机端识别或小米业务功能实测；这些功能可能依赖标签类型、UID 和手机软件。15 项自检通过。

PCR532 主程序当前配置已经改为 COM8。原根目录配置已保留为 `libnfc.conf.before-ntag5-compat-20261008`。新版 Studio 使用每个进程的 libnfc 设备环境变量指定串口，不改写 PCR532 配置。

## 验证结果（2026-10-08）

- 读取本机 NTAG5 全部 2044 字节，状态为 EEPROM。
- PCR532 COM8 / 115200 成功打开；检测时没有 ISO14443A 卡片。
- 30 个原 PCR532 备份文件在 Studio 载入、保存后逐字节一致。
- 调用 PCR532 原文件编辑器的 `DumpStrategyFactory` / `BinaryDumpStrategy`，确认 NTAG5 的 2044 字节文件可完整读取和原样保存；保存后的文件也能被 Studio 重新识别。
- 14 项自检及 Release 编译通过，检查了不同窗口尺寸的界面。
- 未对芯片或外部卡片执行写入；射频写卡尚未实测。

验证输出保存在仓库 `artifacts\compatibility`。文件互通没有添加头部、补齐或截断数据，也没有修改原厂程序可执行文件。
