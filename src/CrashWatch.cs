// Mega Life Launcher - crash finder.
// While the game runs, the launcher keeps the list of the modules (gta_sa.exe, samp.dll, every .asi / .dll)
// loaded in gta_sa.exe with their addresses. When SA-MP writes a crash report (crashinfo.txt), the
// address of the crash is matched with that list, so the log says WHICH file crashed the game
// (for example "crash in anticrasher037.asi"). Full report: %APPDATA%\MegaLifeLauncher\crash_report.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MegaLife
{
    static class CrashWatch
    {
        class Mod
        {
            public string Name, Path;
            public long Base, Size;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessageText(IntPtr h, int msg, IntPtr wp, StringBuilder lp);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessageInt(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

        // The text of the "SA:MP An exception occured" window (SA-MP does not always write crashinfo.txt).
        static string CrashDialogText(int pid)
        {
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowExW(IntPtr.Zero, w, null, "SA:MP An exception occured")) != IntPtr.Zero)
            {
                int owner;
                GetWindowThreadProcessId(w, out owner);
                if (owner != pid) continue;
                var all = new StringBuilder();
                IntPtr c = IntPtr.Zero;
                while ((c = FindWindowExW(w, c, "Edit", null)) != IntPtr.Zero)
                {
                    int len = SendMessageInt(c, 0x000E /* WM_GETTEXTLENGTH */, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    if (len <= 0) continue;
                    var sb = new StringBuilder(len + 1);
                    SendMessageText(c, 0x000D /* WM_GETTEXT */, (IntPtr)(len + 1), sb);
                    all.AppendLine(sb.ToString());
                }
                if (all.Length > 0) return all.ToString();
            }
            return null;
        }

        public static string ModulesFile
        {
            get { return System.IO.Path.Combine(Config.DataDir, "modules.txt"); }
        }

        public static string ReportFile
        {
            get { return System.IO.Path.Combine(Config.DataDir, "crash_report.txt"); }
        }

        static string CrashInfoFile
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"GTA San Andreas User Files\SAMP\crashinfo.txt");
            }
        }

        // onCrash(text for the log) is called from a worker thread.
        public static void Start(int pid, Action<string> onCrash)
        {
            var t = new Thread(delegate() { Run(pid, onCrash); });
            t.IsBackground = true;
            t.Start();
        }

        static void Run(int pid, Action<string> onCrash)
        {
            Process p;
            try { p = Process.GetProcessById(pid); }
            catch { return; }

            DateTime started = DateTime.Now.AddSeconds(-5);
            DateTime lastInfo = File.Exists(CrashInfoFile) ? File.GetLastWriteTime(CrashInfoFile) : DateTime.MinValue;
            var mods = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
            bool reported = false, accessError = false;

            while (true)
            {
                bool exited;
                try { exited = p.HasExited; }
                catch { exited = true; }

                if (!exited)
                {
                    try
                    {
                        p.Refresh();
                        int before = mods.Count;
                        foreach (ProcessModule m in p.Modules)
                        {
                            var x = new Mod();
                            x.Name = m.ModuleName;
                            x.Path = m.FileName;
                            x.Base = m.BaseAddress.ToInt64();
                            x.Size = m.ModuleMemorySize;
                            mods[x.Name + "@" + x.Base.ToString("X")] = x;
                        }
                        if (mods.Count != before) WriteModules(mods);
                    }
                    catch (Exception ex)
                    {
                        if (!accessError) { accessError = true; onCrash("Crash finder: cannot read the game modules (" + ex.Message + ")"); }
                    }
                }

                // the "SA:MP has crashed" window (the game is still open while it is shown)
                try
                {
                    if (!reported && !exited)
                    {
                        string dlg = CrashDialogText(pid);
                        if (dlg != null)
                        {
                            reported = true;
                            onCrash(Analyse(dlg, mods));
                        }
                    }
                }
                catch { }

                // or crashinfo.txt, when SA-MP writes it
                try
                {
                    if (!reported && File.Exists(CrashInfoFile))
                    {
                        DateTime w = File.GetLastWriteTime(CrashInfoFile);
                        if (w > lastInfo && w >= started)
                        {
                            Thread.Sleep(500);
                            reported = true;
                            onCrash(Analyse(File.ReadAllText(CrashInfoFile), mods));
                        }
                    }
                }
                catch { }

                if (exited) break;
                Thread.Sleep(2000);
            }
        }

        static void WriteModules(Dictionary<string, Mod> mods)
        {
            try
            {
                var list = new List<Mod>(mods.Values);
                list.Sort(delegate(Mod x, Mod y) { return x.Base.CompareTo(y.Base); });
                var sb = new StringBuilder();
                sb.AppendLine("Modules in gta_sa.exe - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (Mod m in list)
                    sb.AppendLine("0x" + m.Base.ToString("X8") + " - 0x" + (m.Base + m.Size).ToString("X8") + "  " + m.Name + "   " + m.Path);
                File.WriteAllText(ModulesFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        static Mod Find(Dictionary<string, Mod> mods, long a)
        {
            foreach (Mod m in mods.Values)
                if (a >= m.Base && a < m.Base + m.Size) return m;
            return null;
        }

        static string Where(Dictionary<string, Mod> mods, long a)
        {
            Mod m = Find(mods, a);
            if (m == null) return "0x" + a.ToString("X8") + " (no module)";
            return m.Name + " +0x" + (a - m.Base).ToString("X");
        }

        static string Analyse(string info, Dictionary<string, Mod> mods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mega Life Launcher " + Config.AppVersion + " - crash report " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Game folder: " + Config.GtaPath);
            sb.AppendLine();

            string first = null;
            Match ex = Regex.Match(info, @"Exception At Address:\s*0x([0-9A-Fa-f]+)");
            if (ex.Success)
            {
                long a = long.Parse(ex.Groups[1].Value, NumberStyles.HexNumber);
                Mod m = Find(mods, a);
                first = (m != null) ? m.Name : null;
                sb.AppendLine("CRASH AT: " + Where(mods, a) + (m != null ? "   [" + m.Path + "]" : ""));
            }

            // the stack shows which other files were part of it
            var seen = new List<string>();
            int stack = info.IndexOf("Stack:", StringComparison.OrdinalIgnoreCase);
            if (stack >= 0)
            {
                foreach (Match v in Regex.Matches(info.Substring(stack), @"0x([0-9A-Fa-f]{8})"))
                {
                    long a = long.Parse(v.Groups[1].Value, NumberStyles.HexNumber);
                    Mod m = Find(mods, a);
                    if (m == null) continue;
                    string n = m.Name;
                    if (!seen.Contains(n)) seen.Add(n);
                }
            }
            if (seen.Count > 0) sb.AppendLine("Files on the stack: " + string.Join(", ", seen.ToArray()));
            sb.AppendLine();
            sb.AppendLine("Loaded modules (" + mods.Count + "):");
            var list = new List<Mod>(mods.Values);
            list.Sort(delegate(Mod x, Mod y) { return x.Base.CompareTo(y.Base); });
            foreach (Mod m in list)
                sb.AppendLine("  0x" + m.Base.ToString("X8") + " - 0x" + (m.Base + m.Size).ToString("X8") + "  " + m.Name + "   " + m.Path);
            sb.AppendLine();
            sb.AppendLine("---- SA-MP crash report ----");
            sb.AppendLine(info);
            try { File.WriteAllText(ReportFile, sb.ToString(), new UTF8Encoding(false)); }
            catch { }

            string head = (first != null) ? "CRASH in " + first : "CRASH (address not in a loaded file)";
            if (seen.Count > 0) head += "  |  stack: " + string.Join(", ", seen.ToArray());
            return head;
        }
    }
}
