// Mega Life Launcher - window. Everything is drawn by hand on an 880 x 520 canvas (scaled with the
// screen DPI), with the colours and the logo of the in-game Mega Blue theme.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MegaLife
{
    class Btn
    {
        public RectangleF R;
        public string Label;
        public bool Visible = true;

        public Btn(float x, float y, float w, float h, string label)
        {
            R = new RectangleF(x, y, w, h);
            Label = label;
        }
    }

    class MainForm : Form
    {
        const float W = 1056f, H = 520f;   // 880 + the phone preview column   // design size; the window is W*S x H*S pixels
        float S = 1f;

        // Mega Blue theme (same values as the server: 0x0A1A2F, 0x00D9FF, 0x48CAE4, 0xF1F5F9, 0x8D99AE, 0x06D6A0)
        static readonly Color Bg0 = Color.FromArgb(8, 18, 34);
        static readonly Color Bg1 = Color.FromArgb(10, 26, 47);
        static readonly Color Card = Color.FromArgb(13, 32, 56);
        static readonly Color Card2 = Color.FromArgb(16, 40, 68);
        static readonly Color Border = Color.FromArgb(28, 58, 92);
        static readonly Color Field = Color.FromArgb(7, 18, 33);
        static readonly Color Cyan = Color.FromArgb(0, 217, 255);
        static readonly Color LightCyan = Color.FromArgb(72, 202, 228);
        static readonly Color TextC = Color.FromArgb(241, 245, 249);
        static readonly Color Slate = Color.FromArgb(141, 153, 174);
        static readonly Color Dim = Color.FromArgb(94, 113, 140);
        static readonly Color Mint = Color.FromArgb(6, 214, 160);
        static readonly Color Amber = Color.FromArgb(255, 183, 3);
        static readonly Color Red = Color.FromArgb(239, 71, 111);

        Font fHero, fTitle, fLabel, fMain, fSub, fBtn, fPlay, fLog;
        StringFormat sfLeft, sfCenter, sfRight, sfPath;
        Image logoImage;   // optional logo.png next to the exe (drawn instead of the text logo)
        Image phoneImage;  // [PHONE] in-game phone preview (embedded resource phone.png, same pictures as mlphone.txd)
        System.Windows.Forms.Timer phoneClock;

        TextBox nameBox;
        System.Windows.Forms.Timer pollTimer;

        readonly List<Btn> buttons = new List<Btn>();
        Btn bMin, bClose, bChange, bCheck, bOpen, bRepair, bPlay, bVoice, bSrv0, bSrv1;
        Btn hover;

        // state (only touched on the window thread)
        bool busy, ready, polling, folderOk, retryWhenOnline;
        string folderMain = "Not selected", folderSub = "";
        string filesMain = "Waiting", filesSub = "";
        string protMain = "Waiting", protSub = "";
        int folderTone = 3, filesTone = 0, protTone = 0;   // 0 idle, 1 working, 2 good, 3 attention, 4 error
        string progText = "";
        float progValue;
        string syncedVersion;
        readonly List<string> logLines = new List<string>();

        long lastProgTick;   // worker thread only

        public MainForm()
        {
            Config.Load();
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            }
            catch { S = 1f; }
            if (S < 1f) S = 1f;

            Text = Config.AppName;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;   // a double click on the top strip must not maximise the window
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new System.Drawing.Size((int)Math.Round(W * S), (int)Math.Round(H * S));
            BackColor = Bg0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            fHero = new Font("Segoe UI", 46f, FontStyle.Bold, GraphicsUnit.Pixel);
            fTitle = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
            fLabel = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel);
            fMain = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            fSub = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            fBtn = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
            fPlay = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
            fLog = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel);

            sfLeft = new StringFormat(StringFormat.GenericTypographic);
            sfLeft.FormatFlags = (sfLeft.FormatFlags | StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces) & ~StringFormatFlags.LineLimit;
            sfLeft.Trimming = StringTrimming.EllipsisCharacter;
            sfCenter = new StringFormat(sfLeft);
            sfCenter.Alignment = StringAlignment.Center;
            sfCenter.LineAlignment = StringAlignment.Center;
            sfRight = new StringFormat(sfLeft);
            sfRight.Alignment = StringAlignment.Far;
            sfPath = new StringFormat(sfLeft);
            sfPath.Trimming = StringTrimming.EllipsisPath;

            try
            {
                string lp = Path.Combine(Config.ExeDir, "logo.png");
                if (File.Exists(lp))
                {
                    // read through memory so the file is not kept locked
                    logoImage = Image.FromStream(new MemoryStream(File.ReadAllBytes(lp)));
                }
                else
                {
                    // logo built into the exe (build.bat /resource): the player only needs MegaLifeLauncher.exe
                    using (Stream rs = typeof(MainForm).Assembly.GetManifestResourceStream("MegaLife.logo.png"))
                    {
                        if (rs != null)
                        {
                            var ms = new MemoryStream();
                            rs.CopyTo(ms);
                            ms.Position = 0;
                            logoImage = Image.FromStream(ms);
                        }
                    }
                }
            }
            catch { logoImage = null; }

            try
            {
                using (Stream rs = typeof(MainForm).Assembly.GetManifestResourceStream("MegaLife.phone.png"))
                {
                    if (rs != null)
                    {
                        var ms = new MemoryStream();
                        rs.CopyTo(ms);
                        ms.Position = 0;
                        phoneImage = Image.FromStream(ms);
                    }
                }
            }
            catch { phoneImage = null; }
            phoneClock = new System.Windows.Forms.Timer();
            phoneClock.Interval = 15000;   // the phone preview shows the real time
            phoneClock.Tick += (o, e) => Invalidate(new Rectangle((int)(866 * S), (int)(50 * S), (int)(180 * S), (int)(450 * S)));
            phoneClock.Start();

            bMin = new Btn(W - 84, 8, 34, 26, "");
            bClose = new Btn(W - 46, 8, 34, 26, "");
            bChange = new Btn(752, 80, 84, 30, "CHANGE");
            bCheck = new Btn(752, 168, 84, 30, "CHECK");
            bOpen = new Btn(752, 256, 84, 30, "OPEN");
            bOpen.Visible = false;
            bRepair = new Btn(660, 256, 84, 30, "REPAIR");   // puts the quarantined files back in the game folder
            bRepair.Visible = false;
            bPlay = new Btn(28, 344, 292, 64, "PLAY");
            bVoice = new Btn(28, 416, 292, 32, "");
            bSrv0 = new Btn(28, 184, 142, 30, Config.ServerNames[0]);
            bSrv1 = new Btn(178, 184, 142, 30, Config.ServerNames[1]);
            buttons.AddRange(new Btn[] { bMin, bClose, bChange, bCheck, bOpen, bRepair, bPlay, bVoice, bSrv0, bSrv1 });

            nameBox = new TextBox();
            nameBox.BorderStyle = BorderStyle.None;
            nameBox.BackColor = Field;
            nameBox.ForeColor = TextC;
            nameBox.Font = new Font("Segoe UI", 15f * S, FontStyle.Regular, GraphicsUnit.Pixel);
            nameBox.MaxLength = 20;
            nameBox.Text = Config.PlayerName;
            nameBox.SetBounds((int)(42 * S), (int)(290 * S), (int)(264 * S), (int)(22 * S));
            nameBox.GotFocus += delegate { Invalidate(); };
            nameBox.LostFocus += delegate { Invalidate(); };
            nameBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    OnPlay();
                }
            };
            Controls.Add(nameBox);

            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = Config.PollSeconds * 1000;
            pollTimer.Tick += delegate { PollManifest(); };
            pollTimer.Start();

            Shown += delegate { FirstRun(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000;        // WS_MINIMIZEBOX: the taskbar can minimise a borderless window
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        // The top strip moves the window (it has no title bar of its own).
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0084 && m.Result.ToInt64() == 1)   // WM_NCHITTEST answered HTCLIENT
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (p.Y / S < 44f && p.X / S < W - 92f) m.Result = (IntPtr)2;   // HTCAPTION
            }
        }

        // ---------------------------------------------------------------- start up
        void FirstRun()
        {
            AddLog(Config.AppName + " " + Config.AppVersion + "  |  server " + Config.Server);
            RefreshFolderCard();
            if (!folderOk)
            {
                if (!ChooseFolder()) return;
            }
            StartCheck(false);
        }

        static string DetectGta()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\SAMP"))
                {
                    if (k == null) return null;
                    string exe = k.GetValue("gta_sa_exe") as string;
                    if (string.IsNullOrEmpty(exe)) return null;
                    string dir = Path.GetDirectoryName(exe);
                    return Util.IsGtaDir(dir) ? dir : null;
                }
            }
            catch { return null; }
        }

        // The player picks the GTA San Andreas folder. True when a new valid folder was chosen.
        bool ChooseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose your GTA San Andreas folder (the one that contains gta_sa.exe)";
                dlg.ShowNewFolderButton = false;
                string guess = folderOk ? Config.GtaPath : DetectGta();
                if (guess != null) dlg.SelectedPath = guess;
                while (true)
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    if (Util.IsGtaDir(dlg.SelectedPath))
                    {
                        Config.GtaPath = Path.GetFullPath(dlg.SelectedPath);
                        Config.SaveUser();
                        syncedVersion = null;
                        RefreshFolderCard();
                        AddLog("Game folder: " + Config.GtaPath);
                        return true;
                    }
                    MessageBox.Show(this,
                        "gta_sa.exe was not found in this folder.\nChoose the folder where GTA San Andreas is installed.",
                        Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        string QuarantineRoot()
        {
            return Path.Combine(Path.Combine(Config.GtaPath, Util.OwnDir), "quarantine");
        }

        void RefreshFolderCard()
        {
            folderOk = Util.IsGtaDir(Config.GtaPath);
            if (folderOk)
            {
                bool samp = File.Exists(Path.Combine(Config.GtaPath, "samp.exe"));
                folderMain = Config.GtaPath;
                folderSub = samp ? "gta_sa.exe and SA-MP found" : "gta_sa.exe found  |  SA-MP is not installed in this folder";
                folderTone = samp ? 2 : 3;
                bOpen.Visible = Directory.Exists(QuarantineRoot());
                bRepair.Visible = bOpen.Visible;
            }
            else
            {
                folderMain = "Not selected";
                folderSub = "Press CHANGE and choose the folder that contains gta_sa.exe";
                folderTone = 3;
                ready = false;
                bOpen.Visible = false;
                bRepair.Visible = false;
            }
            Invalidate();
        }

        // ---------------------------------------------------------------- check = protection + files
        void StartCheck(bool thenPlay)
        {
            if (busy) return;
            RefreshFolderCard();
            if (!folderOk) return;

            busy = true;
            ready = false;
            filesMain = "Checking..."; filesSub = ""; filesTone = 1;
            protMain = "Scanning..."; protSub = ""; protTone = 1;
            progText = "Checking the game folder";
            progValue = 0f;
            Invalidate();

            string gta = Config.GtaPath;
            var t = new System.Threading.Thread(() => DoCheck(gta, thenPlay));
            t.IsBackground = true;
            t.Start();
        }

        // Worker thread.
        void DoCheck(string gta, bool thenPlay)
        {
            Manifest m = null;
            ManifestLocation loc = null;
            string manifestError = null;
            try { m = Engine.LoadManifest(out loc); }
            catch (Exception ex) { manifestError = ex.Message; }

            // Without the manifest the list of allowed mods is unknown: nothing is removed, and PLAY stays locked.
            ScanResult scan = null;
            string scanError = null;
            if (m != null)
            {
                try { scan = Engine.ScanCheats(gta, m); }
                catch (Exception ex) { scanError = ex.Message; }
            }
            bool scanSkipped = (m == null);
            Ui(delegate { ShowScan(scan, scanError, scanSkipped); });

            bool wantVoice = Config.Voice;
            SyncResult sync = null;
            string syncError = manifestError;
            if (m != null)
            {
                try { sync = Engine.SyncFiles(gta, m, loc, LogFromWorker, ProgressFromWorker, wantVoice); }
                catch (Exception ex) { syncError = ex.Message; }
            }

            Ui(delegate
            {
                ShowSync(m, sync, syncError);
                bool clean = (scan != null && scan.Failed.Count == 0);
                bool synced = (sync != null && sync.Errors.Count == 0);
                busy = false;
                ready = clean && synced;
                retryWhenOnline = (m == null);
                if (synced) syncedVersion = m.version;
                if (wantVoice && m != null && m.plugins.Count == 0)
                {
                    Config.Voice = false;
                    Config.SaveUser();
                    AddLog("SampVoice is not on the update server yet (server_files\\plugins is empty)");
                }
                else if (wantVoice && synced) AddLog("SampVoice: installed and up to date");
                progValue = ready ? 1f : 0f;
                progText = ready ? "Ready to play" : "Not ready: read the messages above";
                Invalidate();
                if (ready && thenPlay) LaunchGame();
            });
        }

        void LogFromWorker(string s)
        {
            Ui(delegate { AddLog(s); });
        }

        void ProgressFromWorker(long done, long total, string text)
        {
            long now = Environment.TickCount;
            if (done < total && now - lastProgTick >= 0 && now - lastProgTick < 60) return;
            lastProgTick = now;
            float v = total > 0 ? (float)((double)done / total) : 0f;
            Ui(delegate
            {
                if (!busy) return;
                progValue = v;
                progText = text;
                filesMain = text.StartsWith("Installing") ? "Updating..." : "Checking...";
                Invalidate();
            });
        }

        void ShowScan(ScanResult scan, string error, bool skipped)
        {
            if (skipped)
            {
                protMain = "Not checked yet";
                protSub = "The update server must answer first";
                protTone = 3;
            }
            else if (scan == null)
            {
                protMain = "Scan failed";
                protSub = error ?? "";
                protTone = 4;
                AddLog("Protection: " + error);
            }
            else
            {
                foreach (string r in scan.Removed) AddLog("Removed: " + r);
                foreach (string f in scan.Failed) AddLog("Could not remove: " + f);
                if (scan.Failed.Count > 0)
                {
                    protMain = "Could not remove " + Plural(scan.Failed.Count, "item");
                    protSub = "Close GTA, then press CHECK  |  " + scan.Failed[0];
                    protTone = 4;
                }
                else if (scan.Removed.Count > 0)
                {
                    protMain = Plural(scan.Removed.Count, "item") + " removed";
                    protSub = string.Join(", ", scan.Removed.ToArray());
                    protTone = 3;
                }
                else
                {
                    protMain = "Clean";
                    protSub = "No cheat or mod loader in the game folder";
                    protTone = 2;
                }
            }
            bOpen.Visible = Directory.Exists(QuarantineRoot());
            bRepair.Visible = bOpen.Visible;
            Invalidate();
        }

        void ShowSync(Manifest m, SyncResult sync, string error)
        {
            if (sync == null)
            {
                filesMain = (m == null) ? "Update server not reachable" : "Update failed";
                filesSub = error ?? "";
                filesTone = 4;
                AddLog("Files: " + error);
                return;
            }
            string info = Plural(m.files.Count, "file") + "  |  version " + (m.version.Length > 0 ? m.version : "-");
            if (sync.Errors.Count > 0)
            {
                filesMain = Plural(sync.Errors.Count, "file") + " failed";
                filesSub = sync.Errors[0];
                filesTone = 4;
                foreach (string s in sync.Errors) AddLog("Failed: " + s);
            }
            else if (sync.Updated > 0)
            {
                filesMain = Plural(sync.Updated, "file") + " updated";
                filesSub = info;
                filesTone = 2;
                AddLog("Update done: " + Plural(sync.Updated, "file") + ", " + Util.SizeText(sync.Bytes));
            }
            else
            {
                filesMain = "Up to date";
                filesSub = info;
                filesTone = 2;
            }
        }

        static string Plural(int n, string word)
        {
            return n + " " + word + (n == 1 ? "" : "s");
        }

        // Looks at the manifest from time to time: a new version is installed without restarting the launcher.
        void PollManifest()
        {
            if (busy || polling || !folderOk || GameRunning()) return;
            polling = true;
            var t = new System.Threading.Thread(() =>
            {
                string v = null;
                try
                {
                    ManifestLocation loc;
                    v = Engine.LoadManifest(out loc).version;
                }
                catch { }
                Ui(delegate
                {
                    polling = false;
                    if (v == null || busy) return;
                    if (syncedVersion != null && v != syncedVersion)
                    {
                        AddLog("New update found (version " + v + ")");
                        StartCheck(false);
                    }
                    else if (syncedVersion == null && retryWhenOnline)
                    {
                        StartCheck(false);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool CloseGame()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("gta_sa"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch { }
                }
            }
            catch { }
            return !GameRunning();
        }

        // A gta_sa.exe that is really there: one that already ended but is still listed, or that we
        // are not allowed to look at, or that has no window (stuck before it opened), does not count.
        static bool GameRunning()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("gta_sa"))
                {
                    try
                    {
                        if (p.HasExited) continue;
                        p.Refresh();
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        // ---------------------------------------------------------------- play
        void OnPlay()
        {
            if (busy || !folderOk) return;
            string name = nameBox.Text.Trim();
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9_\[\]\.]{3,20}$"))
            {
                MessageBox.Show(this,
                    "Enter your player name first (3 to 20 characters: letters, numbers and _).\nExample: Firstname_Lastname",
                    Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                nameBox.Focus();
                return;
            }
            Config.PlayerName = name;
            Config.SaveUser();
            if (GameRunning())
            {
                // often a game that did not start properly and stays hidden in the background
                DialogResult r = MessageBox.Show(this,
                    "GTA San Andreas is already running (maybe hidden in the background).\n\nClose it and start again?",
                    Config.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
                if (!CloseGame())
                {
                    MessageBox.Show(this, "GTA San Andreas could not be closed. Close gta_sa.exe in the Task Manager, then press PLAY again.",
                        Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AddLog("Closed a running gta_sa.exe");
            }
            StartCheck(true);   // protection and files are always checked right before the game starts
        }

        // INSTALL SAMPVOICE: from now on the plugin files of the manifest are installed and kept up to date like the others.
        void OnVoice()
        {
            if (busy || !folderOk) return;
            if (!Config.Voice)
            {
                Config.Voice = true;
                Config.SaveUser();
                AddLog("SampVoice: installing");
                StartCheck(false);
                return;
            }

            // already installed: the same button removes it (sampvoice.asi; it is not installed again
            // until INSTALL SAMPVOICE is pressed)
            if (GameRunning())
            {
                MessageBox.Show(this, "Close GTA San Andreas first.", Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Remove SampVoice (voice chat) from the game folder?\n\nYou can install it again with the same button.",
                Config.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Config.Voice = false;
            Config.SaveUser();
            try
            {
                string asi = Path.Combine(Config.GtaPath, "sampvoice.asi");
                if (File.Exists(asi))
                {
                    Util.ClearReadOnly(asi);
                    File.Delete(asi);
                }
                AddLog("SampVoice: removed");
            }
            catch (Exception ex)
            {
                AddLog("SampVoice: could not be removed (" + ex.Message + ")");
            }
            Invalidate();
            StartCheck(false);
        }

        void LaunchGame()
        {
            string gta = Config.GtaPath;
            string samp = Path.Combine(gta, "samp.exe");
            if (!File.Exists(samp))
            {
                MessageBox.Show(this,
                    "SA-MP is not installed in this folder (samp.exe is missing).\nInstall SA-MP 0.3.7 into your GTA San Andreas folder, then press PLAY again.",
                    Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                // SA-MP reads the player name and the game path from its own registry key
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\SAMP"))
                {
                    if (k != null)
                    {
                        k.SetValue("PlayerName", Config.PlayerName);
                        k.SetValue("gta_sa_exe", Path.Combine(gta, "gta_sa.exe"));
                    }
                }
            }
            catch { }
            // The password of the server changes every hour: wait out the last / first seconds of an hour.
            int left = GameStart.SecondsToNextHour();
            string name = Config.PlayerName, server = Config.Server;
            busy = true;
            progText = (left <= 8 || left >= 3594) ? "Starting in a few seconds..." : "Starting the game...";
            Invalidate();
            var t = new System.Threading.Thread(() =>
            {
                string error = null;
                try
                {
                    int l = GameStart.SecondsToNextHour();
                    if (l <= 8) System.Threading.Thread.Sleep((l + 6) * 1000);
                    else if (l >= 3594) System.Threading.Thread.Sleep((l - 3593) * 1000);
                    int pid = GameStart.Launch(gta, server, name);
                    CrashWatch.Start(pid, OnGameCrash);   // tells which file crashed the game, if it crashes
                }
                catch (Exception ex) { error = ex.Message; }
                Ui(delegate
                {
                    busy = false;
                    progText = "Ready to play";
                    Invalidate();
                    if (error != null)
                    {
                        AddLog("Game not started: " + error);
                        MessageBox.Show(this, "The game could not be started:\n" + error, Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    AddLog("Game started as " + name + " on " + server);
                    WindowState = FormWindowState.Minimized;
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // Called by CrashWatch (worker thread) when SA-MP reports a crash.
        void OnGameCrash(string text)
        {
            Ui(delegate
            {
                AddLog(text);
                if (!text.StartsWith("CRASH")) return;
                AddLog("Crash report: " + CrashWatch.ReportFile);
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                MessageBox.Show(this, text + "\n\nThe full report is in:\n" + CrashWatch.ReportFile +
                    "\n\nSend it to the Mega Life staff.", Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        // REPAIR: the files the protection took out (graphics fixes, modloader...) go back to the game folder.
        void RepairGame()
        {
            if (GameRunning())
            {
                MessageBox.Show(this, "Close GTA San Andreas first.", Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "Put back in the game folder every file the protection took out?\n\n" +
                "Use it when GTA does not start any more (for example \"requires DirectX 9.0\").\n" +
                "Cheats are taken out again at the next check.", Config.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                ScanResult r = Engine.RestoreQuarantine(Config.GtaPath);
                AddLog("Repair: " + r.Removed.Count + " file(s) put back" + (r.Failed.Count > 0 ? ", " + r.Failed.Count + " skipped" : ""));
                foreach (string f in r.Failed) AddLog("  skipped: " + f);
            }
            catch (Exception ex)
            {
                AddLog("Repair: " + ex.Message);
            }
            bOpen.Visible = Directory.Exists(QuarantineRoot());
            bRepair.Visible = bOpen.Visible;
            Invalidate();
            StartCheck(false);
        }

        void OpenQuarantine()
        {
            try
            {
                string q = QuarantineRoot();
                if (Directory.Exists(q)) Process.Start("explorer.exe", "\"" + q + "\"");
            }
            catch { }
        }

        // ---------------------------------------------------------------- plumbing
        void Ui(Action a)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
            }
            catch { }
        }

        void AddLog(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            logLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + s);
            if (logLines.Count > 200) logLines.RemoveRange(0, logLines.Count - 200);
            Util.LogFile(s);
            Invalidate();
        }

        bool IsEnabled(Btn b)
        {
            if (b == bMin || b == bClose || b == bOpen) return true;
            if (b == bRepair) return !busy && folderOk;
            if (b == bPlay || b == bVoice) return !busy && folderOk;
            if (b == bSrv0 || b == bSrv1) return !busy;
            return !busy;   // CHANGE and CHECK wait for the running check
        }

        Btn HitButton(Point p)
        {
            float x = p.X / S, y = p.Y / S;
            foreach (Btn b in buttons)
            {
                if (b.Visible && b.R.Contains(x, y)) return b;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Btn b = HitButton(e.Location);
            if (b != hover)
            {
                hover = b;
                Cursor = (b != null && IsEnabled(b)) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null)
            {
                hover = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Btn b = HitButton(e.Location);
            if (b == null || !IsEnabled(b)) return;
            if (b == bClose) Close();
            else if (b == bMin) WindowState = FormWindowState.Minimized;
            else if (b == bChange) { if (ChooseFolder()) StartCheck(false); }
            else if (b == bCheck) StartCheck(false);
            else if (b == bOpen) OpenQuarantine();
            else if (b == bRepair) RepairGame();
            else if (b == bPlay) OnPlay();
            else if (b == bVoice) OnVoice();
            else if (b == bSrv0 || b == bSrv1)
            {
                int idx = (b == bSrv0) ? 0 : 1;
                if (idx != Config.ServerIndex)
                {
                    Config.ServerIndex = idx;
                    Config.SaveUser();
                    AddLog("Server: " + Config.ServerNames[idx] + " (" + Config.Server + ")");
                    Invalidate();
                }
            }
        }

        // ---------------------------------------------------------------- drawing
        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void FillRR(Graphics g, Color c, RectangleF r, float rad)
        {
            using (GraphicsPath p = RoundRect(r, rad))
            using (var br = new SolidBrush(c))
            {
                g.FillPath(br, p);
            }
        }

        static void StrokeRR(Graphics g, Color c, RectangleF r, float rad, float width)
        {
            using (GraphicsPath p = RoundRect(r, rad))
            using (var pen = new Pen(c, width))
            {
                g.DrawPath(pen, p);
            }
        }

        static void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (var br = new SolidBrush(c))
            {
                g.DrawString(s, f, br, r, sf);
            }
        }

        static Color ToneColor(int tone)
        {
            switch (tone)
            {
                case 1: return Cyan;
                case 2: return Mint;
                case 3: return Amber;
                case 4: return Red;
                default: return Dim;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(S, S);

            // background: navy gradient with a soft blue glow in the top left corner
            using (var bg = new LinearGradientBrush(new RectangleF(0, -1, W, H + 2), Bg0, Bg1, 90f))
            {
                g.FillRectangle(bg, 0f, 0f, W, H);
            }
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(-220f, -260f, 700f, 560f);
                using (var glow = new PathGradientBrush(gp))
                {
                    glow.CenterColor = Color.FromArgb(46, 0, 217, 255);
                    glow.SurroundColors = new Color[] { Color.FromArgb(0, 0, 217, 255) };
                    g.FillPath(glow, gp);
                }
            }
            using (var edge = new Pen(Border, 1f))
            {
                g.DrawRectangle(edge, 0.5f, 0.5f, W - 1f, H - 1f);
            }

            // top strip
            FillRR(g, Cyan, new RectangleF(20, 12, 18, 18), 5);
            Txt(g, "M", fTitle, Bg0, new RectangleF(20, 12, 18, 18), sfCenter);
            Txt(g, "MEGA LIFE LAUNCHER", fTitle, Slate, new RectangleF(46, 13, 260, 18), sfLeft);
            DrawWindowButtons(g);

            DrawLogo(g);

            // server: OFFICIAL / TEST
            DrawServerButton(g, bSrv0, Config.ServerIndex == 0);
            DrawServerButton(g, bSrv1, Config.ServerIndex == 1);
            Txt(g, Config.Server, fSub, Slate, new RectangleF(28, 220, 292, 18), sfCenter);

            // player name
            Txt(g, "PLAYER NAME", fLabel, Slate, new RectangleF(29, 262, 200, 15), sfLeft);
            var field = new RectangleF(28, 280, 292, 42);
            FillRR(g, Field, field, 9);
            StrokeRR(g, nameBox.Focused ? Cyan : Border, field, 9, 1.2f);

            // play
            RectangleF pr = bPlay.R;
            if (IsEnabled(bPlay))
            {
                Color left = (hover == bPlay) ? Color.FromArgb(90, 232, 255) : Cyan;
                using (GraphicsPath pp = RoundRect(pr, 12))
                using (var pb = new LinearGradientBrush(new RectangleF(pr.X - 1, pr.Y, pr.Width + 2, pr.Height), left, Color.FromArgb(0, 150, 220), 0f))
                {
                    g.FillPath(pb, pp);
                }
                Txt(g, "PLAY", fPlay, Bg0, pr, sfCenter);
            }
            else
            {
                FillRR(g, Card2, pr, 12);
                StrokeRR(g, Border, pr, 12, 1f);
                Txt(g, busy ? "CHECKING..." : "PLAY", fPlay, Dim, pr, sfCenter);
            }
            // SampVoice
            bool vOn = Config.Voice;
            bool vEnabled = IsEnabled(bVoice);
            bool vHot = vEnabled && hover == bVoice;
            FillRR(g, vHot ? Color.FromArgb(22, 54, 88) : Card2, bVoice.R, 8);
            StrokeRR(g, vOn ? Mint : (vHot ? Cyan : Border), bVoice.R, 8, 1f);
            Txt(g, vOn ? (vHot ? "REMOVE SAMPVOICE" : "SAMPVOICE INSTALLED") : "INSTALL SAMPVOICE", fBtn, vEnabled ? (vOn ? Mint : TextC) : Dim, bVoice.R, sfCenter);
            Txt(g, "v" + Config.AppVersion, fSub, Dim, new RectangleF(29, H - 30, 200, 18), sfLeft);

            // status cards
            DrawCard(g, new RectangleF(348, 56, 504, 78), "GAME FOLDER", folderMain, folderSub, folderTone, sfPath, bChange);
            DrawCard(g, new RectangleF(348, 144, 504, 78), "GAME FILES", filesMain, filesSub, filesTone, sfLeft, bCheck);
            DrawCard(g, new RectangleF(348, 232, 504, 78), "PROTECTION", protMain, protSub, protTone, sfLeft, bOpen, bRepair);

            // activity
            var lr = new RectangleF(348, 320, 504, 128);
            FillRR(g, Card, lr, 10);
            StrokeRR(g, Border, lr, 10, 1f);
            Txt(g, "ACTIVITY", fLabel, Slate, new RectangleF(lr.X + 16, lr.Y + 12, 200, 15), sfLeft);
            int show = Math.Min(5, logLines.Count);
            for (int i = 0; i < show; i++)
            {
                string line = logLines[logLines.Count - show + i];
                Txt(g, line, fLog, (i == show - 1) ? TextC : Slate, new RectangleF(lr.X + 16, lr.Y + 33 + i * 17, lr.Width - 32, 16), sfLeft);
            }

            // progress
            var track = new RectangleF(348, 462, 504, 8);
            FillRR(g, Card2, track, 4);
            float pv = Math.Max(0f, Math.Min(1f, progValue));
            if (pv > 0f)
            {
                float fw = Math.Max(8f, track.Width * pv);
                FillRR(g, ready ? Mint : Cyan, new RectangleF(track.X, track.Y, fw, track.Height), 4);
            }
            Txt(g, progText, fSub, Slate, new RectangleF(348, 477, 430, 18), sfLeft);
            Txt(g, ((int)Math.Round(pv * 100f)) + "%", fSub, Slate, new RectangleF(782, 477, 70, 18), sfRight);

            DrawPhonePreview(g);
        }

        // [PHONE] Mega Life Mobile preview: the same phone players get in game (mlphone.txd), with the real time.
        void DrawPhonePreview(Graphics g)
        {
            var card = new RectangleF(870, 56, 166, 439);
            FillRR(g, Card, card, 10);
            StrokeRR(g, Border, card, 10, 1f);
            Txt(g, "IN-GAME PHONE", fLabel, Slate, new RectangleF(card.X + 14, card.Y + 12, 140, 15), sfLeft);

            var r = new RectangleF(card.X + 12, card.Y + 36, 142, 238);   // 352x590 picture, same ratio
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(r.X - 30, r.Y + 20, r.Width + 60, r.Height - 10);
                using (var glow = new PathGradientBrush(gp))
                {
                    glow.CenterColor = Color.FromArgb(70, 0, 140, 255);
                    glow.SurroundColors = new Color[] { Color.FromArgb(0, 0, 140, 255) };
                    g.FillPath(glow, gp);
                }
            }
            if (phoneImage != null)
            {
                g.DrawImage(phoneImage, r);
                DateTime now = DateTime.Now;
                using (var fClock = new Font("Segoe UI", r.Height * 0.085f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fDate = new Font("Segoe UI", r.Height * 0.026f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fStat = new Font("Segoe UI", r.Height * 0.025f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    Txt(g, now.ToString("HH:mm"), fClock, Color.White, new RectangleF(r.X, r.Y + r.Height * 0.075f, r.Width, r.Height * 0.11f), sfCenter);
                    Txt(g, now.ToString("dddd, d MMM", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(), fDate, TextC,
                        new RectangleF(r.X, r.Y + r.Height * 0.178f, r.Width, r.Height * 0.04f), sfCenter);
                    Txt(g, now.ToString("HH:mm"), fStat, TextC, new RectangleF(r.X + r.Width * 0.09f, r.Y + r.Height * 0.03f, 60, r.Height * 0.04f), sfLeft);
                }
            }
            else
            {
                FillRR(g, Card2, r, 22);
                StrokeRR(g, Cyan, r, 22, 2f);
                Txt(g, "MEGA LIFE", fTitle, TextC, r, sfCenter);
            }

            Txt(g, "Mega Life Mobile", fMain, TextC, new RectangleF(card.X + 8, card.Y + 288, card.Width - 16, 20), sfCenter);
            Txt(g, "Phone, contacts, bank,", fSub, Slate, new RectangleF(card.X + 8, card.Y + 314, card.Width - 16, 16), sfCenter);
            Txt(g, "GPS and voice calls", fSub, Slate, new RectangleF(card.X + 8, card.Y + 330, card.Width - 16, 16), sfCenter);
            Txt(g, "Open it in game with /phone", fSub, Dim, new RectangleF(card.X + 8, card.Y + 356, card.Width - 16, 16), sfCenter);
            var chip = new RectangleF(card.X + 22, card.Y + 392, card.Width - 44, 26);
            FillRR(g, Card2, chip, 13);
            StrokeRR(g, Config.ServerIndex == 0 ? Cyan : Amber, chip, 13, 1f);
            Txt(g, Config.ServerNames[Config.ServerIndex == 1 ? 1 : 0] + " SERVER", fBtn, Config.ServerIndex == 0 ? Cyan : Amber, chip, sfCenter);
        }

        // Same logo as in game: MEGA (white) LIFE (blue), ROLEPLAY between two blue lines. logo.png replaces it when present.
        void DrawLogo(Graphics g)
        {
            float colX = 28f, colW = 292f, mid = colX + colW / 2f;
            if (logoImage != null)
            {
                var box = new RectangleF(colX, 48, colW, 130);
                float k = Math.Min(box.Width / logoImage.Width, box.Height / logoImage.Height);
                float iw = logoImage.Width * k, ih = logoImage.Height * k;
                g.DrawImage(logoImage, box.X + (box.Width - iw) / 2f, box.Y + (box.Height - ih) / 2f, iw, ih);
                return;
            }

            float wm = g.MeasureString("MEGA", fHero, 1000, sfLeft).Width;
            float wl = g.MeasureString("LIFE", fHero, 1000, sfLeft).Width;
            float gap = 8f;
            float x0 = mid - (wm + gap + wl) / 2f;
            Txt(g, "MEGA", fHero, TextC, new RectangleF(x0, 80, wm + 8, 64), sfLeft);
            Txt(g, "LIFE", fHero, Cyan, new RectangleF(x0 + wm + gap, 80, wl + 8, 64), sfLeft);

            string rp = "R O L E P L A Y";
            float wr = g.MeasureString(rp, fLabel, 1000, sfLeft).Width;
            Txt(g, rp, fLabel, LightCyan, new RectangleF(mid - wr / 2f, 147, wr + 6, 15), sfLeft);
            using (var line = new SolidBrush(Color.FromArgb(204, 0, 217, 255)))
            {
                g.FillRectangle(line, mid - wr / 2f - 52f, 153f, 40f, 2f);
                g.FillRectangle(line, mid + wr / 2f + 12f, 153f, 40f, 2f);
            }
        }

        void DrawServerButton(Graphics g, Btn b, bool selected)
        {
            bool hot = IsEnabled(b) && hover == b;
            FillRR(g, selected ? Color.FromArgb(14, 52, 74) : (hot ? Color.FromArgb(22, 54, 88) : Card), b.R, 15);
            StrokeRR(g, selected ? Cyan : (hot ? LightCyan : Border), b.R, 15, selected ? 1.4f : 1f);
            using (var dot = new SolidBrush(selected ? Mint : Dim))
            {
                g.FillEllipse(dot, b.R.X + 16, b.R.Y + 11, 8, 8);
            }
            Txt(g, b.Label, fBtn, selected ? TextC : Slate, new RectangleF(b.R.X + 18, b.R.Y, b.R.Width - 18, b.R.Height), sfCenter);
        }

        void DrawWindowButtons(Graphics g)
        {
            if (hover == bMin) FillRR(g, Color.FromArgb(34, 255, 255, 255), bMin.R, 6);
            if (hover == bClose) FillRR(g, Red, bClose.R, 6);
            using (var pen = new Pen(hover == bMin ? TextC : Slate, 1.4f))
            {
                float cx = bMin.R.X + bMin.R.Width / 2f, cy = bMin.R.Y + bMin.R.Height / 2f;
                g.DrawLine(pen, cx - 5f, cy + 3f, cx + 5f, cy + 3f);
            }
            using (var pen = new Pen(hover == bClose ? Color.White : Slate, 1.4f))
            {
                float cx = bClose.R.X + bClose.R.Width / 2f, cy = bClose.R.Y + bClose.R.Height / 2f;
                g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                g.DrawLine(pen, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
            }
        }

        void DrawCard(Graphics g, RectangleF r, string label, string main, string sub, int tone, StringFormat mainFormat, Btn button)
        {
            DrawCard(g, r, label, main, sub, tone, mainFormat, button, null);
        }

        void DrawCard(Graphics g, RectangleF r, string label, string main, string sub, int tone, StringFormat mainFormat, Btn button, Btn button2)
        {
            FillRR(g, Card, r, 10);
            StrokeRR(g, Border, r, 10, 1f);
            FillRR(g, ToneColor(tone), new RectangleF(r.X + 14, r.Y + 16, 3, r.Height - 32), 1.5f);

            bool hasButton = (button != null && button.Visible);
            bool hasButton2 = (button2 != null && button2.Visible);
            float tw = r.Width - 30f - 16f - (hasButton ? button.R.Width + 16f : 0f) - (hasButton2 ? button2.R.Width + 8f : 0f);
            Txt(g, label, fLabel, Slate, new RectangleF(r.X + 30, r.Y + 13, tw, 15), sfLeft);
            Txt(g, main, fMain, (tone == 4) ? Red : TextC, new RectangleF(r.X + 30, r.Y + 29, tw, 22), mainFormat);
            Txt(g, sub, fSub, Dim, new RectangleF(r.X + 30, r.Y + 53, tw, 18), sfLeft);

            if (hasButton)
            {
                bool enabled = IsEnabled(button);
                bool hot = enabled && hover == button;
                FillRR(g, hot ? Color.FromArgb(22, 54, 88) : Card2, button.R, 8);
                StrokeRR(g, hot ? Cyan : Border, button.R, 8, 1f);
                Txt(g, button.Label, fBtn, enabled ? (hot ? Cyan : TextC) : Dim, button.R, sfCenter);
            }
            if (hasButton2)
            {
                bool enabled2 = IsEnabled(button2);
                bool hot2 = enabled2 && hover == button2;
                FillRR(g, hot2 ? Color.FromArgb(22, 54, 88) : Card2, button2.R, 8);
                StrokeRR(g, hot2 ? Amber : Border, button2.R, 8, 1f);
                Txt(g, button2.Label, fBtn, enabled2 ? (hot2 ? Amber : TextC) : Dim, button2.R, sfCenter);
            }
        }
    }
}
