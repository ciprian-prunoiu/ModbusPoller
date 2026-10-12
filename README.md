# ModbusPoller

Aplicatie Windows Forms (.NET Framework 4.8) care face polling periodic pe un dispozitiv Modbus TCP/RTU (PLC, senzor, simulator) si scrie valorile citite intr-o baza de date SQLite. Afiseaza datele in timp real intr-un dashboard cu tabel si grafic.

![Dashboard](doc/images/dashboard.png)

## Cuprins

1. Descriere generala
2. Caracteristici
3. Arhitectura
4. Schema logica
5. Fluxul de date
6. Tehnologii folosite
7. Cerinte de sistem
8. Instalare
9. Configurare App.config
10. Configurare Modbus Slave
11. Script Python de simulare senzori
12. Structura proiectului
13. Componente cheie
14. Baza de date
15. Utilizare
16. Instalare ca serviciu Windows
17. Troubleshooting
18. Video demo
19. Capturi de ecran
20. Licenta

## 1. Descriere generala

**ModbusPoller** este un demo functional care arata cum se construieste un serviciu de achizitie date industriale folosind:

- **Modbus TCP/RTU** - protocolul standard industrial pentru comunicatia cu PLC-uri, senzori si alte echipamente
- **WinForms** - interfata grafica desktop, cu tabel si grafic in timp real
- **NModbus** - biblioteca open-source pentru comunicatia Modbus
- **Dapper + SQLite** - persistenta rapida a datelor fara EF Core

Aplicatia citeste la fiecare secunda un set de registre Modbus (Holding sau Input), afiseaza valorile in timp real si le scrie intr-o baza de date SQLite pentru analiza ulterioara.

## 2. Caracteristici

- Modbus TCP (port 502) si RTU (serial)
- Holding Registers (FC03) si Input Registers (FC04)
- Polling interval configurabil (1000 ms implicit)
- Dashboard cu tabel live + grafic in timp real (10 serii colorate)
- Alias-uri configurabile (ex: "Temperatura" in loc de "Reg 0")
- Persistenta SQLite prin Dapper (INSERT-uri rapide)
- Log de evenimente colorat
- Status bar cu indicator conexiune, contor citiri, timp raspuns
- Rulare ca aplicatie desktop sau Windows Service
- Configurare prin App.config (fara recompilare)

## 3. Arhitectura

Diagrama de ansamblu (in format text):

    +-------------------+        +-----------------------+        +------------------+
    |                   |        |                       |        |                  |
    |  Modbus Slave /   |  TCP   |   ModbusPoller        | SQLite |   modbus.db      |
    |  PLC / Senzor     |<------>|   (WinForms +         |<------>|   (ModbusLog)    |
    |                   |  502   |    Polling Loop)      | Dapper |                  |
    |  Holding Regs     |        |                       |        |                  |
    |  0..9 = valori    |        |   - NModbus           |        |   - Timestamp    |
    |                   |        |   - BackgroundService |        |   - DeviceId     |
    +-------------------+        +-----------------------+        |   - RegisterAddr |
                                           |                       |   - Value        |
                                           |                       +------------------+
                                           v
                                 +---------------------+
                                 |   Dashboard UI      |
                                 |   - DataGridView    |
                                 |   - Chart (live)    |
                                 |   - Log             |
                                 |   - Status bar      |
                                 +---------------------+

Componente principale:

| Componenta | Rol |
|-----------|-----|
| ModbusReader | Comunicatie Modbus TCP/RTU prin NModbus |
| PollingService | Bucla asincrona de citire periodica |
| DataRepository | INSERT-uri SQLite prin Dapper |
| DatabaseInitializer | Creaza tabela si indecsii la prima rulare |
| MainForm | UI-ul: grid, chart, log, status bar |
| RegisterSample | Model: Timestamp, Address, Value |

## 4. Schema logica

Diagrama detaliata a componentelor:

    +---------------------------------------------------------------------+
    |                          MODBUS POLLER                              |
    |                     (WinForms .NET Framework 4.8)                   |
    |                                                                     |
    |  +-------------------------------------------------------------+    |
    |  |                     Prezentare (UI)                         |    |
    |  |  +----------+  +----------+  +----------+  +-------------+  |    |
    |  |  | Toolbar  |  |   Grid   |  |  Chart   |  |    Log      |  |    |
    |  |  | Start/   |  | Address  |  | 10 serii |  |  RichText   |  |    |
    |  |  | Stop     |  | Value    |  | live     |  |  colorat    |  |    |
    |  |  |          |  | Timestamp|  |          |  |             |  |    |
    |  |  +----------+  +----------+  +----------+  +-------------+  |    |
    |  +-------------------------------------------------------------+    |
    |                              ^                                      |
    |                              | BeginInvoke (thread-safe)            |
    |                              |                                      |
    |  +---------------------------+---------------------------------+    |
    |  |                    Logica (Business)                        |    |
    |  |  +----------------+    +--------------------------------+   |    |
    |  |  |  PollingLoop   |    |        UpdateUi                |   |    |
    |  |  |  (async task)  |--->|  - actualizeaza grid           |   |    |
    |  |  |  - la 1000ms   |    |  - adauga puncte in chart      |   |    |
    |  |  |  - citire modbus    |  - updateaza status bar        |   |    |
    |  |  |  - scriere DB  |    |                                |   |    |
    |  |  +-------+--------+    +--------------------------------+   |    |
    |  +----------+-----------------------------------------------+    |
    |             |                                                   |
    |  +----------v-----------------------------------------------+    |
    |  |                    Acces date (Data)                     |    |
    |  |  +----------------+    +--------------------------------+|    |
    |  |  | ModbusReader   |    |      DataRepository           ||    |
    |  |  | (NModbus 3.x)  |    |      (Dapper)                 ||    |
    |  |  |                |    |                               ||    |
    |  |  | TCP: 502       |    |  INSERT INTO ModbusLog        ||    |
    |  |  | RTU: COMx      |    |  PRAGMA WAL + NORMAL          ||    |
    |  |  +-------+--------+    +---------------+---------------++    |
    |  +----------+-----------------------------+-----------------+    |
    +-------------+-----------------------------+----------------------+
                  |                             |
                  | Modbus TCP                  | SQL
                  | (Function Code 03/04)       |
                  v                             v
       +----------------------+      +------------------------+
       |   Modbus Slave       |      |   SQLite               |
       |   (simulator/PLC)    |      |   modbus.db            |
       |                      |      |                        |
       |   Holding Reg 0..9   |      |   Table: ModbusLog     |
       |   Values: 0..65535   |      |   Id, Timestamp,       |
       |                      |      |   DeviceId, Address,   |
       |                      |      |   Value                |
       +----------------------+      +------------------------+

Parametri Modbus folositi:

| Parametru | Valoare demo | Descriere |
|-----------|--------------|-----------|
| Protocol | Modbus TCP | Alternativ: Modbus RTU (serial) |
| Port | 502 | Portul standard Modbus TCP |
| Slave ID | 1 | Identificatorul dispozitivului |
| Function Code | 03 | Read Holding Registers |
| Start Address | 0 | Prima adresa citita |
| Quantity | 10 | Numarul de registre |
| Polling interval | 1000 ms | Frecventa de citire |
| TCP timeout | 2000 ms | Timeout citire/scriere |

## 5. Fluxul de date

Initializare (o singura data, la pornire):

    Main() [Program.cs]
       |
       +--> SQLitePCL.Batteries_V2.Init()       <- initializeaza provider SQLite
       |
       +--> Application.Run(new MainForm())
       |
       +--> MainForm.OnLoad()
            |
            +--> DatabaseInitializer.EnsureDatabase()
                 |
                 +--> creeaza modbus.db daca nu exista
                 +--> creeaza tabela ModbusLog daca nu exista
                 +--> creeaza indecsii

Bucla de polling (la fiecare secunda):

    User apasa [Start]
       |
       +--> MainForm.StartPolling()
            |
            +--> Task.Run(PollingLoop)
                 |
                 +--> while (!cancelled)
                      |
                      +--> [1] ModbusReader.ReadHoldingRegisters(0, 10)
                      |        |
                      |        +--> TCP connect -> FC03 -> primeste 10 ushort
                      |
                      +--> [2] Construieste List<RegisterSample>
                      |
                      +--> [3] DataRepository.Insert() x 10
                      |        |
                      |        +--> SQLite INSERT (cu Dapper)
                      |
                      +--> [4] BeginInvoke(UpdateUi)
                      |        |
                      |        +--> actualizeaza DataGridView
                      |        +--> adauga 10 puncte in Chart
                      |        +--> updateaza StatusStrip
                      |
                      +--> [5] await Task.Delay(1000)

Oprire:

    User apasa [Stop]
       |
       +--> MainForm.StopPolling()
            |
            +--> _cts.Cancel()
                 |
                 +--> Task.Delay() arunca TaskCanceledException
                      |
                      +--> while (!cancelled) iese -> thread se termina

## 6. Tehnologii folosite

| Componenta | Tehnologie | Versiune |
|-----------|-----------|----------|
| Framework | .NET Framework | 4.8 |
| UI | Windows Forms | (inclus) |
| Grafic | System.Windows.Forms.DataVisualization | (inclus) |
| Comunicatie Modbus | NModbus | 3.0.81 |
| Extensie RTU | NModbus.Serial | 3.0.83 |
| ORM | Dapper | 2.1.35 |
| Baza de date | SQLite | 3.x (via winsqlite3.dll) |
| Driver SQLite | Microsoft.Data.Sqlite.Core | 8.0.0 |
| Provider nativ | SQLitePCLRaw.bundle_winsqlite3 | 2.1.11 |

## 7. Cerinte de sistem

Pentru rulare:
- Windows 10 sau 11
- .NET Framework 4.8
- 2 GB RAM minim
- 50 MB spatiu liber

Pentru dezvoltare:
- Visual Studio 2019 sau 2022 (Community)
- Workload: .NET desktop development

Pentru testare:
- Modbus Slave (simulator) - modbustools.com/modbus_slave.html
- Python 3.8+ (pentru scriptul de simulare senzori)
- DB Browser for SQLite (pentru inspectarea datelor)

## 7. Baza de date

### Tehnologia folosita

**SQLite** - baza de date embedded, serverless, stocata intr-un singur fisier local (`modbus.db`).

### Tabele folosite

Baza de date `modbus.db` contine o singura tabela principala, plus indecsii asociati.

| Tabela | Tip | Rol | Randuri estimate |
|--------|-----|-----|------------------|
| `ModbusLog` | Tabela principala | Stocheaza fiecare citire Modbus (un rand per registru per ciclu) | ~864.000/zi la 10 registre × 1 sec |
| `sqlite_sequence` | Tabela interna SQLite | Gestioneaza AUTOINCREMENT pentru `ModbusLog.Id` | 1 |
| `sqlite_stat1` | Tabela interna SQLite | Statistici pentru optimizatorul de query-uri | 0-5 |

**Nota:** Tabelele `sqlite_*` sunt create automat de SQLite si nu trebuie modificate manual. Sunt incluse aici doar pentru completitudine.

### Structura tabelei `ModbusLog`

| Coloana | Tip SQLite | Null | Default | Descriere | Exemplu |
|---------|-----------|------|---------|-----------|---------|
| `Id` | INTEGER | NOT NULL | AUTOINCREMENT | Cheie primara, generata automat | `1`, `2`, `3`, ... |
| `Timestamp` | TEXT | NOT NULL | - | Momentul citirii, format ISO 8601 UTC | `2025-10-12T14:23:45.1234567Z` |
| `DeviceId` | TEXT | NOT NULL | - | Identificatorul dispozitivului | `PLC-01` |
| `RegisterAddress` | INTEGER | NOT NULL | - | Adresa registrului Modbus | `0` .. `65535` |
| `Value` | INTEGER | NOT NULL | - | Valoarea citita (unsigned 16-bit) | `0` .. `65535` |

**Cheie primara:** `Id` (INTEGER PRIMARY KEY AUTOINCREMENT)

### Schema SQL completa

```sql
-- Tabela principala: fiecare citire Modbus = un rand
CREATE TABLE IF NOT EXISTS ModbusLog (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp       TEXT    NOT NULL,
    DeviceId        TEXT    NOT NULL,
    RegisterAddress INTEGER NOT NULL,
    Value           INTEGER NOT NULL
);

-- Index pe Timestamp (pentru query-uri de tip "ultimele N")
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Timestamp
    ON ModbusLog(Timestamp DESC);

-- Index compus (pentru query-uri "toate valorile registrului X")
CREATE INDEX IF NOT EXISTS IX_ModbusLog_Device_Reg
    ON ModbusLog(DeviceId, RegisterAddress);


