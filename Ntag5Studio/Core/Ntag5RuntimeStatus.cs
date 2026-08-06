namespace Ntag5Studio.Core;

public enum Ntag5ArbiterMode
{
    Normal = 0,
    SramMirror = 1,
    SramPassThrough = 2,
    SramPhdc = 3
}

public enum Ntag5UseCase
{
    I2cTarget = 0,
    I2cController = 1,
    GpioPwm = 2,
    HostInterfaceDisabled = 3
}

public sealed record Ntag5RuntimeStatus(byte Config1Register)
{
    public bool SramEnabled => (Config1Register & 0x02) != 0;

    public Ntag5ArbiterMode ArbiterMode => (Ntag5ArbiterMode)((Config1Register >> 2) & 0x03);

    public Ntag5UseCase UseCase => (Ntag5UseCase)((Config1Register >> 4) & 0x03);

    public bool TransferDirectionNfcToI2c => (Config1Register & 0x01) != 0;

    public bool IsSramMirrorActive => SramEnabled && ArbiterMode == Ntag5ArbiterMode.SramMirror;

    public string ArbiterModeDisplay => ArbiterMode switch
    {
        Ntag5ArbiterMode.Normal => "正常模式",
        Ntag5ArbiterMode.SramMirror => "SRAM 镜像模式",
        Ntag5ArbiterMode.SramPassThrough => "SRAM 透传模式",
        Ntag5ArbiterMode.SramPhdc => "SRAM PHDC 模式",
        _ => "未知模式"
    };

    public string UseCaseDisplay => UseCase switch
    {
        Ntag5UseCase.I2cTarget => "I2C 目标设备",
        Ntag5UseCase.I2cController => "I2C 控制器",
        Ntag5UseCase.GpioPwm => "GPIO / PWM",
        Ntag5UseCase.HostInterfaceDisabled => "主机接口已禁用",
        _ => "未知用途"
    };

    public string UserMemoryMappingDisplay => IsSramMirrorActive
        ? "0x0000-0x003F：SRAM；0x0040-0x01FE：EEPROM"
        : "0x0000-0x01FE：EEPROM";

    public string TransferDirectionDisplay => TransferDirectionNfcToI2c
        ? "NFC -> I2C"
        : "I2C -> NFC";

    public string CompactDisplay => IsSramMirrorActive
        ? "存储：SRAM 镜像"
        : SramEnabled
            ? $"存储：EEPROM + {ArbiterModeDisplay}"
            : "存储：EEPROM（SRAM 关闭）";
}
