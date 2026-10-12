using System;

namespace ModbusPoller
{
    public class RegisterSample
    {
        public DateTime Timestamp { get; set; }
        public ushort Address   { get; set; }
        public ushort Value     { get; set; }
    }
}
