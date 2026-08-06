NTA5332 NTAG5 legacy PowerShell user-memory tool

Use Start-Ntag5Studio.bat for the supported .NET application. This PowerShell
file is retained only as a fallback.

Usage
1. Install the driver using install.bat. The machine must contain the NTAG5/NTA5332 hardware.
2. Double-click Start-Ntag5Tool.bat.
3. Select Connect, then Read NTAG5.
4. Double-click a Byte0-Byte3 cell and enter two hexadecimal digits.
5. Save backup writes a 2044-byte raw user-memory image.
6. Write changes first saves a pre-write backup to the Desktop, then writes and verifies each changed block.

Safety scope
- Only I2C user EEPROM blocks 0x0000-0x01FE are accessed (2044 bytes).
- Configuration memory from 0x1000, passwords, lock bits, the originality signature, and the NFC-only counter are never accessed.
- EEPROM writes are exactly four bytes as required by the NTA5332 data sheet.
- Password protection, a disabled I2C interface, or missing ACPI SPB resources will cause the operation to fail.

Driver interface
The supplied driver is derived from Microsoft SpbTestTool but changes the
symbolic link to \\.\SPBNFC01. ACPI configures the I2C target resource. The
NTA5332 default I2C address is 0x54.
