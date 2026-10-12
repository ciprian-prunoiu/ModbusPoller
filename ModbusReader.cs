using System;
using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;

namespace ModbusPoller
{
    /// <summary>
    /// Cititor Modbus care suporta atat TCP cat si RTU (serial).
    /// Foloseste NModbus 3.x — factory pattern pentru a crea master-ul.
    /// </summary>
    public class ModbusReader
    {
        // Parametri pentru conexiunea TCP
        private readonly string _host;      // IP-ul sau hostname-ul serverului Modbus
        private readonly int _port;         // Portul TCP (implicit 502)

        // Parametri pentru conexiunea RTU (serial)
        private readonly string _comPort;   // Portul serial (ex: "COM3")
        private readonly int _baudRate;     // Viteza serial (ex: 9600, 19200)

        // Parametri comuni
        private readonly byte _slaveId;     // Slave ID Modbus (1..247)
        private readonly bool _useRtu;      // true = RTU, false = TCP

        /// <summary>
        /// Constructor privat — fortam folosirea metodelor factory
        /// (CreateTcp / CreateRtu) pentru a evita ambiguitatea intre
        /// constructorii cu aceeasi semnatura (string, int, byte).
        /// </summary>
        private ModbusReader(string host, int port, string comPort, int baudRate, byte slaveId, bool useRtu)
        {
            _host = host;
            _port = port;
            _comPort = comPort;
            _baudRate = baudRate;
            _slaveId = slaveId;
            _useRtu = useRtu;
        }

        /// <summary>
        /// Factory pentru conexiune Modbus TCP.
        /// </summary>
        /// <param name="host">IP-ul simulatorului/PLC-ului (ex: "127.0.0.1")</param>
        /// <param name="port">Portul TCP (de regula 502)</param>
        /// <param name="slaveId">Slave ID Modbus</param>
        public static ModbusReader CreateTcp(string host, int port, byte slaveId)
            => new ModbusReader(host, port, null, 0, slaveId, useRtu: false);

        /// <summary>
        /// Factory pentru conexiune Modbus RTU (serial).
        /// </summary>
        /// <param name="comPort">Portul serial (ex: "COM3")</param>
        /// <param name="baudRate">Viteza de comunicatie (ex: 9600)</param>
        /// <param name="slaveId">Slave ID Modbus</param>
        public static ModbusReader CreateRtu(string comPort, int baudRate, byte slaveId)
            => new ModbusReader(null, 0, comPort, baudRate, slaveId, useRtu: true);

        /// <summary>
        /// Citeste registre de tip Holding (4xxxx).
        /// Function code 03 (Read Holding Registers).
        /// </summary>
        /// <param name="start">Adresa de start (0-based)</param>
        /// <param name="count">Cate registre sa citeasca</param>
        public ushort[] ReadHoldingRegisters(ushort start, ushort count)
            => _useRtu ? ReadRtu(start, count, true) : ReadTcp(start, count, true);

        /// <summary>
        /// Citeste registre de tip Input (3xxxx).
        /// Function code 04 (Read Input Registers).
        /// </summary>
        /// <param name="start">Adresa de start (0-based)</param>
        /// <param name="count">Cate registre sa citeasca</param>
        public ushort[] ReadInputRegisters(ushort start, ushort count)
            => _useRtu ? ReadRtu(start, count, false) : ReadTcp(start, count, false);

        /// <summary>
        /// Citire prin TCP — deschide o conexiune noua pentru fiecare citire.
        /// Simplu, dar nu foarte eficient la polling rapid.
        /// (Pentru productie, s-ar putea mentine o conexiune persistenta.)
        /// </summary>
        private ushort[] ReadTcp(ushort start, ushort count, bool isHolding)
        {
            using (var client = new TcpClient(_host, _port))
            {
                // Timeout-uri ca sa nu blocam aplicatia daca simulatorul nu raspunde
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;

                // NModbus 3.x: cream master-ul prin factory
                var factory = new ModbusFactory();
                IModbusMaster master = factory.CreateMaster(client);

                // Function code 03 (Holding) sau 04 (Input)
                return isHolding
                    ? master.ReadHoldingRegisters(_slaveId, start, count)
                    : master.ReadInputRegisters(_slaveId, start, count);
            }
        }

        /// <summary>
        /// Citire prin RTU (serial) — deschide portul pentru fiecare citire.
        /// Parametrii serial sunt fixati la 8N1 (8 data bits, No parity, 1 stop bit),
        /// ceea ce e standardul Modbus RTU.
        /// </summary>
        private ushort[] ReadRtu(ushort start, ushort count, bool isHolding)
        {
            using (var port = new SerialPort(_comPort, _baudRate, Parity.None, 8, StopBits.One))
            {
                port.Open();
                port.ReadTimeout = 2000;
                port.WriteTimeout = 2000;

                // NModbus.Serial: extensia CreateRtuMaster primeste direct SerialPort
                var factory = new ModbusFactory();
                IModbusMaster master = factory.CreateRtuMaster(port);

                return isHolding
                    ? master.ReadHoldingRegisters(_slaveId, start, count)
                    : master.ReadInputRegisters(_slaveId, start, count);
            }
        }
    }
}