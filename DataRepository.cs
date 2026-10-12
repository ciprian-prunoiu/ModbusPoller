using System;
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ModbusPoller
{
    /// <summary>
    /// Repozitor pentru scrierea esantioanelor Modbus in baza de date SQLite.
    /// Foloseste Dapper pentru executarea rapida a comenzilor SQL (fara EF Core).
    /// </summary>
    public class DataRepository
    {
        // Connection string-ul catre fisierul SQLite (ex: "Data Source=modbus.db;")
        private readonly string _connectionString;

        /// <summary>
        /// Constructor — primeste connection string-ul din App.config.
        /// </summary>
        public DataRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Deschide o conexiune SQLite noua si aplica PRAGMA-urile de performanta.
        /// Se apeleaza pentru fiecare INSERT (connection pooling-ul SQLite e rapid).
        /// </summary>
        private IDbConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // WAL (Write-Ahead Logging) = permite citiri concurente in timpul scrierii.
            // Ideal pentru scenarii cu polling rapid + citiri din DB Browser.
            conn.Execute("PRAGMA journal_mode=WAL;");

            // synchronous=NORMAL = compromis intre viteza si siguranta.
            // FULL ar fi mai sigur (sync pe disc la fiecare commit) dar mult mai lent.
            conn.Execute("PRAGMA synchronous=NORMAL;");

            return conn;
        }

        /// <summary>
        /// Insereaza o valoare citita din Modbus in tabela ModbusLog.
        /// </summary>
        /// <param name="deviceId">Identificatorul dispozitivului (ex: "PLC-01")</param>
        /// <param name="address">Adresa registrului Modbus (ex: 0, 1, 2...)</param>
        /// <param name="value">Valoarea citita (unsigned 16-bit)</param>
        /// <param name="utc">Momentul citirii in UTC (stocat ca ISO 8601 in DB)</param>
        public void Insert(string deviceId, ushort address, ushort value, DateTime utc)
        {
            using (var c = OpenConnection())
            {
                // Dapper mapeaza automat proprietatile obiectului anonim la parametrii @xxx
                c.Execute(
                    "INSERT INTO ModbusLog (Timestamp, DeviceId, RegisterAddress, Value) " +
                    "VALUES (@ts, @dev, @addr, @val)",
                    new
                    {
                        // "o" = format rotund-trip ISO 8601 (ex: "2025-10-12T14:23:45.1234567Z")
                        // Se sorteaza corect alfabetic si pastreaza precizia la milisecunda
                        ts = utc.ToString("o"),

                        dev = deviceId,     // string — numele dispozitivului
                        addr = address,      // ushort — adresa registrului
                        val = value         // ushort — valoarea citita
                    });
            }
        }
    }
}