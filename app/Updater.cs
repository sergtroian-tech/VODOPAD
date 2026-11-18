// Водопад — автообновление с GitHub (sergtroian-tech/vodopad, раздел Releases)
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace Vodopad
{
    public static class Updater
    {
        public const string Repo = "sergtroian-tech/vodopad";

        public static Version Current { get { return Norm(Assembly.GetExecutingAssembly().GetName().Version); } }
        public static string CurrentText { get { var v = Current; return v.Build > 0 ? v.ToString(3) : v.ToString(2); } }

        static Version Norm(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }
        static string Url(string file)
        {
            string test = Environment.GetEnvironmentVariable("VODOPAD_UPDATE_URL");   // только для проверки
            return (string.IsNullOrEmpty(test) ? "https://raw.githubusercontent.com/" + Repo + "/main/release/" : test) + file;
        }

        static void Tls() { try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } }

        // Последняя версия на GitHub, или null (нет интернета / ещё ничего не выложено)
        public static Version GetLatest()
        {
            Tls();
            var req = (HttpWebRequest)WebRequest.Create(Url("version.txt"));
            req.UserAgent = "Vodopad-Updater";
            req.Timeout = 8000; req.ReadWriteTimeout = 8000;
            req.AllowAutoRedirect = true;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
            {
                string s = r.ReadToEnd().Trim();
                return Norm(new Version(s));
            }
        }

        // Удаляем старую копию после обновления
        public static void Cleanup()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { string tmp = Application.ExecutablePath + ".new"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        // Скачиваем новую версию и подменяем EXE (запущенный файл Windows разрешает переименовать)
        public static void Download(Action<int> progress)
        {
            Tls();
            string exe = Application.ExecutablePath, tmp = exe + ".new", old = exe + ".old";
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Vodopad-Updater";
                if (progress != null) wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                var t = wc.DownloadFileTaskAsync(new Uri(Url("Vodopad.exe")), tmp);
                t.Wait();
            }
            var fi = new FileInfo(tmp);
            byte[] head = new byte[2];
            using (var f = File.OpenRead(tmp)) f.Read(head, 0, 2);
            if (fi.Length < 1000000 || head[0] != 'M' || head[1] != 'Z') { File.Delete(tmp); throw new Exception("скачанный файл повреждён"); }
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(tmp, exe); }
            catch { File.Move(old, exe); throw; }
        }

        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) });
            Application.Exit();
        }
    }
}
