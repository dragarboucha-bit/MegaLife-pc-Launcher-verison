// Mega Life Launcher - starts GTA San Andreas with SA-MP for the Mega Life server.
// The server password changes every hour (see [LAUNCHER] in MLRP.pwn); the launcher works it
// out from the shared secret and passes it to the game, so players never see it.
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace MegaLife
{
    static partial class GameStart
    {
        // ---- hourly password (same algorithm as Launcher_Password in MLRP.pwn) -----------------
        // S1 / S2 (the hourly key, split in two) are in Secret.cs: that file is never put on GitHub.
        const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        static string Secret()
        {
            var b = new byte[S1.Length];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(S1[i] ^ S2[i]);
            return Encoding.ASCII.GetString(b);
        }

        public static long UtcHour()
        {
            return (long)Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds / 3600.0);
        }

        // Seconds until the password changes (0..3599).
        public static int SecondsToNextHour()
        {
            double s = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return 3600 - (int)(s % 3600.0);
        }

        public static string Password(long hour)
        {
            byte[] s = Encoding.ASCII.GetBytes(Secret() + ":" + hour.ToString());
            uint h1 = 0x811C9DC5;
            unchecked
            {
                for (int i = 0; i < s.Length; i++) h1 = (h1 ^ s[i]) * 0x01000193;
                uint h2 = 0x01234567 ^ h1;
                for (int r = 0; r < 3; r++)
                {
                    for (int i = 0; i < s.Length; i++) h2 = (h2 ^ s[i]) * 0x01000193;
                    h2 ^= (h2 >> 15);
                }
                var sb = new StringBuilder(12);
                for (int i = 0; i < 6; i++) sb.Append(Alpha[(int)((h1 >> (i * 5)) & 31)]);
                for (int i = 0; i < 6; i++) sb.Append(Alpha[(int)((h2 >> (i * 5)) & 31)]);
                return sb.ToString();
            }
        }

        // ---- start gta_sa.exe with samp.dll, the way samp.exe does it --------------------------
        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr proc, IntPtr addr, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr written);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr proc, IntPtr attr, UIntPtr stack, IntPtr start, IntPtr param, uint flags, out uint tid);
        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")]
        static extern uint ResumeThread(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        static string Resolve(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return host;
            foreach (IPAddress a in Dns.GetHostAddresses(host))
            {
                if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
            }
            throw new Exception("the server address " + host + " could not be found");
        }

        // Starts the game connected to "host:port" as "name". Throws with a readable message on failure.
        public static int Launch(string gta, string server, string name)
        {
            if (IntPtr.Size != 4) throw new Exception("the launcher must be built for x86 (run build.bat again)");
            int colon = server.LastIndexOf(':');
            string host = Resolve(server.Substring(0, colon));
            string port = server.Substring(colon + 1);
            string exe = Path.Combine(gta, "gta_sa.exe");
            string dll = Path.Combine(gta, "samp.dll");
            if (!File.Exists(dll)) throw new Exception("samp.dll is missing in the game folder (install SA-MP 0.3.7)");

            string pass = Password(UtcHour());
            var cmd = new StringBuilder("\"" + exe + "\" -c -n " + name + " -h " + host + " -p " + port + " -z " + pass);

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            const uint CREATE_SUSPENDED = 0x4;
            if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_SUSPENDED, IntPtr.Zero, gta, ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "gta_sa.exe could not be started");

            bool ok = false;
            int pid = pi.dwProcessId;
            try
            {
                byte[] path = Encoding.Unicode.GetBytes(dll + "\0");
                IntPtr mem = VirtualAllocEx(pi.hProcess, IntPtr.Zero, (UIntPtr)path.Length, 0x3000, 0x04);
                if (mem == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "SA-MP could not be loaded");
                UIntPtr written;
                if (!WriteProcessMemory(pi.hProcess, mem, path, (UIntPtr)path.Length, out written))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SA-MP could not be loaded");
                IntPtr loadLib = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                uint tid;
                IntPtr th = CreateRemoteThread(pi.hProcess, IntPtr.Zero, UIntPtr.Zero, loadLib, mem, 0, out tid);
                if (th == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "SA-MP could not be loaded");
                WaitForSingleObject(th, 15000);
                CloseHandle(th);
                VirtualFreeEx(pi.hProcess, mem, UIntPtr.Zero, 0x8000);
                ResumeThread(pi.hThread);
                ok = true;
            }
            finally
            {
                if (!ok) TerminateProcess(pi.hProcess, 1);
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
            return pid;
        }
    }
}
