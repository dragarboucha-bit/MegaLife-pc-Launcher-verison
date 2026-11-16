// Mega Life Launcher - entry point.
//   MegaLifeLauncher.exe                           the launcher
//   MegaLifeLauncher.exe --make-manifest <folder>  admin: writes <folder>\manifest.json (see make_manifest.bat)
//   MegaLifeLauncher.exe --make-release <folder>   admin: release\ (2 ZIPs for MediaFire + pc.json), see make_release.bat
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MegaLife
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && args[0] == "--make-manifest")
            {
                ManifestBuilder.Run(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--make-release")
            {
                ReleaseBuilder.Run(args[1]);
                return;
            }

            Config.Load();
            // newer launcher in pc.json: download it, put it in place and start it
            if (SelfUpdate.Run()) return;

            bool first;
            using (var one = new System.Threading.Mutex(true, "MegaLifeLauncher_SingleInstance", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Mega Life Launcher is already open.", Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm());
            }
        }
    }
}
