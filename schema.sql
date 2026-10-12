CREATE TABLE IF NOT EXISTS ModbusLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp       TEXT    NOT NULL,
    DeviceId        TEXT    NOT NULL,
    RegisterAddress INTEGER NOT NULL,
    Value           INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Timestamp ON ModbusLog(Timestamp DESC);
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Device_Reg ON ModbusLog(DeviceId, RegisterAddress);
