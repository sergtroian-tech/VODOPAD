// Водопад — прошивальщик: служебные классы (esptool, COM-порты, связь с прошивкой)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Vodopad
{
    public static class Paths
    {
        public static string ToolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vodopad", "tools");
        public static string BackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Водопад", "Копии прошивки");
        public static string EspTool { get { return Path.Combine(ToolDir, "esptool.exe"); } }
        public static string Fw(string chip, string file) { return Path.Combine(ToolDir, "fw", chip, file); }

        // Достаём из EXE встроенные файлы: esptool.exe и прошивки для всех чипов
        public static void ExtractAll()
        {
            Directory.CreateDirectory(ToolDir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("res/")) continue;
                string rel = name.Substring(4).Replace('/', Path.DirectorySeparatorChar);
                string dst = Path.Combine(ToolDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (File.Exists(dst) && new FileInfo(dst).Length == s.Length && !rel.StartsWith("fw")) continue;   // esptool уже на месте
                    try { using (var f = File.Create(dst)) s.CopyTo(f); } catch (IOException) { /* занят — уже есть */ }
                }
            }
        }
    }

    public class PortInfo
    {
        public string Port, Name, Id;
        public int Score;
        public override string ToString() { return Port + " — " + Name; }
    }

    public static class Ports
    {
        public static List<PortInfo> List()
        {
            var res = new List<PortInfo>();
            var seen = new HashSet<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string name = (o["Name"] ?? "").ToString(), id = (o["DeviceID"] ?? "").ToString().ToUpperInvariant();
                        var m = Regex.Match(name, @"\((COM\d+)\)");
                        if (!m.Success) continue;
                        var p = new PortInfo { Port = m.Groups[1].Value, Name = Regex.Replace(name, @"\s*\(COM\d+\)", ""), Id = id };
                        if (id.Contains("VID_303A")) { p.Score = 5; p.Name += "  [ESP32 — родной USB]"; }
                        else if (id.Contains("VID_1A86") || id.Contains("VID_10C4") || id.Contains("VID_0403")) p.Score = 4;
                        else if (id.StartsWith("USB")) p.Score = 3;
                        else if (id.Contains("BTHENUM") || name.ToLower().Contains("bluetooth")) { p.Score = 0; p.Name += "  [Bluetooth — это не панель]"; }
                        else p.Score = 1;
                        res.Add(p); seen.Add(p.Port);
                    }
            }
            catch { }
            foreach (var pn in SerialPort.GetPortNames())
                if (!seen.Contains(pn)) res.Add(new PortInfo { Port = pn, Name = "COM-порт", Id = "", Score = 1 });
            return res.OrderByDescending(p => p.Score).ThenBy(p => p.Port).ToList();
        }

        // Ищем USB-устройства, которые не стали COM-портом (подсказки пользователю)
        public static List<string> UsbHints()
        {
            var hints = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string id = (o["DeviceID"] ?? "").ToString().ToUpperInvariant();
                        string name = (o["Name"] ?? "").ToString();
                        int err = 0; try { err = Convert.ToInt32(o["ConfigManagerErrorCode"]); } catch { }
                        if (name.Contains("(COM")) continue;
                        if (id.Contains("VID_1A86") || id.Contains("VID_10C4"))
                            hints.Add("Найден USB-переходник (" + name + "), но без COM-порта — установите драйвер " + (id.Contains("VID_1A86") ? "CH340" : "CP210x") + ".");
                        else if (id.Contains("VID_2E8A"))
                            hints.Add("Найдена плата RP2040 (" + name + ") — эта программа прошивает только ESP32/ESP8266. Напишите Claude, сделаю версию для RP2040.");
                        else if (id.Contains("VID_303A") && err != 0)
                            hints.Add("Найдена плата ESP32 (" + name + "), но Windows сообщает об ошибке драйвера (код " + err + ").");
                        else if (err == 28 || err == 1 || err == 10)
                            hints.Add("Устройство без драйвера: " + name + " (" + id.Split('\\').FirstOrDefault() + ")");
                    }
            }
            catch { }
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady && (d.VolumeLabel == "RPI-RP2")) hints.Add("Найден диск RPI-RP2 — это плата RP2040, она прошивается иначе."); } catch { }
            }
            return hints.Distinct().ToList();
        }
    }

    public class ChipInfo
    {
        public string Key = "", Text = "", Mac = "", Error = null;
        public long FlashBytes;
        public string BootOffset { get { return (Key == "esp32" || Key == "esp32s2") ? "0x1000" : "0x0"; } }
        public string MacShort { get { return Mac.Replace(":", "").ToUpperInvariant(); } }
    }

    public static class EspTool
    {
        public static int Run(string args, Action<string> onLine, Action<int> onProgress)
        {
            var psi = new ProcessStartInfo(Paths.EspTool, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Paths.ToolDir
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            if (onLine != null) onLine("> esptool " + args);
            using (var p = Process.Start(psi))
            {
                var t1 = new Thread(() => Pump(p.StandardOutput, onLine, onProgress)) { IsBackground = true };
                var t2 = new Thread(() => Pump(p.StandardError, onLine, onProgress)) { IsBackground = true };
                t1.Start(); t2.Start();
                p.WaitForExit();
                t1.Join(2000); t2.Join(2000);
                return p.ExitCode;
            }
        }

        static void Pump(StreamReader r, Action<string> onLine, Action<int> onProgress)
        {
            var sb = new StringBuilder();
            int c;
            while ((c = r.Read()) >= 0)
            {
                if (c == '\r' || c == '\n')
                {
                    if (sb.Length > 0)
                    {
                        string line = sb.ToString(); sb.Clear();
                        var m = Regex.Match(line, @"\((\d+) ?%\)");
                        if (m.Success && onProgress != null) onProgress(int.Parse(m.Groups[1].Value));
                        else if (onLine != null) onLine(line);
                    }
                }
                else sb.Append((char)c);
            }
            if (sb.Length > 0 && onLine != null) onLine(sb.ToString());
        }

        public static ChipInfo Parse(string all)
        {
            var info = new ChipInfo();
            var m = Regex.Match(all, @"Chip (?:is|type:)\s*([^\r\n]+)");
            if (m.Success) info.Text = m.Groups[1].Value.Trim();
            // Тип чипа берём из строки «Detecting chip type... ESP32-C3» (в «Chip is» бывают ESP32-D0WD, ESP8685 и т.п.)
            string type = "";
            foreach (Match dm in Regex.Matches(all, @"Detecting chip type\.\.\.\s*(ESP[\w\-\(\)]*)")) type = dm.Groups[1].Value.ToUpperInvariant();
            if (type == "")
            {
                string t = info.Text.ToUpperInvariant();
                if (t.StartsWith("ESP32-C3") || t.StartsWith("ESP8685") || t.StartsWith("ESP8686")) type = "ESP32-C3";
                else if (t.StartsWith("ESP32-S3")) type = "ESP32-S3";
                else if (t.StartsWith("ESP32-S2")) type = "ESP32-S2";
                else if (t.StartsWith("ESP8266") || t.StartsWith("ESP8285")) type = "ESP8266";
                else if (t.StartsWith("ESP32-D") || t.StartsWith("ESP32-PICO") || t.StartsWith("ESP32-U4") || t.StartsWith("ESP32-S0")) type = "ESP32";
            }
            switch (type)
            {
                case "ESP32": info.Key = "esp32"; break;
                case "ESP32-C3": info.Key = "esp32c3"; break;
                case "ESP32-S3": info.Key = "esp32s3"; break;
                case "ESP32-S2": info.Key = "esp32s2"; break;
                case "ESP8266": info.Key = "esp8266"; break;
                default: info.Key = ""; break;     // C6, H2, C2, P4 — пока не поддерживаются
            }
            if (info.Text == "" && type != "") info.Text = type;
            m = Regex.Match(all, @"MAC:\s*([0-9a-fA-F:]{17})");
            if (m.Success) info.Mac = m.Groups[1].Value;
            m = Regex.Match(all, @"Detected flash size:\s*(\d+)\s*(MB|KB)");
            if (m.Success) info.FlashBytes = long.Parse(m.Groups[1].Value) * (m.Groups[2].Value == "MB" ? 1024 * 1024 : 1024);
            return info;
        }

        public static ChipInfo Detect(string port, Action<string> log)
        {
            var lines = new List<string>();
            int rc = Run("--port " + port + " --baud 115200 --before default_reset --after hard_reset flash_id",
                l => { lines.Add(l); if (log != null) log(l); }, null);
            string all = string.Join("\n", lines);
            var info = Parse(all);
            if (rc != 0 || info.Text == "")
            {
                string last = lines.LastOrDefault(l => l.Contains("rror") || l.Contains("could not") || l.Contains("Failed")) ?? "";
                if (all.Contains("FileNotFoundError") || all.Contains("cannot find the file") || all.Contains("не удается найти"))
                    info.Error = "Порт " + port + " сейчас не существует — панель отключилась или в списке старый порт.\n\nНажмите «Обновить список» и выберите порт, который появляется при подключении панели.";
                else if (all.Contains("PermissionError") || all.Contains("Access is denied") || all.Contains("Отказано в доступе"))
                    info.Error = "Windows не даёт открыть порт " + port + ".\n\nЧаще всего это значит одно из двух:\n• это не порт панели (например, Bluetooth-порт ноутбука);\n• порт держит другая программа — в том числе второе открытое окно «Водопад».\n\nОтключите панель, нажмите «Обновить список», подключите панель и выберите порт, который появится.";
                else if (all.Contains("OSError(22") || all.Contains("OSError(121") || all.Contains("semaphore") || all.Contains("семафор"))
                    info.Error = "Порт " + port + " не отвечает — похоже, это Bluetooth-порт, а не панель.\n\nВыберите другой порт или подключите панель и нажмите «Обновить список».";
                else
                    info.Error = "Не удалось связаться с чипом на " + port + ".\n\nПопробуйте: отключите панель, зажмите кнопку на ней и, не отпуская, подключите кабель. Отпустите кнопку и нажмите «Определить чип» ещё раз.";
                if (last != "") info.Error += "\n\n(" + last.Trim() + ")";
                return info;
            }
            return info;
        }
    }

    // Связь с прошивкой «Водопад» по USB
    public class Link
    {
        SerialPort sp;
        public string PortName { get { return sp != null ? sp.PortName : null; } }
        public bool IsOpen { get { return sp != null && sp.IsOpen; } }
        public Action<string> Log;
        readonly object lk = new object();

        public void Close()
        {
            lock (lk) { try { if (sp != null) { sp.Close(); sp.Dispose(); } } catch { } sp = null; }
        }

        bool OpenRaw(string port, bool dtr)
        {
            Close();
            try
            {
                sp = new SerialPort(port, 115200) { DtrEnable = dtr, RtsEnable = false, ReadTimeout = 150, WriteTimeout = 1000, NewLine = "\n", Encoding = Encoding.UTF8 };
                sp.Open();
                return true;
            }
            catch (Exception e) { if (Log != null) Log("Не открывается " + port + ": " + e.Message); sp = null; return false; }
        }

        // Возвращает строку "OK VODOPAD ..." или null
        public string Connect(string port, int totalMs)
        {
            var sw = Stopwatch.StartNew();
            bool dtr = false;
            while (sw.ElapsedMilliseconds < totalMs)
            {
                if (OpenRaw(port, dtr))
                {
                    Thread.Sleep(dtr ? 400 : 200);
                    for (int i = 0; i < 3; ++i)
                    {
                        string r = Cmd("PING", "OK VODOPAD", 600);
                        if (r != null) return r;
                    }
                    Close();
                }
                dtr = !dtr;              // для ESP32-S2 (TinyUSB) нужен DTR
                Thread.Sleep(400);
            }
            return null;
        }

        string ReadLine(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try { string l = sp.ReadLine(); return l.TrimEnd('\r'); }
                catch (TimeoutException) { }
            }
            return null;
        }

        public string Cmd(string line, string expect, int timeoutMs)
        {
            lock (lk)
            {
                if (sp == null) return null;
                try
                {
                    sp.DiscardInBuffer();
                    sp.Write(line + "\n");
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        string l = ReadLine((int)Math.Max(50, timeoutMs - sw.ElapsedMilliseconds));
                        if (l == null) break;
                        if (l.StartsWith(expect) || l.StartsWith("ERR")) return l;
                    }
                }
                catch (Exception e) { if (Log != null) Log("Связь: " + e.Message); }
                return null;
            }
        }

        public List<string> CmdMulti(string line, string endPrefix, int timeoutMs)
        {
            var res = new List<string>();
            lock (lk)
            {
                if (sp == null) return res;
                try
                {
                    sp.DiscardInBuffer();
                    sp.Write(line + "\n");
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        string l = ReadLine(300);
                        if (l == null) continue;
                        res.Add(l);
                        if (l.StartsWith(endPrefix) || l.StartsWith("ERR")) break;
                    }
                }
                catch (Exception e) { if (Log != null) Log("Связь: " + e.Message); }
            }
            return res;
        }

        public Dictionary<string, string> GetCfg()
        {
            var d = new Dictionary<string, string>();
            string r = Cmd("GET", "CFG", 1500);
            if (r == null) return null;
            foreach (var part in r.Substring(3).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf('=');
                if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return d;
        }
    }
}
