// Mega Life Launcher - automatic launcher update and release builder.
//
// Update: at start the launcher reads pc.json (Config.OnlineManifest). When "launcher.build" is newer
// than Config.AppBuild it downloads the new launcher (MediaFire / GitHub link of MegaLife_Launcher.zip),
// checks its SHA-256, puts it in place of the running exe and starts it. With "force": true the
// player cannot keep the old launcher.
//
// Release (admin): MegaLifeLauncher.exe --make-release <server_files>  (make_release.bat)
//   release\MegaLife_PC_Files.zip   the game files (files/, plugins/, img/)  -> MediaFire
//   release\MegaLife_Launcher.zip   MegaLifeLauncher.exe                     -> MediaFire (players download this)
//   release\pc.json                 put on GitHub after pasting the two MediaFire links
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MegaLife
{
    static class SelfUpdate
    {
        static string Exe
        {
            get { return Application.ExecutablePath; }
        }

        // Returns true when the new launcher was started and this one must close.
        public static bool Run()
        {
            Util.TryDelete(Exe + ".old");   // left by the previous update
            Util.TryDelete(Exe + ".new");

            ManifestLauncher l;
            try
            {
                ManifestLocation loc;
                Manifest m = Engine.LoadManifest(out loc);
                l = m.launcher;
            }
            catch
            {
                return false;   // offline: the launcher still works, it will update next time
            }
            if (l == null || l.build <= Config.AppBuild || string.IsNullOrEmpty(l.url)) return false;

            if (!l.force)
            {
                DialogResult r = MessageBox.Show("A new Mega Life Launcher is available (" + (l.version ?? ("build " + l.build)) + ").\n\nUpdate now?",
                    Config.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r != DialogResult.Yes) return false;
            }

            string error = null;
            var form = new UpdateForm(l.version ?? ("build " + l.build));
            var t = new Thread(delegate()
            {
                try { Install(l, form); }
                catch (Exception ex) { error = ex.Message; }
                try { form.BeginInvoke(new MethodInvoker(form.Close)); }
                catch { }
            });
            t.IsBackground = true;
            form.Shown += delegate { t.Start(); };
            Application.Run(form);

            if (error == null)
            {
                Process.Start(Exe);
                return true;
            }
            MessageBox.Show("The launcher could not be updated:\n" + error +
                (l.force ? "\n\nDownload the new launcher from the Mega Life Discord." : ""),
                Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return l.force;   // a forced update that failed: do not open the old launcher
        }

        static void Install(ManifestLauncher l, UpdateForm form)
        {
            string dir = Path.GetDirectoryName(Exe);
            string dl = Path.Combine(Config.DataDir, "launcher_update.part");
            Net.Download(l.url, l.mirror, dl, 0, delegate(long b, long total)
            {
                form.Progress(b, total);
            });

            string newExe = Exe + ".new";
            byte[] head = new byte[2];
            using (var fs = File.OpenRead(dl)) fs.Read(head, 0, 2);
            if (head[0] == 'P' && head[1] == 'K')
            {
                using (ZipArchive z = ZipFile.OpenRead(dl))
                {
                    ZipArchiveEntry hit = null;
                    foreach (ZipArchiveEntry e in z.Entries)
                        if (e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
                    if (hit == null) throw new Exception("the update ZIP has no .exe");
                    using (Stream s = hit.Open())
                    using (var o = File.Create(newExe)) s.CopyTo(o);
                }
            }
            else
            {
                if (head[0] != 'M' || head[1] != 'Z') throw new Exception("the download is not the launcher (check the link in pc.json)");
                File.Copy(dl, newExe, true);
            }
            Util.TryDelete(dl);

            if (Util.IsSha(l.sha256) && Util.Sha256File(newExe) != l.sha256.ToLowerInvariant())
            {
                Util.TryDelete(newExe);
                throw new Exception("the new launcher is damaged or not the official one (SHA-256)");
            }

            // Windows lets a running exe be renamed: old -> .old, new -> exe name.
            try
            {
                File.Move(Exe, Exe + ".old");
            }
            catch (UnauthorizedAccessException)
            {
                Util.TryDelete(newExe);
                throw new Exception("no write access to " + dir + " (move the launcher to Desktop or run it as administrator)");
            }
            File.Move(newExe, Exe);
        }
    }

    class UpdateForm : Form
    {
        readonly Label label;
        readonly ProgressBar bar;

        public UpdateForm(string version)
        {
            Text = Config.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 110);
            BackColor = Color.FromArgb(8, 18, 34);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            label = new Label();
            label.ForeColor = Color.FromArgb(241, 245, 249);
            label.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            label.SetBounds(20, 18, 380, 24);
            label.Text = "Updating Mega Life Launcher to " + version + "...";
            Controls.Add(label);

            bar = new ProgressBar();
            bar.SetBounds(20, 54, 380, 18);
            bar.Maximum = 1000;
            bar.Style = ProgressBarStyle.Marquee;
            Controls.Add(bar);
        }

        public void Progress(long done, long total)
        {
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (total > 0)
                    {
                        bar.Style = ProgressBarStyle.Continuous;
                        bar.Value = (int)Math.Min(1000, done * 1000 / total);
                    }
                    label.Text = "Downloading the new launcher  " + Util.SizeText(done) + (total > 0 ? " / " + Util.SizeText(total) : "");
                }));
            }
            catch { }
        }
    }

    static class ReleaseBuilder
    {
        public static void Run(string serverFiles)
        {
            try
            {
                serverFiles = Path.GetFullPath(serverFiles);
                ManifestBuilder.Run(serverFiles);   // writes server_files\manifest.json (same checks as before)
                string manifestFile = Path.Combine(serverFiles, "manifest.json");
                if (!File.Exists(manifestFile)) return;

                string outDir = Path.Combine(Path.GetDirectoryName(serverFiles.TrimEnd(Path.DirectorySeparatorChar)), "release");
                Directory.CreateDirectory(outDir);

                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 16 * 1024 * 1024;
                Manifest m = ser.Deserialize<Manifest>(File.ReadAllText(manifestFile, Encoding.UTF8));

                // 1) game files pack: files/..., plugins/..., img/... (only what is in the manifest)
                string pack = Path.Combine(outDir, "MegaLife_PC_Files.zip");
                Util.TryDelete(pack);
                using (ZipArchive z = ZipFile.Open(pack, ZipArchiveMode.Create))
                {
                    AddAll(z, serverFiles, "files", m.files);
                    AddAll(z, serverFiles, "plugins", m.plugins);
                    AddAll(z, serverFiles, "img", m.img);
                }

                // 2) launcher for players: just the exe (logo, phone picture, icon are inside it)
                string lzip = Path.Combine(outDir, "MegaLife_Launcher.zip");
                Util.TryDelete(lzip);
                using (ZipArchive z = ZipFile.Open(lzip, ZipArchiveMode.Create))
                {
                    z.CreateEntryFromFile(Application.ExecutablePath, "MegaLifeLauncher.exe", CompressionLevel.Optimal);
                }

                // 3) pc.json = manifest + pack + launcher (links to paste)
                m.client = null;
                m.pack = new ManifestPack();
                m.pack.url = "PASTE_MEDIAFIRE_LINK_OF_MegaLife_PC_Files.zip";
                m.pack.sha256 = Util.Sha256File(pack);
                m.pack.size = new FileInfo(pack).Length;
                m.launcher = new ManifestLauncher();
                m.launcher.build = Config.AppBuild;
                m.launcher.version = Config.AppVersion;
                m.launcher.force = true;
                m.launcher.url = "PASTE_MEDIAFIRE_LINK_OF_MegaLife_Launcher.zip";
                m.launcher.sha256 = Util.Sha256File(Application.ExecutablePath);
                ClearFolders(m.files);
                ClearFolders(m.plugins);
                ClearFolders(m.img);
                string json = ser.Serialize(m).Replace(",\"folder\":null", "");
                File.WriteAllText(Path.Combine(outDir, "pc.json"), json, new UTF8Encoding(false));

                MessageBox.Show("Release written in:\n" + outDir +
                    "\n\n1. Upload MegaLife_PC_Files.zip and MegaLife_Launcher.zip to MediaFire." +
                    "\n2. Open pc.json and paste the two MediaFire links (pack.url and launcher.url)." +
                    "\n3. Put pc.json on GitHub (MegaLife-pc-Launcher-verison, replace pc.json)." +
                    "\n\nLauncher build " + Config.AppBuild + " (" + Config.AppVersion + ")",
                    Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The release could not be made:\n" + ex.Message, Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void ClearFolders(List<ManifestFile> list)
        {
            if (list == null) return;
            foreach (ManifestFile f in list) f.folder = null;
        }

        static void AddAll(ZipArchive z, string serverFiles, string folder, List<ManifestFile> list)
        {
            if (list == null) return;
            foreach (ManifestFile f in list)
            {
                string src = Util.Inside(Path.Combine(serverFiles, folder), f.path);
                if (src == null || !File.Exists(src)) throw new Exception("missing: server_files\\" + folder + "\\" + f.path);
                z.CreateEntryFromFile(src, folder + "/" + f.path, CompressionLevel.Optimal);
            }
        }
    }
}
