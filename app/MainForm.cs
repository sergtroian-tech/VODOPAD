// Водопад — главное окно
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Vodopad
{
    public class MainForm : Form
    {
        static readonly string[] KNAMES = {"неизвестный", "MPU6050/6500/9250", "BMI160", "LIS3DH/LIS2DH/SC7A20", "LIS2DW12",
            "BMA250/253/280", "ADXL345", "MMA845x", "MMA7660", "QMI8658", "LSM6DS", "QMA7981/6100", "KXTJ3", "MSA301/DA2xx"};

        readonly Link link = new Link();
        ChipInfo chip;
        string chipPort;
        bool loading, busy, scanning, litClicked;
        readonly Dictionary<string, int> pending = new Dictionary<string, int>();
        readonly Timer debounce = new Timer { Interval = 180 };
        readonly Timer portWatch = new Timer { Interval = 1500 };
        string lastPorts = "";

        static string CurrentPortSet()
        {
            try { return string.Join(",", System.IO.Ports.SerialPort.GetPortNames().OrderBy(x => x)); } catch { return ""; }
        }

        // Следим за подключением: появился новый порт — это панель, выбираем его сами
        void WatchPorts()
        {
            if (busy || scanning) return;
            string now = CurrentPortSet();
            if (now == lastPorts) return;
            var before = new HashSet<string>(lastPorts.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            lastPorts = now;
            RefreshPorts();
            var added = now.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(p => !before.Contains(p)).ToList();
            if (added.Count > 0)
            {
                for (int i = 0; i < cbPorts.Items.Count; i++)
                    if (((PortInfo)cbPorts.Items[i]).Port == added[0]) { cbPorts.SelectedIndex = i; break; }
                Status("Появился новый порт " + added[0] + " — похоже, это панель.");
            }
        }

        void CheckSecondInstance()
        {
            try
            {
                var me = System.Diagnostics.Process.GetCurrentProcess();
                if (System.Diagnostics.Process.GetProcessesByName(me.ProcessName).Length > 1)
                    Msg("Программа «Водопад» уже открыта в другом окне. Закройте лишние окна — иначе они мешают друг другу работать с портом.", MessageBoxIcon.Warning);
            }
            catch { }
        }

        TabControl tabs;
        TabPage tabConn, tabFlash, tabSetup, tabLook;
        TextBox logBox;
        ProgressBar progress;
        Label status;

        // вкладка 1
        ComboBox cbPorts; Label lblDevice, lblHints, lblVersion; Button btnRefresh, btnDetect, btnUpdate;
        const string PanelFwVersion = "1.0";   // версия прошивки, встроенной в эту программу
        bool fwAsked;
        // вкладка 2
        Button btnBackup, btnFlash, btnRestore, btnOpenFolder; CheckBox chkErase; Label lblBackup;
        // вкладка 3
        Button btnFindLed, btnLit, btnStopLed, btnManualLed, btnFindSensor, btnOrient, btnRgb, btnCalUp, btnCalFlat, btnDone;
        Label lblLedTry, lblLed, lblSensor, lblCal;
        NumericUpDown numManual, numW, numH;
        ComboBox cbRot, cbOrder; CheckBox chkMir, chkSerp;
        // вкладка 4
        ComboBox cbMode; TrackBar tbBri, tbFill, tbSpeed, tbSplash; Panel swatch; Button btnColor, btnSplash, btnDefaults;
        int hue = 140, sat = 235;

        public MainForm()
        {
            Text = "Водопад — прошивка LED-панели  (версия " + Updater.CurrentText + ")";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(820, 720);
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            link.Log = Log;
            BuildUi();
            debounce.Tick += (s, e) => FlushPending();
            Load += async (s, e) => { Updater.Cleanup(); RefreshPorts(); lastPorts = CurrentPortSet(); portWatch.Start(); CheckSecondInstance(); await CheckUpdates(true); };
            portWatch.Tick += (s, e) => WatchPorts();
            FormClosing += (s, e) => { scanning = false; try { if (link.IsOpen) { link.Cmd("RUN", "OK", 500); link.Close(); } } catch { } };
        }

        // ----------------------------------------------------------- интерфейс
        static Label L(string text, bool bold = false, int width = 740)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(3, 4, 3, 4),
                Font = bold ? new Font("Segoe UI", 10.5f, FontStyle.Bold) : null };
        }
        static Button B(string text, EventHandler h, int w = 0)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3) };
            if (w > 0) { b.AutoSize = false; b.Width = w; b.Height = 34; }
            b.Click += h; return b;
        }
        static FlowLayoutPanel Row(params Control[] cs)
        {
            var r = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 2), MaximumSize = new Size(760, 0) };
            r.Controls.AddRange(cs); return r;
        }
        static FlowLayoutPanel Page(TabPage tp)
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            tp.Controls.Add(f); return f;
        }
        static Control Group(string title, params Control[] cs)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 10, 0, 4), Padding = new Padding(8, 4, 8, 8), BackColor = Color.FromArgb(250, 251, 253), BorderStyle = BorderStyle.FixedSingle, MinimumSize = new Size(740, 0) };
            var h = L(title, true);
            h.ForeColor = Color.FromArgb(20, 60, 110);
            f.Controls.Add(h);
            f.Controls.AddRange(cs);
            return f;
        }
        static TrackBar TB(int min, int max, int tick)
        {
            return new TrackBar { Minimum = min, Maximum = max, TickFrequency = tick, Width = 430, AutoSize = false, Height = 36 };
        }

        void BuildUi()
        {
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabConn = new TabPage("1. Подключение"); tabFlash = new TabPage("2. Прошивка");
            tabSetup = new TabPage("3. Настройка панели"); tabLook = new TabPage("4. Водопад");
            tabs.TabPages.AddRange(new[] { tabConn, tabFlash, tabSetup, tabLook });
            tabs.SelectedIndexChanged += async (s, e) => await OnTab();
            Shown += (s, e) => { foreach (var l in new[] { lblLedTry, lblLed, lblSensor, lblCal, lblBackup, lblDevice }) l.MinimumSize = new Size(700, 0); };

            // ---- 1. Подключение
            var p1 = Page(tabConn);
            p1.Controls.Add(L("Подключите панель к компьютеру кабелем USB-C, выберите порт и нажмите «Определить чип».", true));
            cbPorts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 };
            btnRefresh = B("Обновить список", (s, e) => RefreshPorts());
            btnDetect = B("Определить чип", async (s, e) => await DoDetect());
            p1.Controls.Add(Row(cbPorts, btnRefresh, btnDetect));
            lblDevice = L("Чип ещё не определён.", true);
            lblDevice.ForeColor = Color.FromArgb(0, 90, 60);
            p1.Controls.Add(lblDevice);
            lblHints = L("");
            lblHints.ForeColor = Color.FromArgb(150, 70, 0);
            p1.Controls.Add(lblHints);
            p1.Controls.Add(L(" "));
            lblVersion = L("Версия программы: " + Updater.CurrentText);
            btnUpdate = B("Проверить обновления", async (s, e) => await CheckUpdates(false));
            p1.Controls.Add(Row(lblVersion, btnUpdate));

            // ---- 2. Прошивка
            var p2 = Page(tabFlash);
            p2.Controls.Add(L("Шаг 1. Сохраните родную прошивку — тогда всегда можно будет вернуть как было (водопад + Love + сердечко).", true));
            btnBackup = B("Сохранить родную прошивку", async (s, e) => await DoBackup(), 330);
            btnOpenFolder = B("Открыть папку с копиями", (s, e) => { Directory.CreateDirectory(Paths.BackupDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.BackupDir + "\""); });
            p2.Controls.Add(Row(btnBackup, btnOpenFolder));
            lblBackup = L("Копия ещё не сделана."); lblBackup.MinimumSize = new Size(700, 0);
            p2.Controls.Add(lblBackup);
            p2.Controls.Add(L(" "));
            p2.Controls.Add(L("Шаг 2. Прошейте «Водопад» — останется только водопад, без надписи и сердечка.", true));
            chkErase = new CheckBox { Text = "Полностью очистить память перед прошивкой (нужно при первой прошивке)", AutoSize = true, Checked = true };
            p2.Controls.Add(chkErase);
            btnFlash = B("Прошить «Водопад»", async (s, e) => await DoFlash(false), 330);
            btnFlash.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            p2.Controls.Add(btnFlash);
            p2.Controls.Add(L(" "));
            p2.Controls.Add(L("Вернуть всё как было:", true));
            btnRestore = B("Вернуть родную прошивку…", async (s, e) => await DoRestore(), 330);
            p2.Controls.Add(btnRestore);

            // ---- 3. Настройка панели
            var p3 = Page(tabSetup);
            p3.Controls.Add(L("После первой прошивки нужно один раз показать программе, как устроена панель. Всё запоминается в самой панели.", false));

            lblLedTry = L("Нажмите «Начать поиск» и смотрите на панель.", true);
            lblLedTry.ForeColor = Color.FromArgb(0, 70, 140); lblLedTry.MinimumSize = new Size(700, 0);
            btnFindLed = B("Начать поиск", async (s, e) => await FindLed());
            btnLit = B("Загорелось!", (s, e) => { litClicked = true; });
            btnLit.Enabled = false; btnLit.BackColor = Color.FromArgb(200, 245, 210);
            btnStopLed = B("Стоп", (s, e) => { scanning = false; });
            btnStopLed.Enabled = false;
            numManual = new NumericUpDown { Minimum = 0, Maximum = 48, Width = 60 };
            btnManualLed = B("Задать вручную", async (s, e) => await ChooseLed((int)numManual.Value));
            lblLed = L("Выход на светодиоды: не найден"); lblLed.MinimumSize = new Size(700, 0);
            p3.Controls.Add(Group("Шаг 1. Найти, куда подключены светодиоды",
                L("Программа по очереди включает выходы платы. Как только панель засветится (тускло, зелёным) — жмите «Загорелось!»."),
                Row(btnFindLed, btnLit, btnStopLed), lblLedTry,
                Row(L("Номер GPIO, если знаете:"), numManual, btnManualLed), lblLed));

            btnFindSensor = B("Найти датчик", async (s, e) => await FindSensor());
            lblSensor = L("Датчик: не выбран"); lblSensor.MinimumSize = new Size(700, 0);
            p3.Controls.Add(Group("Шаг 2. Найти датчик движения",
                L("Сначала ищется акселерометр. Если его нет — программа попросит потрясти панель, чтобы найти датчик тряски."),
                btnFindSensor, lblSensor));

            btnOrient = B("Показать стрелку", async (s, e) => await Send("ORIENT"));
            cbRot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            cbRot.Items.AddRange(new object[] { "0°", "90°", "180°", "270°" });
            cbRot.SelectedIndexChanged += (s, e) => Queue("rot", cbRot.SelectedIndex);
            chkMir = new CheckBox { Text = "Зеркально", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
            chkMir.CheckedChanged += (s, e) => Queue("mir", chkMir.Checked ? 1 : 0);
            chkSerp = new CheckBox { Text = "Змейка", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
            chkSerp.CheckedChanged += (s, e) => Queue("serp", chkSerp.Checked ? 1 : 0);
            numW = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 16, Width = 55 };
            numH = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 16, Width = 55 };
            numW.ValueChanged += (s, e) => Queue("w", (int)numW.Value);
            numH.ValueChanged += (s, e) => Queue("h", (int)numH.Value);
            btnRgb = B("Проверить цвета", async (s, e) => await Send("RGBTEST"));
            cbOrder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cbOrder.Items.AddRange(new object[] { "GRB (обычно)", "RGB", "BRG", "RBG", "GBR", "BGR" });
            cbOrder.SelectedIndexChanged += (s, e) => Queue("order", cbOrder.SelectedIndex);
            p3.Controls.Add(Group("Шаг 3. Ориентация картинки",
                L("Нажмите «Показать стрелку». Добейтесь, чтобы белая стрелка смотрела вверх (к кнопке), а красная точка была в левом верхнем углу. Если стрелка «рваная» — переключите «Змейка»."),
                Row(btnOrient, L("Поворот:"), cbRot, chkMir, chkSerp),
                Row(L("Размер панели:"), numW, L("×"), numH, L("светодиодов")),
                L("Цвета: нажмите «Проверить цвета» — слева должен быть красный, в центре зелёный, справа синий. Если не так — меняйте порядок:"),
                Row(btnRgb, cbOrder)));

            btnCalUp = B("1) Стоит стрелкой вверх — запомнить", async (s, e) => await Cal(true));
            btnCalFlat = B("2) Лежит экраном вверх — запомнить", async (s, e) => await Cal(false));
            lblCal = L("Датчик не откалиброван."); lblCal.MinimumSize = new Size(700, 0);
            p3.Controls.Add(Group("Шаг 4. Научить датчик, где верх (только для акселерометра)",
                L("Нажмите «Показать стрелку». Поставьте панель вертикально, чтобы стрелка смотрела вверх, и нажмите кнопку 1. Потом положите панель на стол экраном вверх и нажмите кнопку 2."),
                Row(btnCalUp, btnCalFlat), lblCal));

            btnDone = B("Готово — запустить водопад", async (s, e) => { await Send("SAVE"); await Send("RUN"); tabs.SelectedTab = tabLook; }, 330);
            btnDone.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            p3.Controls.Add(btnDone);

            // ---- 4. Водопад
            var p4 = Page(tabLook);
            p4.Controls.Add(L("Изменения сразу видны на панели и сохраняются в ней.", false));
            cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            cbMode.Items.AddRange(new object[] { "Вода переливается по наклону (как было)", "Струи водопада" });
            cbMode.SelectedIndexChanged += (s, e) => Queue("mode", cbMode.SelectedIndex);
            p4.Controls.Add(Row(L("Режим:", false, 160), cbMode));

            swatch = new Panel { Width = 60, Height = 30, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 4, 3, 3) };
            btnColor = B("Выбрать цвет…", (s, e) => PickColor());
            var presets = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            AddPreset(presets, "Как было", 140, 235); AddPreset(presets, "Бирюза", 170, 235); AddPreset(presets, "Голубой", 195, 230);
            AddPreset(presets, "Синий", 225, 240); AddPreset(presets, "Фиолет", 275, 220); AddPreset(presets, "Лава", 8, 255);
            p4.Controls.Add(Row(L("Цвет воды:", false, 160), swatch, btnColor));
            p4.Controls.Add(Row(L("", false, 160), presets));

            tbBri = TB(2, 255, 25); tbBri.ValueChanged += (s, e) => Queue("bri", tbBri.Value);
            p4.Controls.Add(Row(L("Яркость:", false, 160), tbBri));
            tbFill = TB(5, 95, 10); tbFill.ValueChanged += (s, e) => Queue("fill", tbFill.Value);
            p4.Controls.Add(Row(L("Сколько воды:", false, 160), tbFill));
            tbSpeed = TB(1, 10, 1); tbSpeed.ValueChanged += (s, e) => Queue("speed", tbSpeed.Value);
            p4.Controls.Add(Row(L("Скорость:", false, 160), tbSpeed));
            tbSplash = TB(0, 100, 10); tbSplash.ValueChanged += (s, e) => Queue("splash", tbSplash.Value);
            p4.Controls.Add(Row(L("Брызги при встряхивании:", false, 160), tbSplash));
            btnSplash = B("Брызги — проверить", async (s, e) => await Send("SPLASH"));
            btnDefaults = B("Сбросить настройки вида", async (s, e) => { await Send("DEFAULTS"); await Send("SAVE"); await LoadCfg(); });
            p4.Controls.Add(Row(btnSplash, btnDefaults));

            // ---- низ: прогресс + журнал
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(6) };
            status = new Label { Dock = DockStyle.Top, Height = 24, Text = "Готово к работе." };
            progress = new ProgressBar { Dock = DockStyle.Top, Height = 16 };
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(245, 247, 250) };
            bottom.Controls.Add(logBox); bottom.Controls.Add(progress); bottom.Controls.Add(status);
            Controls.Add(tabs);
            Controls.Add(bottom);

            // значения по умолчанию, пока панель не подключена
            loading = true;
            cbMode.SelectedIndex = 0; cbRot.SelectedIndex = 0; cbOrder.SelectedIndex = 0;
            tbBri.Value = 40; tbFill.Value = 55; tbSpeed.Value = 6; tbSplash.Value = 30;
            SetColor(140, 235, false);
            loading = false;
        }

        void AddPreset(FlowLayoutPanel host, string name, int h, int s)
        {
            var c = HsvToColor(h, s / 255.0, 1.0);
            var b = new Button { Text = name, AutoSize = true, BackColor = Color.FromArgb(255, (c.R + 510) / 3, (c.G + 510) / 3, (c.B + 510) / 3), Margin = new Padding(2) };
            b.Click += (o, e) => SetColor(h, s, true);
            host.Controls.Add(b);
        }

        // ----------------------------------------------------------- служебное
        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(30000);
            logBox.AppendText(s + Environment.NewLine);
        }
        void Status(string s) { if (InvokeRequired) { BeginInvoke(new Action<string>(Status), s); return; } status.Text = s; Log("— " + s); }
        void Progress(int v) { if (InvokeRequired) { BeginInvoke(new Action<int>(Progress), v); return; } progress.Value = Math.Max(0, Math.Min(100, v)); }
        void Msg(string s, MessageBoxIcon icon = MessageBoxIcon.Information) { MessageBox.Show(this, s, "Водопад", MessageBoxButtons.OK, icon); }
        bool Ask(string s) { return MessageBox.Show(this, s, "Водопад", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        void SetBusy(bool b)
        {
            busy = b;
            UseWaitCursor = b;
            foreach (var c in new Control[] { btnDetect, btnRefresh, btnUpdate, btnBackup, btnFlash, btnRestore, btnFindLed, btnFindSensor, btnCalUp, btnCalFlat, btnDone, cbPorts })
                c.Enabled = !b;
        }

        string SelectedPort() { var p = cbPorts.SelectedItem as PortInfo; return p != null ? p.Port : null; }

        void RefreshPorts()
        {
            var list = Ports.List();
            cbPorts.Items.Clear();
            foreach (var p in list) cbPorts.Items.Add(p);
            if (cbPorts.Items.Count > 0) cbPorts.SelectedIndex = 0;
            var hints = Ports.UsbHints();
            bool anyUsb = list.Any(p => p.Score >= 3);
            if (!anyUsb)
            {
                hints.Insert(0, (list.Count == 0 ? "Windows не видит панель как COM-порт." : "Среди портов нет USB-порта панели (" + string.Join(", ", list.Select(x => x.Port)) + " — это не она).") + "\n" +
                    "Программа сама заметит панель, когда она появится. Что попробовать:\n" +
                    "• другой кабель — многие кабели USB-C только для зарядки;\n" +
                    "• зажать кнопку на панели и, не отпуская, подключить кабель — так плата входит в режим прошивки;\n" +
                    "• если в Диспетчере устройств есть «Неизвестное устройство» — нужен драйвер (CH340 или CP210x);\n" +
                    "• если ничего не помогает — возможно, USB на панели только для зарядки, и плату прошивают только со вскрытием корпуса.");
            }
            lblHints.Text = string.Join("\n\n", hints);
            Log("Найдено портов: " + list.Count + (list.Count > 0 ? " (" + string.Join(", ", list.Select(x => x.Port)) + ")" : ""));
        }

        // ----------------------------------------------------------- автообновление
        async Task CheckUpdates(bool auto)
        {
            if (busy || scanning) return;
            btnUpdate.Enabled = false;
            try
            {
                Status("Проверяю обновления…");
                Version latest = null;
                try { latest = await Task.Run(() => Updater.GetLatest()); }
                catch (Exception e)
                {
                    Status(auto ? "Готово к работе." : "Не удалось проверить обновления");
                    Log("Обновления: " + e.Message);
                    if (!auto) Msg("Не получилось связаться с GitHub. Проверьте интернет и попробуйте позже.", MessageBoxIcon.Warning);
                    return;
                }
                if (latest <= Updater.Current)
                {
                    lblVersion.Text = "Версия программы: " + Updater.CurrentText + " — последняя ✓";
                    Status("Готово к работе. У вас последняя версия.");
                    if (!auto) Msg("У вас последняя версия (" + Updater.CurrentText + ").");
                    return;
                }
                string lv = latest.Build > 0 ? latest.ToString(3) : latest.ToString(2);
                Status("Скачиваю новую версию " + lv + "…");
                SetBusy(true);
                try { await Task.Run(() => Updater.Download(Progress)); }
                catch (Exception e)
                {
                    Status("Обновление не скачалось");
                    Log("Обновление: " + e.Message);
                    Msg("Не получилось обновить программу: " + e.Message + "\n\nРаботаю на текущей версии.", MessageBoxIcon.Warning);
                    return;
                }
                finally { SetBusy(false); Progress(0); }
                link.Close();
                Msg("Программа обновлена до версии " + lv + ". Сейчас она перезапустится.");
                Updater.Restart();
            }
            finally { btnUpdate.Enabled = true; }
        }

        // В панели старая прошивка «Водопад» — предлагаем обновить (настройки сохранятся)
        async Task CheckPanelFw(string ping)
        {
            if (fwAsked || ping == null) return;
            var a = ping.Split(' ');
            if (a.Length < 4) return;
            Version have, need;
            if (!Version.TryParse(a[2], out have) || !Version.TryParse(PanelFwVersion, out need) || have >= need) return;
            fwAsked = true;
            if (!Ask("В панели прошивка «Водопад» версии " + a[2] + ", а в программе есть новее — " + PanelFwVersion + ".\n\nОбновить прошивку панели? Все настройки сохранятся.")) return;
            link.Close();
            tabs.SelectedTab = tabFlash;
            await DoFlash(true);
        }

        // ----------------------------------------------------------- 1. определение
        async Task<bool> DoDetect()
        {
            string port = SelectedPort();
            if (port == null) { Msg("Сначала подключите панель и нажмите «Обновить список»."); return false; }
            var sel = cbPorts.SelectedItem as PortInfo;
            if (sel != null && sel.Score == 0 && !Ask(port + " — это Bluetooth-порт, а не панель.\n\nВсё равно попробовать?")) return false;
            SetBusy(true);
            try
            {
                link.Close();
                Status("Определяю чип на " + port + "…");
                var info = await Task.Run(() => EspTool.Detect(port, Log));
                if (info.Error != null) { Status("Чип не определён"); Msg(info.Error, MessageBoxIcon.Warning); return false; }
                chip = info; chipPort = port;
                string mb = info.FlashBytes > 0 ? (info.FlashBytes / 1048576.0).ToString("0.#") + " МБ" : "?";
                if (info.Key == "")
                {
                    lblDevice.Text = "Чип: " + info.Text + "\nЭтот чип пока не поддерживается прошивкой «Водопад» — напишите Claude, добавлю.";
                    Status("Чип не поддерживается"); return false;
                }
                lblDevice.Text = "Найдено: " + info.Text + "\nMAC: " + info.Mac + "   Память: " + mb;
                Status("Найден " + info.Text);
                UpdateBackupLabel();
                tabs.SelectedTab = tabFlash;
                return true;
            }
            finally { SetBusy(false); Progress(0); }
        }

        string ExistingBackup()
        {
            if (chip == null || chip.Mac == "" || !Directory.Exists(Paths.BackupDir)) return null;
            return Directory.GetFiles(Paths.BackupDir, "*_" + chip.MacShort + "_*.bin").OrderByDescending(f => f).FirstOrDefault();
        }

        void UpdateBackupLabel()
        {
            string f = ExistingBackup();
            lblBackup.Text = f != null ? "✓ Копия есть: " + f : "Копия ещё не сделана.";
            lblBackup.ForeColor = f != null ? Color.FromArgb(0, 110, 50) : Color.FromArgb(160, 60, 0);
        }

        async Task<bool> NeedChip()
        {
            if (chip != null && chipPort == SelectedPort()) return true;
            return await DoDetect();
        }

        // ----------------------------------------------------------- 2. прошивка
        async Task DoBackup()
        {
            if (!await NeedChip()) return;
            string port = SelectedPort();
            Directory.CreateDirectory(Paths.BackupDir);
            string file = Path.Combine(Paths.BackupDir, "Родная_" + chip.Key + "_" + chip.MacShort + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".bin");
            string size = chip.FlashBytes > 0 ? "0x" + chip.FlashBytes.ToString("X") : "ALL";
            SetBusy(true);
            try
            {
                link.Close();
                Status("Сохраняю родную прошивку (1–3 минуты)…");
                int rc = await Task.Run(() => EspTool.Run("--chip " + chip.Key + " --port " + port + " --baud 460800 --before default_reset --after hard_reset read_flash 0 " + size + " \"" + file + "\"", Log, Progress));
                bool ok = rc == 0 && File.Exists(file) && (chip.FlashBytes == 0 || new FileInfo(file).Length == chip.FlashBytes);
                if (!ok)
                {
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                    Status("Копия не сохранилась"); Msg("Не получилось прочитать память платы. Посмотрите журнал внизу и попробуйте ещё раз (можно с зажатой кнопкой BOOT при подключении).", MessageBoxIcon.Warning);
                    return;
                }
                Status("Копия сохранена");
                UpdateBackupLabel();
                Msg("Родная прошивка сохранена:\n" + file + "\n\nТеперь можно прошивать «Водопад».");
            }
            finally { SetBusy(false); Progress(0); }
        }

        async Task DoFlash(bool update = false)
        {
            if (!await NeedChip()) return;
            if (chip.Key == "") return;
            if (!update && ExistingBackup() == null && !Ask("Копии родной прошивки нет — без неё не получится вернуть надпись Love и сердечко.\n\nВсё равно прошить?")) return;
            string port = SelectedPort();
            string erase = (chkErase.Checked && !update) ? "-e " : "";
            string args;
            if (chip.Key == "esp8266")
                args = "--chip esp8266 --port " + port + " --baud 460800 --before default_reset --after hard_reset write_flash " + erase +
                       "-z --flash_mode dout --flash_size detect 0x0 \"" + Paths.Fw("esp8266", "app.bin") + "\"";
            else
                args = "--chip " + chip.Key + " --port " + port + " --baud 460800 --before default_reset --after hard_reset write_flash " + erase +
                       "-z --flash_mode dio --flash_size detect " + chip.BootOffset + " \"" + Paths.Fw(chip.Key, "boot.bin") + "\" 0x8000 \"" +
                       Paths.Fw(chip.Key, "part.bin") + "\" 0x10000 \"" + Paths.Fw(chip.Key, "app.bin") + "\"";
            SetBusy(true);
            try
            {
                link.Close();
                Status("Прошиваю «Водопад»…");
                int rc = await Task.Run(() => EspTool.Run(args, Log, Progress));
                if (rc != 0) { Status("Ошибка прошивки"); Msg("Прошивка не записалась. Посмотрите журнал внизу. Попробуйте ещё раз, при необходимости — подключив панель с зажатой кнопкой BOOT.", MessageBoxIcon.Warning); return; }
                Status("Прошито! Жду перезапуска панели…");
                Progress(100);
                await WaitPort(port, 10000);
                await Task.Delay(1500);
                var r = await Task.Run(() => link.Connect(port, 8000));
                if (r == null)
                {
                    Msg("Прошивка записана, но панель пока не отвечает.\nОтключите и снова подключите кабель, затем откройте вкладку «3. Настройка панели».");
                    return;
                }
                Log("Связь: " + r);
                var cfg = await LoadCfg();
                Status("«Водопад» работает");
                if (cfg != null && cfg.ContainsKey("led") && cfg["led"] == "-1")
                {
                    tabs.SelectedTab = tabSetup;
                    Msg("Готово! Теперь один раз найдём, куда на плате подключены светодиоды.\nНажмите «Начать поиск» и смотрите на панель.");
                }
                else tabs.SelectedTab = tabLook;
            }
            finally { SetBusy(false); Progress(0); }
        }

        async Task WaitPort(string port, int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(800);
            while (sw.ElapsedMilliseconds < ms)
            {
                if (System.IO.Ports.SerialPort.GetPortNames().Contains(port)) return;
                await Task.Delay(300);
            }
        }

        async Task DoRestore()
        {
            string port = SelectedPort();
            if (port == null) { Msg("Выберите порт на вкладке «Подключение»."); return; }
            Directory.CreateDirectory(Paths.BackupDir);
            var dlg = new OpenFileDialog { InitialDirectory = Paths.BackupDir, Filter = "Копия прошивки (*.bin)|*.bin", Title = "Какую прошивку вернуть?" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (!Ask("Записать в панель:\n" + dlg.FileName + "\n\nПанель вернётся к родной прошивке. Продолжить?")) return;
            SetBusy(true);
            try
            {
                link.Close();
                Status("Возвращаю родную прошивку…");
                int rc = await Task.Run(() => EspTool.Run("--port " + port + " --baud 460800 --before default_reset --after hard_reset write_flash -z --flash_size keep 0x0 \"" + dlg.FileName + "\"", Log, Progress));
                if (rc == 0) { Status("Родная прошивка возвращена"); Msg("Готово — родная прошивка возвращена."); }
                else { Status("Ошибка"); Msg("Не получилось записать. Посмотрите журнал.", MessageBoxIcon.Warning); }
            }
            finally { SetBusy(false); Progress(0); }
        }

        // ----------------------------------------------------------- связь с «Водопадом»
        Task<bool> connecting;
        async Task<bool> EnsureLink(bool quiet = false)
        {
            if (connecting != null) return await connecting;
            connecting = EnsureLinkCore(quiet);
            try { return await connecting; } finally { connecting = null; }
        }

        async Task<bool> EnsureLinkCore(bool quiet)
        {
            if (link.IsOpen)
            {
                var pr = await Task.Run(() => link.Cmd("PING", "OK VODOPAD", 800));
                if (pr != null) return true;
            }
            string port = SelectedPort();
            if (port == null) { if (!quiet) Msg("Сначала выберите порт на вкладке «1. Подключение»."); return false; }
            Status("Подключаюсь к панели на " + port + "…");
            var r = await Task.Run(() => link.Connect(port, 6000));
            if (r == null)
            {
                Status("Панель не отвечает");
                if (!quiet) Msg("Панель не отвечает как «Водопад».\nЕсли ещё не прошивали — сначала вкладка «2. Прошивка».", MessageBoxIcon.Warning);
                return false;
            }
            Status("Связь есть: " + r);
            await LoadCfg();
            var ping = r;
            BeginInvoke(new Action(async () => await CheckPanelFw(ping)));
            return true;
        }

        async Task<string> Send(string cmd, string expect = "OK", int timeout = 2000)
        {
            if (!await EnsureLink()) return null;
            var r = await Task.Run(() => link.Cmd(cmd, expect, timeout));
            Log(cmd + " → " + (r ?? "нет ответа"));
            return r;
        }

        void Queue(string key, int val)
        {
            if (loading) return;
            pending[key] = val;
            debounce.Stop(); debounce.Start();
        }

        async void FlushPending()
        {
            debounce.Stop();
            if (pending.Count == 0 || scanning) return;
            if (!await EnsureLink(true)) { pending.Clear(); return; }
            var items = pending.ToList(); pending.Clear();
            await Task.Run(() =>
            {
                foreach (var kv in items) link.Cmd("SET " + kv.Key + " " + kv.Value, "OK", 1500);
                link.Cmd("SAVE", "OK", 1500);
            });
            Log("Сохранено: " + string.Join(", ", items.Select(kv => kv.Key + "=" + kv.Value)));
        }

        async Task<Dictionary<string, string>> LoadCfg()
        {
            var cfg = await Task.Run(() => link.GetCfg());
            if (cfg == null) return null;
            loading = true;
            try
            {
                Func<string, int, int> I = (k, d) => { string v; int x; return cfg.TryGetValue(k, out v) && int.TryParse(v, out x) ? x : d; };
                int led = I("led", -1);
                lblLed.Text = led >= 0 ? "✓ Светодиоды подключены к GPIO" + led : "Выход на светодиоды: не найден";
                cbRot.SelectedIndex = Math.Max(0, Math.Min(3, I("rot", 0)));
                chkMir.Checked = I("mir", 0) == 1; chkSerp.Checked = I("serp", 0) == 1;
                numW.Value = Math.Max(1, Math.Min(32, I("w", 16))); numH.Value = Math.Max(1, Math.Min(32, I("h", 16)));
                cbOrder.SelectedIndex = Math.Max(0, Math.Min(5, I("order", 0)));
                cbMode.SelectedIndex = Math.Max(0, Math.Min(1, I("mode", 0)));
                tbBri.Value = Math.Max(tbBri.Minimum, Math.Min(tbBri.Maximum, I("bri", 40)));
                tbFill.Value = Math.Max(tbFill.Minimum, Math.Min(tbFill.Maximum, I("fill", 55)));
                tbSpeed.Value = Math.Max(1, Math.Min(10, I("speed", 6)));
                tbSplash.Value = Math.Max(0, Math.Min(100, I("splash", 30)));
                SetColor(I("hue", 140), I("sat", 235), false);
                int sens = I("sens", 0), kind = I("kind", 0);
                if (sens == 1) lblSensor.Text = "✓ Акселерометр " + KNAMES[Math.Max(0, Math.Min(KNAMES.Length - 1, kind))] + " (SDA=GPIO" + I("sda", -1) + ", SCL=GPIO" + I("scl", -1) + ")" + (I("sready", 0) == 1 ? "" : " — сейчас не отвечает");
                else if (sens == 2) lblSensor.Text = "✓ Датчик тряски на GPIO" + I("sw", -1);
                else lblSensor.Text = "Датчик: не выбран";
                lblCal.Text = I("cal", 0) == 1 ? "✓ Датчик откалиброван" : "Датчик не откалиброван.";
            }
            finally { loading = false; }
            return cfg;
        }

        async Task OnTab()
        {
            if (busy || scanning) return;
            if (tabs.SelectedTab == tabSetup || tabs.SelectedTab == tabLook)
            {
                if (await EnsureLink(true) && tabs.SelectedTab == tabLook) await Task.Run(() => link.Cmd("RUN", "OK", 1000));
            }
        }

        // ----------------------------------------------------------- 3. мастер настройки
        async Task FindLed()
        {
            if (!await EnsureLink()) return;
            var r = await Task.Run(() => link.Cmd("LEDPINS", "PINS", 1500));
            if (r == null || r.StartsWith("ERR")) { Msg("Панель не ответила списком выходов."); return; }
            var pins = r.Substring(4).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            scanning = true; litClicked = false;
            SetBusy(true);
            btnLit.Enabled = true; btnStopLed.Enabled = true;
            int i = 0;
            try
            {
                for (; i < pins.Count && scanning; i++)
                {
                    int p = pins[i];
                    lblLedTry.Text = "Проверяю выход GPIO" + p + "  (" + (i + 1) + " из " + pins.Count + ")…  Засветилось — жмите «Загорелось!»";
                    await Task.Run(() => link.Cmd("TRYLED " + p, "OK TRY", 2000));
                    for (int t = 0; t < 20 && !litClicked && scanning; t++) await Task.Delay(100);
                    if (litClicked) break;
                }
            }
            finally { btnLit.Enabled = false; btnStopLed.Enabled = false; }
            bool lit = litClicked;
            scanning = false;
            SetBusy(false);
            if (!lit)
            {
                lblLedTry.Text = "Поиск остановлен.";
                if (i >= pins.Count) Msg("Проверены все выходы, но панель не засветилась.\nВозможно, светодиоды подключены к выходу, который нельзя трогать без риска, или панель питается отдельно. Напишите Claude номер платы/фото платы изнутри.", MessageBoxIcon.Warning);
                await Task.Run(() => link.Cmd("RUN", "OK", 1000));
                return;
            }
            // уточняем: вдруг нажали, когда уже переключилось на следующий
            for (int k = Math.Min(i, pins.Count - 1); k >= 0; k--)
            {
                int p = pins[k];
                await Task.Run(() => link.Cmd("TRYLED " + p, "OK TRY", 2000));
                var ans = MessageBox.Show(this, "Сейчас включён выход GPIO" + p + ".\n\nПанель светится?", "Водопад", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) { await Task.Run(() => link.Cmd("RUN", "OK", 1000)); return; }
                if (ans == DialogResult.Yes) { await ChooseLed(p); return; }
            }
            Msg("Не получилось поймать нужный выход. Запустите поиск ещё раз.");
        }

        async Task ChooseLed(int p)
        {
            if (!await EnsureLink()) return;
            var r = await Task.Run(() => link.Cmd("SET led " + p, "OK", 1500));
            if (r == null || r.StartsWith("ERR")) { Msg("Этот выход нельзя использовать на этом чипе."); return; }
            await Task.Run(() => link.Cmd("SAVE", "OK", 1500));
            lblLed.Text = "✓ Светодиоды подключены к GPIO" + p;
            lblLedTry.Text = "Найдено: GPIO" + p;
            await Task.Run(() => link.Cmd("ORIENT", "OK", 1500));
            Status("Светодиоды на GPIO" + p);
            Msg("Отлично, светодиоды на GPIO" + p + ".\n\nТеперь на панели стрелка. Перейдите к шагу 2 (датчик) и шагу 3 (ориентация).");
        }

        async Task FindSensor()
        {
            if (!await EnsureLink()) return;
            SetBusy(true);
            try
            {
                Status("Ищу акселерометр (до минуты)…");
                var lines = await Task.Run(() => link.CmdMulti("SCANI2C", "END", 120000));
                var found = new List<int[]>();
                foreach (var l in lines.Where(x => x.StartsWith("I2C ")))
                {
                    Log(l);
                    var a = l.Split(' ');
                    try { found.Add(new[] { int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]) }); } catch { }
                }
                var good = found.FirstOrDefault(f => f[3] > 0);
                if (good != null)
                {
                    var r = await Task.Run(() => link.Cmd("USEI2C " + good[0] + " " + good[1] + " " + good[2], "OK SENSOR", 3000));
                    if (r != null && r.StartsWith("OK"))
                    {
                        await Task.Run(() => link.Cmd("SAVE", "OK", 1500));
                        await LoadCfg();
                        Status("Найден акселерометр " + KNAMES[good[3]]);
                        Msg("Найден акселерометр " + KNAMES[good[3]] + ".\n\nТеперь шаг 4: научите его, где верх.");
                        return;
                    }
                }
                if (found.Count > 0)
                    Log("На шине I2C есть устройства, но тип не распознан: " + string.Join(", ", found.Select(f => "0x" + f[2].ToString("X2"))));
                Msg((found.Count > 0 ? "Нашлось устройство на I2C (адрес 0x" + found[0][2].ToString("X2") + "), но это незнакомый датчик. Напишите Claude этот адрес — добавлю.\n\n" : "Акселерометр не найден.\n\n") +
                    "Сейчас поищем датчик тряски: после нажатия OK 6 секунд трясите панель.");
                Status("Трясите панель…");
                var sw = await Task.Run(() => link.CmdMulti("SCANSW 6000", "END", 15000));
                int bestPin = -1, best = 0;
                foreach (var l in sw.Where(x => x.StartsWith("SW ")))
                {
                    Log(l);
                    var a = l.Split(' ');
                    int pin, cnt;
                    if (int.TryParse(a[1], out pin) && int.TryParse(a[2], out cnt) && cnt > best) { best = cnt; bestPin = pin; }
                }
                if (bestPin >= 0 && best >= 3)
                {
                    await Task.Run(() => link.Cmd("USESW " + bestPin, "OK", 1500));
                    await Task.Run(() => link.Cmd("SAVE", "OK", 1500));
                    await LoadCfg();
                    Status("Датчик тряски на GPIO" + bestPin);
                    Msg("Найден датчик тряски на GPIO" + bestPin + ". При встряхивании вода будет разбрызгиваться.");
                }
                else
                {
                    await Task.Run(() => link.Cmd("NOSENSOR", "OK", 1500));
                    await Task.Run(() => link.Cmd("SAVE", "OK", 1500));
                    await LoadCfg();
                    Status("Датчик не найден");
                    Msg("Датчик не найден. Водопад будет работать без него — вода будет плавно покачиваться сама.");
                }
            }
            finally { SetBusy(false); }
        }

        bool calUpDone, calFlatDone;
        async Task Cal(bool up)
        {
            var r = await Send("CAL " + (up ? "UP" : "FLAT"), "OK CAL", 3000);
            if (r == null) return;
            if (r.StartsWith("ERR")) { Msg("Сначала найдите акселерометр (шаг 2)."); return; }
            if (up) calUpDone = true; else calFlatDone = true;
            if (calUpDone && calFlatDone)
            {
                if (r.Contains("cal=1"))
                {
                    await Send("SAVE");
                    lblCal.Text = "✓ Датчик откалиброван";
                    Msg("Готово! Теперь вода будет течь туда, куда наклоняете панель.");
                }
                else
                {
                    lblCal.Text = "Положения получились одинаковыми — повторите оба шага.";
                    calUpDone = calFlatDone = false;
                    Msg("Датчик показал почти одно и то же в обоих положениях.\nПовторите: 1) панель стоит вертикально, 2) лежит на столе экраном вверх.", MessageBoxIcon.Warning);
                }
            }
            else lblCal.Text = up ? "Запомнил «вертикально». Теперь положите экраном вверх и нажмите кнопку 2." : "Запомнил «лёжа». Теперь поставьте вертикально и нажмите кнопку 1.";
        }

        // ----------------------------------------------------------- цвет
        void PickColor()
        {
            var d = new ColorDialog { FullOpen = true, Color = swatch.BackColor };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var c = d.Color;
            double max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0, min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
            int s = max > 0 ? (int)Math.Round((max - min) / max * 255) : 0;
            SetColor((int)Math.Round(c.GetHue()) % 360, s, true);
        }

        void SetColor(int h, int s, bool send)
        {
            hue = h; sat = s;
            swatch.BackColor = HsvToColor(h, s / 255.0, 1.0);
            if (send) { Queue("hue", h); Queue("sat", s); }
        }

        static Color HsvToColor(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
