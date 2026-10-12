using System.Configuration;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ModbusPoller
{
    public static class DatabaseInitializer
    {
        public static string EnsureDatabase()
        {
            var cs = ConfigurationManager.ConnectionStrings["ModbusDb"].ConnectionString;
            var builder = new SqliteConnectionStringBuilder(cs);
            var dbFile = builder.DataSource;

            if (!Path.IsPathRooted(dbFile))
            {
                dbFile = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, dbFile);
                builder.DataSource = dbFile;
                cs = builder.ConnectionString;
            }

            using (var c = new SqliteConnection(cs))
            {
                c.Open();
                c.Execute(@"
CREATE TABLE IF NOT EXISTS ModbusLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp       TEXT    NOT NULL,
    DeviceId        TEXT    NOT NULL,
    RegisterAddress INTEGER NOT NULL,
    Value           INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Timestamp ON ModbusLog(Timestamp DESC);
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Device_Reg ON ModbusLog(DeviceId, RegisterAddress);
");
            }

            return dbFile;
        }
    }
}
