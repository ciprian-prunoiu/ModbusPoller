using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ModbusPoller
{
    public class MainForm : Form
    {
        // ---------- Configurare citita din App.config ----------
        private readonly string _host;              // IP-ul simulatorului Modbus (ex: 127.0.0.1)
        private readonly int _port;                 // Portul Modbus TCP (implicit 502)
        private readonly byte _slaveId;             // Slave ID Modbus (1..247)
        private readonly ushort _startAddr;         // Adresa de start a registrelor (ex: 0)
        private readonly ushort _count;             // Numarul de registre citite (ex: 10)
        private readonly string _regType;           // "Holding" sau "Input"
        private readonly int _intervalMs;           // Intervalul de polling in milisecunde
        private readonly string _deviceId;          // Identificatorul dispozitivului pentru DB
        private readonly int _chartMaxPoints;       // Cate puncte pastreaza graficul pe serie
        private readonly string[] _aliases;         // Numele "prietenoase" ale registrelor

        // ---------- Servicii interne ----------
        private readonly ModbusReader _reader;      // Cititorul Modbus (TCP sau RTU)
        private readonly DataRepository _repo;      // Repozitorul SQLite

        // ---------- Stare runtime ----------
        private CancellationTokenSource _cts;       // Token pentru oprirea polling-ului
        private int _readCount;                     // Numar total de citiri reusite
        private int _errorCount;                    // Numar total de erori
        private DateTime _lastSampleAt;             // Momentul ultimei citiri reusite

        // ---------- Controale UI ----------
        private DataGridView _grid;                 // Tabelul cu registre
        private Chart _chart;                       // Graficul in timp real
        private RichTextBox _log;                   // Jurnalul de evenimente (jos)
        private Button _btnStart;                   // Butonul Start
        private Button _btnStop;                    // Butonul Stop
        private StatusStrip _status;                // Bara de status (jos)
        private ToolStripStatusLabel _lblConn;      // Indicator conexiune
        private ToolStripStatusLabel _lblReads;     // Contor citiri
        private ToolStripStatusLabel _lblDb;        // Info SQLite
        private ToolStripStatusLabel _lblLast;      // Timpul ultimei citiri
        private SplitContainer _mainSplit;          // Splitter-ul grid | chart

        // Paleta de culori pentru seriile graficului (10 culori distincte)
        private static readonly Color[] Palette =
        {
            Color.FromArgb(0, 120, 215),      // albastru
            Color.FromArgb(216, 59, 1),       // rosu
            Color.FromArgb(16, 137, 62),      // verde
            Color.FromArgb(136, 23, 152),     // mov
            Color.FromArgb(255, 140, 0),      // portocaliu
            Color.FromArgb(0, 153, 188),      // cyan
            Color.FromArgb(232, 17, 35),      // rosu aprins
            Color.FromArgb(104, 33, 122),     // violet
            Color.FromArgb(0, 99, 177),       // albastru inchis
            Color.FromArgb(118, 118, 118),    // gri
        };

        public MainForm()
        {
            // Citim toti parametrii din App.config
            _host = ConfigurationManager.AppSettings["Modbus.Host"];
            _port = int.Parse(ConfigurationManager.AppSettings["Modbus.Port"]);
            _slaveId = byte.Parse(ConfigurationManager.AppSettings["Modbus.SlaveId"]);
            _startAddr = ushort.Parse(ConfigurationManager.AppSettings["Modbus.StartAddress"]);
            _count = ushort.Parse(ConfigurationManager.AppSettings["Modbus.NumRegisters"]);
            _regType = ConfigurationManager.AppSettings["Modbus.RegisterType"];
            _intervalMs = int.Parse(ConfigurationManager.AppSettings["Polling.IntervalMs"]);
            _deviceId = ConfigurationManager.AppSettings["Device.Id"];
            _chartMaxPoints = int.Parse(ConfigurationManager.AppSettings["Chart.MaxPoints"]);

            // Cream reader-ul Modbus si repozitorul SQLite
            _reader = ModbusReader.CreateTcp(_host, _port, _slaveId);
            _repo = new DataRepository(ConfigurationManager.ConnectionStrings["ModbusDb"].ConnectionString);

            // Citim alias-urile registrelor (numele prietenoase)
            var aliasesRaw = ConfigurationManager.AppSettings["Register.Aliases"] ?? "";
            _aliases = aliasesRaw.Split(',');

            BuildUi();
        }

        // ============================================================
        // Construirea interfetei grafice
        // ============================================================
        private void BuildUi()
        {
            Text = "Modbus Polling Dashboard";
            Width = 1280;
            Height = 800;
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(245, 245, 245);

            // ---------- Toolbar (sus): butoane + info config ----------
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
            _btnStart = new Button
            {
                Text = "▶ Start",
                Width = 100,
                Height = 32,
                BackColor = Color.FromArgb(16, 137, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _btnStop = new Button
            {
                Text = "■ Stop",
                Width = 100,
                Height = 32,
                BackColor = Color.FromArgb(196, 43, 28),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Enabled = false
            };
            _btnStart.Click += (s, e) => StartPolling();
            _btnStop.Click += (s, e) => StopPolling();

            // Eticheta care arata configuratia curenta (target, slave, interval)
            var lblCfg = new Label
            {
                Text = $"  Target: {_host}:{_port}  |  Slave: {_slaveId}  |  {_regType}[{_startAddr}..{_startAddr + _count - 1}]  |  {_intervalMs} ms",
                AutoSize = false,
                Width = 700,
                Height = 32,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(60, 60, 60)
            };

            topPanel.Controls.Add(_btnStart);
            topPanel.Controls.Add(_btnStop);
            topPanel.Controls.Add(lblCfg);
            _btnStart.Location = new Point(8, 8);
            _btnStop.Location = new Point(116, 8);
            lblCfg.Location = new Point(230, 8);

            // ---------- Grid (stanga): tabelul cu registre ----------
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,          // Umple tot panoul din stanga
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Font = new Font("Consolas", 9F)
            };
            _grid.Columns.Add("addr", "Address");
            _grid.Columns.Add("value", "Value");
            _grid.Columns.Add("ts", "Timestamp");
            _grid.Columns["addr"].Width = 80;
            _grid.Columns["value"].Width = 90;
            _grid.Columns["ts"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            // Adaugam cate un rand pentru fiecare registru
            for (ushort i = 0; i < _count; i++)
            {
                _grid.Rows.Add((_startAddr + i).ToString(), "-", "-");
            }

            // ---------- Chart (dreapta): graficul live ----------
            _chart = new Chart
            {
                Dock = DockStyle.Fill,          // Umple tot panoul din dreapta
                BackColor = Color.White,
                Palette = ChartColorPalette.None
            };
            // Zona principala a graficului (axe + grid lines)
            var area = new ChartArea("main")
            {
                BackColor = Color.White,
                AxisX = new Axis
                {
                    Title = "Timp",
                    LabelStyle = { Format = "HH:mm:ss" },
                    MajorGrid = { LineColor = Color.FromArgb(230, 230, 230) }
                },
                AxisY = new Axis
                {
                    Title = "Valoare registru",
                    MajorGrid = { LineColor = Color.FromArgb(230, 230, 230) }
                }
            };
            _chart.ChartAreas.Add(area);

            // Legenda (sus, centrata) — afiseaza numele fiecarei serii
            var legend = new Legend("legend")
            {
                Docking = Docking.Top,
                Alignment = StringAlignment.Center,
                MaximumAutoSize = 100,
                IsTextAutoFit = true
            };
            _chart.Legends.Add(legend);

            // Cream cate o serie pentru fiecare registru
            int seriesCount = (int)_count;
            for (int i = 0; i < seriesCount; i++)
            {
                // Folosim alias-ul daca exista, altfel "Reg N"
                string alias = (i < _aliases.Length && !string.IsNullOrWhiteSpace(_aliases[i]))
                    ? _aliases[i].Trim()
                    : $"Reg {_startAddr + i}";

                var s = new Series(alias)
                {
                    ChartType = SeriesChartType.Line,       // linie continua
                    BorderWidth = 2,
                    XValueType = ChartValueType.DateTime,   // axa X = timp
                    Color = Palette[i % Palette.Length],
                    IsVisibleInLegend = true
                };
                s.MarkerStyle = MarkerStyle.Circle;         // puncte pe linie
                s.MarkerSize = 4;
                _chart.Series.Add(s);
            }

            // ---------- Log (jos): jurnal de evenimente ----------
            _log = new RichTextBox
            {
                Dock = DockStyle.Bottom,
                Height = 160,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),     // fundal inchis (stil consola)
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None
            };

            // ---------- Status bar (jos de tot) ----------
            _status = new StatusStrip { BackColor = Color.FromArgb(225, 225, 225) };
            _lblConn = new ToolStripStatusLabel("● Oprit") { ForeColor = Color.Gray };
            _lblReads = new ToolStripStatusLabel("Citiri: 0");
            _lblLast = new ToolStripStatusLabel("Ultima: -");
            _lblDb = new ToolStripStatusLabel("SQLite: -");
            _lblDb.Spring = true;                            // impinge eticheta spre dreapta
            _lblDb.TextAlign = ContentAlignment.MiddleRight;
            _status.Items.Add(_lblConn);
            _status.Items.Add(new ToolStripSeparator());
            _status.Items.Add(_lblReads);
            _status.Items.Add(new ToolStripSeparator());
            _status.Items.Add(_lblLast);
            _status.Items.Add(_lblDb);

            // ---------- SplitContainer: grid (stanga) | chart (dreapta) ----------
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(220, 220, 220)
                // Panel1MinSize / Panel2MinSize se seteaza in OnShown
                // (altfel crapa cu InvalidOperationException)
            };
            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_chart);

            // ---------- Asamblare finala a controalelor in forma ----------
            // Ordinea conteaza: Dock-ul se aplica in ordine inversa a adaugarii
            Controls.Add(split);
            Controls.Add(_log);
            Controls.Add(topPanel);
            Controls.Add(_status);

            // Pastram referinta la splitter pentru a-l configura in OnShown
            _mainSplit = split;
        }

        // ============================================================
        // Pornirea / oprirea polling-ului
        // ============================================================
        private void StartPolling()
        {
            _cts = new CancellationTokenSource();
            _readCount = 0;
            _errorCount = 0;

            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _lblConn.Text = "● Conectare...";
            _lblConn.ForeColor = Color.DarkOrange;
            Log("Start polling.", Color.LightGreen);

            // Pornim bucla de polling pe un thread de fundal
            Task.Run(() => PollingLoop(_cts.Token));
        }

        private void StopPolling()
        {
            _cts?.Cancel();                 // semnalam thread-ului sa se opreasca
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _lblConn.Text = "● Oprit";
            _lblConn.ForeColor = Color.Gray;
            Log("Stop polling.", Color.Orange);
        }

        // Bucla principala de polling — ruleaza pe thread de fundal
        private async Task PollingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // Citim registrele din simulator (Holding sau Input)
                    ushort[] regs = _regType.Equals("Input", StringComparison.OrdinalIgnoreCase)
                        ? _reader.ReadInputRegisters(_startAddr, _count)
                        : _reader.ReadHoldingRegisters(_startAddr, _count);

                    // Construim lista de esantioane
                    var nowUtc = DateTime.UtcNow;
                    var samples = new List<RegisterSample>(regs.Length);
                    for (ushort i = 0; i < regs.Length; i++)
                    {
                        samples.Add(new RegisterSample
                        {
                            Timestamp = nowUtc,
                            Address = (ushort)(_startAddr + i),
                            Value = regs[i]
                        });
                    }

                    // Scriem fiecare esantion in SQLite
                    try
                    {
                        foreach (var s in samples)
                            _repo.Insert(_deviceId, s.Address, s.Value, s.Timestamp);
                    }
                    catch (Exception ex)
                    {
                        Log($"Eroare SQLite: {ex.Message}", Color.Salmon);
                    }

                    _readCount++;
                    _lastSampleAt = nowUtc;

                    // Actualizam UI-ul pe thread-ul principal (obligatoriu in WinForms)
                    BeginInvoke((Action)(() => UpdateUi(samples, sw.ElapsedMilliseconds)));
                }
                catch (Exception ex)
                {
                    // Eroare de comunicatie Modbus (simulator oprit, timeout, etc.)
                    _errorCount++;
                    BeginInvoke((Action)(() =>
                    {
                        _lblConn.Text = "● Eroare conexiune";
                        _lblConn.ForeColor = Color.Red;
                        Log($"Eroare Modbus: {ex.Message}", Color.Salmon);
                    }));
                }

                // Asteptam pana la urmatorul ciclu
                try { await Task.Delay(_intervalMs, ct); }
                catch (TaskCanceledException) { break; }
            }
        }

        // ============================================================
        // Actualizarea interfetei cu noile date
        // ============================================================
        private void UpdateUi(List<RegisterSample> samples, long elapsedMs)
        {
            // Status bar
            _lblConn.Text = "● Conectat";
            _lblConn.ForeColor = Color.Green;
            _lblReads.Text = $"Citiri: {_readCount}  (erori: {_errorCount})";
            _lblLast.Text = $"Ultima: {_lastSampleAt.ToLocalTime():HH:mm:ss}  ({elapsedMs} ms)";
            _lblDb.Text = $"SQLite: modbus.db  |  {_readCount * samples.Count} randuri";

            // Actualizam tabelul (grid)
            for (int i = 0; i < samples.Count && i < _grid.Rows.Count; i++)
            {
                var s = samples[i];
                _grid.Rows[i].Cells[0].Value = s.Address.ToString();
                _grid.Rows[i].Cells[1].Value = s.Value.ToString();
                _grid.Rows[i].Cells[2].Value = s.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            }

            // Actualizam graficul — adaugam un punct nou la fiecare serie
            var xValue = _lastSampleAt.ToLocalTime();
            for (int i = 0; i < _chart.Series.Count && i < samples.Count; i++)
            {
                var s = _chart.Series[i];
                s.Points.AddXY(xValue, samples[i].Value);

                // Limitam numarul de puncte (fereastra glisanta)
                while (s.Points.Count > _chartMaxPoints)
                    s.Points.RemoveAt(0);
            }

            // Re-scalam axa X ca sa arate doar ultimele N secunde
            if (_chart.Series.Count > 0 && _chart.Series[0].Points.Count > 0)
            {
                var first = _chart.Series[0].Points[0].XValue;
                _chart.ChartAreas[0].AxisX.Minimum = first;
                _chart.ChartAreas[0].AxisX.Maximum = _chart.Series[0].Points[_chart.Series[0].Points.Count - 1].XValue;
            }
        }

        // ============================================================
        // Logare in panoul de jos
        // ============================================================
        private void Log(string msg, Color color)
        {
            // Daca suntem pe alt thread, facem marshal inapoi pe UI thread
            if (_log.InvokeRequired)
            {
                _log.BeginInvoke((Action)(() => Log(msg, color)));
                return;
            }
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            _log.ScrollToCaret();
        }

        // ============================================================
        // Evenimente ale formei
        // ============================================================
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Initializam baza de date SQLite (creeaza tabela daca nu exista)
            try
            {
                var dbPath = DatabaseInitializer.EnsureDatabase();
                _lblDb.Text = $"SQLite: {dbPath}";
                Log($"Baza de date SQLite pregatita: {dbPath}", Color.LightBlue);
            }
            catch (Exception ex)
            {
                Log($"Eroare initializare SQLite: {ex.Message}", Color.Salmon);
                _lblDb.Text = "SQLite: EROARE";
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Configuram splitter-ul DUPA ce forma are dimensiuni reale
            // (altfel SplitterDistance arunca InvalidOperationException)
            if (_mainSplit != null)
            {
                // Setam limitele minime ale celor doua panouri
                _mainSplit.Panel1MinSize = 150;
                _mainSplit.Panel2MinSize = 250;

                // Impartim la 30% / 70%
                int desired = (int)(_mainSplit.Width * 0.30);

                // Ne asiguram ca valoarea e in limitele valide
                int minAllowed = _mainSplit.Panel1MinSize;
                int maxAllowed = _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth;

                if (desired < minAllowed) desired = minAllowed;
                if (desired > maxAllowed) desired = maxAllowed;

                _mainSplit.SplitterDistance = desired;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Oprim polling-ul inainte de inchiderea formei
            _cts?.Cancel();
            base.OnFormClosing(e);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(844, 573);
            this.Name = "MainForm";
            this.ResumeLayout(false);
        }
    }
}