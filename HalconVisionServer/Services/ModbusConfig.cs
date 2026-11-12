using System.IO.Ports;

namespace HalconVisionServer.Services
{
    public enum AddressType
    {
        HoldingRegister,  // 保持寄存器 (0x03/0x06/0x10)
        Coil,             // 线圈 (0x01/0x05/0x0F)
        InputRegister,    // 输入寄存器 (0x04)
        DiscreteInput     // 离散输入 (0x02)
    }

    public enum ModbusProtocol
    {
        Tcp,
        Rtu
    }

    public enum DataType
    {
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float,
        Double
    }

    public enum DisplayFormat
    {
        Decimal,
        HexWithPrefix,
        HexNoPrefix,
        Octal,
        Binary,
        Ascii
    }

    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }

    public class ModbusConfig
    {
        public byte SlaveAddress { get; set; } = 1;
        public ModbusProtocol Protocol { get; set; } = ModbusProtocol.Tcp;
        public int TimeoutMs { get; set; } = 3000;
        public DataType DataType { get; set; } = DataType.Int16;
        public DisplayFormat DisplayFormat { get; set; } = DisplayFormat.Decimal;
        public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

        // TCP
        public string IpAddress { get; set; } = "192.168.0.0";
        public int Port { get; set; } = 502;

        // RTU
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public Parity Parity { get; set; } = Parity.None;

        public ushort StartAddress { get; set; } = 0;
        public ushort RegisterCount { get; set; } = 1;
        public AddressType AddressType { get; set; } = AddressType.HoldingRegister;
    }
}