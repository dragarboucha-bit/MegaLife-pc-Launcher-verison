// Mega Life Launcher - settings, manifest, file sync and protection scan.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x): see build.bat.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MegaLife
{
    // ------------------------------------------------------------------ settings
    static class Config
    {
        public const string AppName = "Mega Life Launcher";
        public const string AppVersion = "1.0.2";
        // BDEL HADY: +1 for every new launcher you publish (pc.json "launcher.build" tells players to update)
        public const int AppBuild = 3;

        // Online list of files + launcher update (pc.json in the public GitHub repo). The files themselves
        // come from the MediaFire ZIP written in pc.json ("pack"); launcher.cfg can still point elsewhere for tests.
        public const string OnlineManifest = "https://raw.githubusercontent.com/dragarboucha-bit/MegaLife-pc-Launcher-verison/main/pc.json";
        public const string OnlineManifestMirror = "https://cdn.jsdelivr.net/gh/dragarboucha-bit/MegaLife-pc-Launcher-verison@main/pc.json";

        // The launcher only ever connects to one of these two servers (not read from any file).
        public static readonly string[] Servers = { "mega-life.top:7777", "31.57.151.15:7777" };
        public static readonly string[] ServerNames = { "OFFICIAL", "TEST" };
        public static int ServerIndex = 0;   // the player's choice, saved in settings.cfg

        public static string Server
        {
            get { return Servers[(ServerIndex >= 0 && ServerIndex < Servers.Length) ? ServerIndex : 0]; }
        }

        // BDEL HADY (seconds between two looks at the manifest while the launcher is open)
        public const int PollSeconds = 30;

        // launcher.cfg next to the exe can change these two (test version).
        public static string Manifest = OnlineManifest;
        public static bool DeleteCheats = false;

        // Player settings (%APPDATA%\MegaLifeLauncher\settings.cfg)
        public static string GtaPath = "";
        public static string PlayerName = "";
        public static bool Voice = false;   // the player pressed INSTALL SAMPVOICE

        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MegaLifeLauncher");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static Dictionary<string, string> ReadKeys(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return d;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        public static void Load()
        {
            try
            {
                Dictionary<string, string> k = ReadKeys(Path.Combine(ExeDir, "launcher.cfg"));
                string v;
                if (k.TryGetValue("manifest", out v) && v.Length > 0) Manifest = v;
                if (k.TryGetValue("cheats", out v)) DeleteCheats = string.Equals(v, "delete", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            try
            {
                Dictionary<string, string> u = ReadKeys(Path.Combine(DataDir, "settings.cfg"));
                string w;
                if (u.TryGetValue("gta", out w)) GtaPath = w;
                if (u.TryGetValue("name", out w)) PlayerName = w;
                if (u.TryGetValue("server", out w)) ServerIndex = (w == "1") ? 1 : 0;
                if (u.TryGetValue("voice", out w)) Voice = (w == "1");
            }
            catch { }
        }

        public static void SaveUser()
        {
            try
            {
                File.WriteAllLines(Path.Combine(DataDir, "settings.cfg"), new string[] { "gta=" + GtaPath, "name=" + PlayerName, "voice=" + (Voice ? "1" : "0"), "server=" + ServerIndex });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ helpers
    static class Util
    {
        // Folder the launcher keeps for itself inside the game folder (backups, quarantine, list of installed files).
        public const string OwnDir = "_MegaLife";

        // Only data files are ever installed into the game folder: never .exe / .dll / .asi.
        public static readonly string[] DataExt = {
            ".txd", ".dff", ".img", ".ide", ".ipl", ".dat", ".cfg", ".col", ".ifp", ".fxp",
            ".gxt", ".ttf", ".png", ".jpg", ".bmp", ".wav", ".mp3", ".ogg", ".ini"
        };

        public static bool IsGtaDir(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "gta_sa.exe")); }
            catch { return false; }
        }

        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        public static string Sha256File(string path)
        {
            using (var sha = new SHA256CryptoServiceProvider())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Hex(sha.ComputeHash(fs));
            }
        }

        public static bool IsSha(string s)
        {
            if (s == null || s.Length != 64) return false;
            for (int i = 0; i < 64; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        public static bool HasDataExt(string rel)
        {
            string e = Path.GetExtension(rel).ToLowerInvariant();
            return Array.IndexOf(DataExt, e) >= 0;
        }

        // Plugins (SampVoice and its loader) are the only place where .asi / .dll are accepted.
        public static bool HasPluginExt(string rel)
        {
            string e = Path.GetExtension(rel).ToLowerInvariant();
            return e == ".asi" || e == ".dll" || e == ".saa" || Array.IndexOf(DataExt, e) >= 0;
        }

        // "models\txd\a.txd" -> "models/txd/a.txd". Returns null for anything that could leave the game folder.
        public static string NormalizeRel(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            p = p.Replace('\\', '/').Trim();
            if (p.Length == 0 || p[0] == '/' || p.IndexOf(':') >= 0) return null;
            string[] parts = p.Split('/');
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i];
                if (s.Length == 0 || s == "." || s == "..") return null;
                if (s.IndexOfAny(bad) >= 0) return null;
                if (s[s.Length - 1] == '.' || s[s.Length - 1] == ' ') return null;
            }
            if (string.Equals(parts[0], OwnDir, StringComparison.OrdinalIgnoreCase)) return null;
            return string.Join("/", parts);
        }

        // Full path of root + rel, or null when it would land outside root.
        public static string Inside(string root, string rel)
        {
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(r, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }

        public static void ClearReadOnly(string file)
        {
            try
            {
                FileAttributes a = File.GetAttributes(file);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, a & ~FileAttributes.ReadOnly);
            }
            catch { }
        }

        public static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { }
        }

        public static string SizeText(long b)
        {
            if (b >= 1048576) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        public static void LogFile(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(Config.DataDir, "launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ manifest
    public class ManifestFile
    {
        public string path { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
        public string folder { get; set; }   // "files", "client" or "plugins": set by the launcher, never taken from the manifest
    }

    public class ManifestAllow
    {
        public string name { get; set; }
        public string sha256 { get; set; }
    }

    public class Manifest
    {
        public string version { get; set; }
        public List<ManifestFile> files { get; set; }
        public List<ManifestFile> plugins { get; set; }   // SampVoice: installed when the player asks, never removed by the scan
        public List<ManifestFile> client { get; set; }    // SA-MP 0.3.DL client (samp.dll, samp.saa...): always installed
        public List<ManifestFile> img { get; set; }       // models put inside an IMG archive: "models/gta3.img/wmymech.dff"
        public List<ManifestAllow> allow { get; set; }
        public ManifestPack pack { get; set; }          // ZIP (MediaFire / GitHub) that holds files/, plugins/, img/
        public ManifestLauncher launcher { get; set; }  // newest launcher: players with an older build update
    }

    public class ManifestPack
    {
        public string url { get; set; }       // MediaFire page or direct link
        public string mirror { get; set; }    // optional second link (GitHub release...)
        public string sha256 { get; set; }
        public long size { get; set; }
    }

    public class ManifestLauncher
    {
        public int build { get; set; }
        public string version { get; set; }
        public bool force { get; set; }       // true: the player cannot use the old launcher
        public string url { get; set; }       // MediaFire page / direct link of MegaLife_Launcher.zip (or the .exe)
        public string mirror { get; set; }
        public string sha256 { get; set; }    // SHA-256 of MegaLifeLauncher.exe
    }

    class ManifestLocation
    {
        public bool IsUrl;
        public Uri Url;       // address of manifest.json (IsUrl)
        public string Dir;    // folder that holds manifest.json (!IsUrl)
    }

    class SyncResult
    {
        public int Checked;
        public int Updated;
        public long Bytes;
        public List<string> Errors = new List<string>();
    }

    class ScanResult
    {
        public List<string> Removed = new List<string>();
        public List<string> Failed = new List<string>();
    }

    static class Net
    {
        static Net()
        {
            // TLS 1.2 (3072) is not on by default on every .NET Framework 4.x
            try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072; }
            catch { }
        }

        public static HttpWebResponse Get(Uri u)
        {
            var r = (HttpWebRequest)WebRequest.Create(u);
            r.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MegaLifeLauncher/" + Config.AppVersion;
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            r.Headers["Cache-Control"] = "no-cache";
            return (HttpWebResponse)r.GetResponse();
        }

        // MediaFire share page -> direct download link; any other https link is used as it is.
        public static Uri Resolve(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new Exception("no download link");
            var u = new Uri(url);
            if (u.Scheme != Uri.UriSchemeHttps) throw new Exception("download links must start with https://");
            if (u.Host.EndsWith("mediafire.com", StringComparison.OrdinalIgnoreCase) && u.AbsolutePath.IndexOf("/file", StringComparison.OrdinalIgnoreCase) >= 0
                && !u.Host.StartsWith("download", StringComparison.OrdinalIgnoreCase))
            {
                string html;
                using (HttpWebResponse resp = Get(u))
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    html = rd.ReadToEnd();
                }
                System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(html, "https://download[0-9]*\\.mediafire\\.com/[^\"'\\s<>]+");
                if (!mm.Success) throw new Exception("MediaFire did not give a download link (check the link in pc.json)");
                return new Uri(System.Net.WebUtility.HtmlDecode(mm.Value));
            }
            return u;
        }

        // Downloads url (or mirror) to "dest", returns the SHA-256 of what was written.
        public static string Download(string url, string mirror, string dest, long expected, Action<long, long> onBytes)
        {
            Exception last = null;
            foreach (string link in new string[] { url, mirror })
            {
                if (string.IsNullOrEmpty(link)) continue;
                try
                {
                    using (HttpWebResponse resp = Get(Resolve(link)))
                    {
                        string ct = resp.ContentType ?? "";
                        if (ct.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("the link gives a web page, not the file");
                        long total = resp.ContentLength > 0 ? resp.ContentLength : expected;
                        using (var sha = new SHA256CryptoServiceProvider())
                        using (Stream input = resp.GetResponseStream())
                        using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buf = new byte[81920];
                            long got = 0;
                            int n;
                            while ((n = input.Read(buf, 0, buf.Length)) > 0)
                            {
                                output.Write(buf, 0, n);
                                sha.TransformBlock(buf, 0, n, null, 0);
                                got += n;
                                if (onBytes != null) onBytes(got, total);
                            }
                            sha.TransformFinalBlock(buf, 0, 0);
                            return Util.Hex(sha.Hash);
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    Util.TryDelete(dest);
                }
            }
            throw last ?? new Exception("no download link");
        }
    }

    // The files of the manifest come from one ZIP (pc.json "pack"), downloaded once and kept in
    // %APPDATA%\MegaLifeLauncher while its SHA-256 is the same. Inside: files/..., plugins/..., img/...
    static class PackStore
    {
        public static ManifestPack Info;

        static string LocalFile
        {
            get { return Path.Combine(Config.DataDir, "pack_" + Info.sha256.Substring(0, 16) + ".zip"); }
        }

        public static bool Active
        {
            get { return Info != null && !string.IsNullOrEmpty(Info.url) && Util.IsSha(Info.sha256); }
        }

        public static void Ensure(Action<long, long, string> progress)
        {
            string file = LocalFile;
            if (File.Exists(file) && Util.Sha256File(file) == Info.sha256.ToLowerInvariant()) return;
            string part = file + ".part";
            string got = Net.Download(Info.url, Info.mirror, part, Info.size, delegate(long b, long t)
            {
                progress(b, t, "Downloading Mega Life files  " + Util.SizeText(b) + (t > 0 ? " / " + Util.SizeText(t) : ""));
            });
            if (got != Info.sha256.ToLowerInvariant())
            {
                Util.TryDelete(part);
                throw new Exception("the Mega Life files pack is damaged or not the right one (pc.json sha256)");
            }
            Util.TryDelete(file);
            File.Move(part, file);
            foreach (string old in Directory.GetFiles(Config.DataDir, "pack_*.zip"))
                if (!string.Equals(old, file, StringComparison.OrdinalIgnoreCase)) Util.TryDelete(old);
        }

        // One file of the pack, read into memory (the files are small: textures, models, plugins).
        public static Stream Open(ManifestFile f)
        {
            string want = f.folder + "/" + f.path;
            using (ZipArchive z = ZipFile.OpenRead(LocalFile))
            {
                ZipArchiveEntry hit = null;
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    string n = e.FullName.Replace('\\', '/');
                    if (string.Equals(n, want, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
                }
                if (hit == null) throw new Exception("not in the files pack (" + want + ")");
                var ms = new MemoryStream();
                using (Stream s = hit.Open()) s.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
        }
    }

    // Remembers the SHA-256 of files that did not change (same size and date), so big files are not re-read at every start.
    static class HashCache
    {
        static Dictionary<string, string> map;
        static bool dirty;

        static string CacheFile
        {
            get { return Path.Combine(Config.DataDir, "hashcache.txt"); }
        }

        static void Load()
        {
            if (map != null) return;
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CacheFile)) return;
                foreach (string line in File.ReadAllLines(CacheFile))
                {
                    int i = line.LastIndexOf('|');
                    if (i > 0) map[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static string Get(string full)
        {
            Load();
            var fi = new FileInfo(full);
            string key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            string h;
            if (map.TryGetValue(key, out h)) return h;
            h = Util.Sha256File(full);
            if (map.Count > 5000) map.Clear();
            map[key] = h;
            dirty = true;
            return h;
        }

        public static void Save()
        {
            if (map == null || !dirty) return;
            try
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in map) lines.Add(kv.Key + "|" + kv.Value);
                File.WriteAllLines(CacheFile, lines.ToArray());
                dirty = false;
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ engine
    static class Engine
    {
        // ---- manifest ------------------------------------------------------------------------
        static string ReadText(Uri u)
        {
            using (HttpWebResponse resp = Net.Get(u))
            {
                if (resp.ResponseUri.Scheme != Uri.UriSchemeHttps && !resp.ResponseUri.IsLoopback)
                    throw new Exception("The manifest was redirected to an address that is not https.");
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return rd.ReadToEnd();
                }
            }
        }

        public static Manifest LoadManifest(out ManifestLocation loc)
        {
            loc = new ManifestLocation();
            string address = Config.Manifest.Trim();
            string json;

            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var u = new Uri(address);
                if (u.Scheme != Uri.UriSchemeHttps && !u.IsLoopback)
                    throw new Exception("The manifest address must start with https://");
                loc.IsUrl = true;
                loc.Url = u;

                var ub = new UriBuilder(u);
                string q = u.Query;
                ub.Query = (q.Length > 1 ? q.Substring(1) + "&" : "") + "t=" + DateTime.UtcNow.Ticks;
                try
                {
                    json = ReadText(ub.Uri);
                }
                catch
                {
                    // GitHub raw not reachable: same file through the jsDelivr mirror
                    if (address != Config.OnlineManifest) throw;
                    json = ReadText(new Uri(Config.OnlineManifestMirror + "?t=" + DateTime.UtcNow.Ticks));
                }
            }
            else
            {
                string file = Path.IsPathRooted(address) ? address : Path.Combine(Config.ExeDir, address);
                if (!File.Exists(file)) throw new Exception("Manifest not found: " + file);
                loc.IsUrl = false;
                loc.Dir = Path.GetDirectoryName(Path.GetFullPath(file));
                json = File.ReadAllText(file, Encoding.UTF8);
            }

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 16 * 1024 * 1024;
            Manifest m = ser.Deserialize<Manifest>(json);
            if (m == null) throw new Exception("The manifest is empty.");
            if (m.version == null) m.version = "";
            if (m.files == null) m.files = new List<ManifestFile>();
            if (m.allow == null) m.allow = new List<ManifestAllow>();

            // Nothing from the manifest is trusted: every path must stay inside the game folder and be a data file.
            if (m.plugins == null) m.plugins = new List<ManifestFile>();
            if (m.client == null) m.client = new List<ManifestFile>();
            if (m.img == null) m.img = new List<ManifestFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] folders = { "files", "plugins", "client", "img" };
            for (int pass = 0; pass < 4; pass++)
            {
                List<ManifestFile> list = (pass == 0) ? m.files : (pass == 1 ? m.plugins : (pass == 2 ? m.client : m.img));
                foreach (ManifestFile f in list)
                {
                    string rel = (f == null) ? null : Util.NormalizeRel(f.path);
                    if (rel == null) throw new Exception("Manifest: unsafe path refused.");
                    bool typeOk = (pass == 0) ? Util.HasDataExt(rel) : (pass == 3 ? ImgPatch.ValidPath(rel) : Util.HasPluginExt(rel));
                    if (!typeOk) throw new Exception("Manifest: file type refused (" + rel + ")");
                    if (!Util.IsSha(f.sha256) || f.size < 0) throw new Exception("Manifest: bad entry (" + rel + ")");
                    if (!seen.Add(rel)) throw new Exception("Manifest: the same path is listed twice (" + rel + ")");
                    f.path = rel;
                    f.sha256 = f.sha256.ToLowerInvariant();
                    f.folder = folders[pass];
                }
            }
            PackStore.Info = (m.pack != null && Util.IsSha(m.pack.sha256)) ? m.pack : null;
            return m;
        }

        // ---- files ---------------------------------------------------------------------------
        static string InstalledFile(string gta)
        {
            return Path.Combine(gta, Util.OwnDir, "installed.txt");
        }

        static HashSet<string> LoadInstalled(string gta)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = InstalledFile(gta);
                if (File.Exists(f))
                {
                    foreach (string line in File.ReadAllLines(f))
                    {
                        if (line.Trim().Length > 0) set.Add(line.Trim());
                    }
                }
            }
            catch { }
            return set;
        }

        static void SaveInstalled(string gta, HashSet<string> set)
        {
            try
            {
                string f = InstalledFile(gta);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var lines = new List<string>(set);
                lines.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(f, lines.ToArray());
            }
            catch { }
        }

        // Compares the game folder with the manifest and installs every file that is missing or different.
        // progress(done, total, text): done / total is the fraction to show.
        public static SyncResult SyncFiles(string gta, Manifest m, ManifestLocation loc, Action<string> log, Action<long, long, string> progress, bool withPlugins)
        {
            var res = new SyncResult();
            // SA-MP 0.3.7 + SampVoice: the 0.3.DL client files ("client") are not installed any more
            var wanted = new List<ManifestFile>(m.files);
            if (withPlugins) wanted.AddRange(m.plugins);
            wanted.AddRange(m.img);
            var need = new List<ManifestFile>();
            long total = 0;
            int n = 0;

            foreach (ManifestFile f in wanted)
            {
                n++;
                progress(n, wanted.Count, "Checking " + f.path);
                string full = Util.Inside(gta, f.path);
                if (full == null) { res.Errors.Add(f.path + ": outside the game folder"); continue; }
                bool same = false;
                try
                {
                    if (f.folder == "img") same = ImgPatch.IsSame(gta, f);
                    else
                    {
                        var fi = new FileInfo(full);
                        if (fi.Exists && fi.Length == f.size) same = (HashCache.Get(full) == f.sha256);
                    }
                }
                catch (Exception ex)
                {
                    res.Errors.Add(f.path + ": " + ex.Message);
                    continue;
                }
                if (!same) { need.Add(f); total += f.size; }
            }
            res.Checked = wanted.Count;

            if (need.Count > 0 && loc.IsUrl && PackStore.Active)
            {
                try { PackStore.Ensure(progress); }
                catch (Exception ex)
                {
                    res.Errors.Add("files pack: " + ex.Message);
                    return res;
                }
            }

            HashSet<string> installed = LoadInstalled(gta);
            long done = 0;
            int idx = 0;
            foreach (ManifestFile f in need)
            {
                idx++;
                string full = Util.Inside(gta, f.path);
                bool inImg = (f.folder == "img");
                string part = inImg ? Path.Combine(Path.Combine(gta, Util.OwnDir), "img.mlpart") : full + ".mlpart";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(inImg ? part : full));
                    long baseDone = done;
                    string label = "Installing " + f.path + "  (" + idx + "/" + need.Count + ")";
                    string got = Fetch(f, loc, part, delegate(long b) { progress(baseDone + b, total, label); });
                    if (got != f.sha256) throw new Exception("the file received does not match the manifest");

                    if (inImg)
                    {
                        ImgPatch.Apply(gta, f, part);
                        Util.TryDelete(part);
                        done += f.size;
                        res.Updated++;
                        res.Bytes += f.size;
                        log("Installed " + f.path + " (" + Util.SizeText(f.size) + ")");
                        continue;
                    }

                    if (File.Exists(full))
                    {
                        // keep the player's own file the first time one of ours replaces it
                        if (!installed.Contains(f.path)) Backup(gta, f.path, full);
                        Util.ClearReadOnly(full);
                        File.Delete(full);
                    }
                    File.Move(part, full);
                    installed.Add(f.path);
                    done += f.size;
                    res.Updated++;
                    res.Bytes += f.size;
                    log("Installed " + f.path + " (" + Util.SizeText(f.size) + ")");
                }
                catch (UnauthorizedAccessException)
                {
                    Util.TryDelete(part);
                    res.Errors.Add(f.path + ": access denied (close GTA, or run the launcher as administrator)");
                }
                catch (Exception ex)
                {
                    Util.TryDelete(part);
                    res.Errors.Add(f.path + ": " + ex.Message);
                }
            }

            if (res.Updated > 0) SaveInstalled(gta, installed);
            HashCache.Save();
            return res;
        }

        static void Backup(string gta, string rel, string full)
        {
            string b = Path.Combine(Path.Combine(gta, Util.OwnDir, "backup"), rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(b)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(b));
            File.Copy(full, b);
        }

        // Copies one manifest file to "part" and returns the SHA-256 of what was written.
        static string Fetch(ManifestFile f, ManifestLocation loc, string part, Action<long> onBytes)
        {
            using (var sha = new SHA256CryptoServiceProvider())
            using (Stream input = OpenSource(f, loc))
            using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[81920];
                long got = 0;
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    got += n;
                    if (got > f.size) throw new Exception("the file is larger than the manifest says");
                    output.Write(buf, 0, n);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    onBytes(got);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return Util.Hex(sha.Hash);
            }
        }

        static Stream OpenSource(ManifestFile f, ManifestLocation loc)
        {
            if (!loc.IsUrl)
            {
                string src = Util.Inside(Path.Combine(loc.Dir, f.folder), f.path);
                if (src == null || !File.Exists(src)) throw new Exception("missing in server_files\\" + f.folder + " (run make_manifest.bat again)");
                return new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            if (PackStore.Active) return PackStore.Open(f);
            string[] parts = f.path.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            var u = new Uri(loc.Url, f.folder + "/" + string.Join("/", parts));
            return Net.Get(u).GetResponseStream();
        }

        // ---- protection ----------------------------------------------------------------------
        // Script loaders: whole folders.
        static readonly string[] CheatDirs = { "cleo", "moonloader", "SAMPFUNCS" };

        // System DLL names that the game folder never has: a copy next to gta_sa.exe is an ASI loader / injector.
        // Graphics wrappers (d3d8 / d3d9 / d3d11 / ddraw / dxgi: Windows 10/11 fixes, ENB, ReShade) are NOT
        // here any more: many GTA packs need them and the game stops with "requires DirectX 9.0" without them.
        static readonly string[] LoaderDlls = {
            "dinput.dll", "dinput8.dll", "dsound.dll",
            "msacm32.dll", "msvfw32.dll", "version.dll", "wininet.dll", "winmm.dll", "winhttp.dll",
            "xinput1_3.dll", "xinput9_1_0.dll"
        };

        // Folders where loaders pick scripts and plugins up.
        static readonly string[] ModDirs = { "scripts", "plugins", "modloader" };
        static readonly string[] ScriptExt = { ".asi", ".cs", ".cleo", ".sf", ".lua", ".luac" };
        // Well-known fixes that GTA packs need. SilentPatchSA.asi removes the DirectPlay dependency: without it
        // Windows 10/11 asks for DirectPlay and the game does not start. They are kept (by file name) in the
        // game folder and in scripts\ / plugins\. Cheat loaders (CLEO.asi, SAMPFUNCS.asi, MoonLoader.asi...) are
        // never in this list.
        static readonly string[] SafeAsi = {
            "silentpatchsa.asi", "modloader.asi", "sampvoice.asi",
            "refreshratefixbydarkp1xel32.asi", "refreshratefix.asi", "reffix.asi",
            "sensfix.asi", "sensfix xy.asi", "sensfix_xy.asi", "y-axis_sensitivity_fix_gta3vcsa.asi",
            "noshadows.asi", "normalmap.asi", "normalmapfix.asi", "colormod.asi", "ccolor_fast.asi", "ccolor_slow.asi",
            "fix_skygrad.asi", "fix_sizemap.asi", "flickr.asi", "crashes.asi", "anticrasher037.asi",
            "skygfx.asi", "widescreenfix.asi", "gtasa.widescreenfix.asi", "fpslimit.asi", "fpsboost by dapo.asi"
        };

        // modloader keeps its graphics / model mods (.asi such as SkyGfx): only scripts are taken out there.
        static readonly string[] ModloaderScriptExt = { ".cs", ".cleo", ".sf", ".lua", ".luac" };

        // Removes cheat / mod loaders from the game folder. Files whose SHA-256 is in manifest "allow" are kept.
        // "Removed" = moved to <game>\_MegaLife\quarantine\<date>\ (or erased when cheats=delete).
        public static ScanResult ScanCheats(string gta, Manifest m)
        {
            var res = new ScanResult();
            var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (m != null)
            {
                foreach (ManifestAllow a in m.allow)
                {
                    if (a != null && Util.IsSha(a.sha256)) allow.Add(a.sha256);
                }
                foreach (ManifestFile f in m.files) ours.Add(f.path);
                foreach (ManifestFile p in m.plugins) allow.Add(p.sha256);   // SampVoice files are never removed
                foreach (ManifestFile c in m.client) allow.Add(c.sha256);    // nor the SA-MP 0.3.DL files
            }

            string root = Path.GetFullPath(gta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string qdir = Path.Combine(Path.Combine(root, Util.OwnDir, "quarantine"), DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            // 1. script loader folders
            foreach (string d in CheatDirs)
            {
                string full = Path.Combine(root, d);
                if (!Directory.Exists(full)) continue;
                try
                {
                    RemoveDir(full, Path.Combine(qdir, d));
                    res.Removed.Add(d + "\\");
                }
                catch (Exception ex) { res.Failed.Add(d + "\\ (" + ex.Message + ")"); }
            }

            // 2. ASI loader that took the place of vorbisFile.dll (it keeps the real one as vorbisHooked.dll)
            string hooked = Path.Combine(root, "vorbisHooked.dll");
            string vorbis = Path.Combine(root, "vorbisFile.dll");
            if (File.Exists(hooked))
            {
                try
                {
                    bool keep = File.Exists(vorbis) && allow.Contains(Util.Sha256File(vorbis));
                    if (!keep)
                    {
                        if (File.Exists(vorbis)) RemoveFile(vorbis, Path.Combine(qdir, "vorbisFile.dll"));
                        File.Move(hooked, vorbis);   // the game gets its own file back
                        res.Removed.Add("vorbisFile.dll (ASI loader)");
                    }
                }
                catch (Exception ex) { res.Failed.Add("vorbisFile.dll (" + ex.Message + ")"); }
            }

            // 3. loose loaders and scripts
            var suspects = new List<string>();
            foreach (string f in Directory.GetFiles(root))
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                if (name.EndsWith(".asi") || Array.IndexOf(LoaderDlls, name) >= 0) suspects.Add(f);
            }
            foreach (string d in ModDirs)
            {
                string full = Path.Combine(root, d);
                if (!Directory.Exists(full)) continue;
                try
                {
                    string[] exts = d == "modloader" ? ModloaderScriptExt : ScriptExt;
                    foreach (string f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                    {
                        if (Array.IndexOf(exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) suspects.Add(f);
                    }
                }
                catch (Exception ex) { res.Failed.Add(d + "\\ (" + ex.Message + ")"); }
            }

            foreach (string f in suspects)
            {
                string full = Path.GetFullPath(f);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = full.Substring(root.Length);
                try
                {
                    if (ours.Contains(rel.Replace('\\', '/'))) continue;
                    if (Array.IndexOf(SafeAsi, Path.GetFileName(full).ToLowerInvariant()) >= 0) continue;
                    if (allow.Count > 0 && allow.Contains(Util.Sha256File(full))) continue;
                    RemoveFile(full, Path.Combine(qdir, rel));
                    res.Removed.Add(rel);
                }
                catch (Exception ex) { res.Failed.Add(rel + " (" + ex.Message + ")"); }
            }
            return res;
        }

        // REPAIR: everything in <game>\_MegaLife\quarantine goes back where it was (newest first; a file that is
        // already back in the game folder is not overwritten). Cheats are taken out again at the next check,
        // with the current rules, so mods the game needs stay.
        public static ScanResult RestoreQuarantine(string gta)
        {
            var res = new ScanResult();
            string root = Path.GetFullPath(gta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string q = Path.Combine(Path.Combine(root, Util.OwnDir), "quarantine");
            if (!Directory.Exists(q)) return res;
            string[] dates = Directory.GetDirectories(q);
            Array.Sort(dates, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(dates);
            foreach (string date in dates)
            {
                string dRoot = date.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string f in Directory.GetFiles(date, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(dRoot.Length);
                    string dest = Path.GetFullPath(Path.Combine(root, rel));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (File.Exists(dest)) { res.Failed.Add(rel + " (already in the game folder)"); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Move(f, dest);
                        res.Removed.Add(rel);
                    }
                    catch (Exception ex) { res.Failed.Add(rel + " (" + ex.Message + ")"); }
                }
                // empty folders left by the move
                try
                {
                    string[] dirs = Directory.GetDirectories(date, "*", SearchOption.AllDirectories);
                    Array.Sort(dirs, delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                    foreach (string d in dirs)
                        if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d);
                    if (Directory.GetFileSystemEntries(date).Length == 0) Directory.Delete(date);
                }
                catch { }
            }
            try { if (Directory.GetFileSystemEntries(q).Length == 0) Directory.Delete(q); }
            catch { }
            return res;
        }

        static void RemoveFile(string full, string quarantineTarget)
        {
            Util.ClearReadOnly(full);
            if (Config.DeleteCheats)
            {
                File.Delete(full);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(quarantineTarget));
            if (File.Exists(quarantineTarget)) File.Delete(quarantineTarget);
            File.Move(full, quarantineTarget);
        }

        static void RemoveDir(string full, string quarantineTarget)
        {
            if (Config.DeleteCheats)
            {
                foreach (string f in Directory.GetFiles(full, "*", SearchOption.AllDirectories)) Util.ClearReadOnly(f);
                Directory.Delete(full, true);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(quarantineTarget));
            Directory.Move(full, quarantineTarget);
        }
    }

    // ------------------------------------------------------------------ IMG archives (gta3.img)
    // Replaces one model inside an IMG v2 archive ("VER2"): the new data is added at the end of the
    // archive and the directory entry is pointed at it. The original entry is saved once in
    // <game>\_MegaLife\backup\img\<archive>\<name> before the first change.
    static class ImgPatch
    {
        const int Sector = 2048;
        static readonly string[] Ext = { ".dff", ".txd", ".col", ".ifp" };

        // "models/gta3.img/wmymech.dff"
        public static bool ValidPath(string rel)
        {
            if (rel == null) return false;
            string[] p = rel.Split('/');
            if (p.Length < 2) return false;
            string archive = p[p.Length - 2], name = p[p.Length - 1];
            if (!archive.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Length == 0 || name.Length > 23) return false;
            return Array.IndexOf(Ext, Path.GetExtension(name).ToLowerInvariant()) >= 0;
        }

        static void Split(string gta, ManifestFile f, out string archive, out string name)
        {
            int cut = f.path.LastIndexOf('/');
            name = f.path.Substring(cut + 1);
            archive = Util.Inside(gta, f.path.Substring(0, cut));
            if (archive == null || !File.Exists(archive)) throw new Exception("archive not found (" + f.path.Substring(0, cut) + ")");
        }

        // Returns the entry index, and its offset / size in sectors. -1 when the name is not in the archive.
        static int Find(FileStream fs, string name, out uint offset, out ushort sectors, out long dirPos)
        {
            offset = 0; sectors = 0; dirPos = 0;
            var r = new BinaryReader(fs);
            fs.Position = 0;
            if (new string(r.ReadChars(4)) != "VER2") throw new Exception("unsupported IMG archive (only GTA SA VER2)");
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                long pos = 8 + (long)i * 32;
                fs.Position = pos;
                uint off = r.ReadUInt32();
                ushort stream = r.ReadUInt16();
                ushort arch = r.ReadUInt16();
                byte[] nb = r.ReadBytes(24);
                int len = Array.IndexOf(nb, (byte)0);
                string n = Encoding.ASCII.GetString(nb, 0, len < 0 ? 24 : len);
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                {
                    offset = off;
                    sectors = stream != 0 ? stream : arch;
                    dirPos = pos;
                    return i;
                }
            }
            return -1;
        }

        public static bool IsSame(string gta, ManifestFile f)
        {
            string archive, name;
            Split(gta, f, out archive, out name);
            using (var fs = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                uint off; ushort sec; long dp;
                if (Find(fs, name, out off, out sec, out dp) < 0) throw new Exception(name + " is not in " + Path.GetFileName(archive));
                if ((long)sec * Sector < f.size || sec != (f.size + Sector - 1) / Sector) return false;
                fs.Position = (long)off * Sector;
                using (var sha = new SHA256CryptoServiceProvider())
                {
                    byte[] buf = new byte[f.size];
                    int got = 0;
                    while (got < buf.Length)
                    {
                        int n = fs.Read(buf, got, buf.Length - got);
                        if (n <= 0) return false;
                        got += n;
                    }
                    return Util.Hex(sha.ComputeHash(buf)) == f.sha256;
                }
            }
        }

        public static void Apply(string gta, ManifestFile f, string dataFile)
        {
            string archive, name;
            Split(gta, f, out archive, out name);
            byte[] data = File.ReadAllBytes(dataFile);
            long newSectors = (data.Length + Sector - 1) / Sector;
            if (newSectors > 0xFFFF) throw new Exception("model too large for the IMG archive");

            Util.ClearReadOnly(archive);
            using (var fs = new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                uint off; ushort sec; long dirPos;
                if (Find(fs, name, out off, out sec, out dirPos) < 0) throw new Exception(name + " is not in " + Path.GetFileName(archive));

                // save the original model once
                string bdir = Path.Combine(Path.Combine(Path.Combine(gta, Util.OwnDir), "backup"), "img");
                string bfile = Path.Combine(Path.Combine(bdir, Path.GetFileName(archive)), name);
                if (!File.Exists(bfile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(bfile));
                    byte[] old = new byte[(long)sec * Sector];
                    fs.Position = (long)off * Sector;
                    int got = 0;
                    while (got < old.Length)
                    {
                        int n = fs.Read(old, got, old.Length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    File.WriteAllBytes(bfile, old);
                }

                // new data at the end of the archive, padded to whole sectors
                long end = (fs.Length + Sector - 1) / Sector;
                if (end > uint.MaxValue) throw new Exception("IMG archive too large");
                fs.Position = end * Sector;
                fs.Write(data, 0, data.Length);
                int pad = (int)(newSectors * Sector - data.Length);
                if (pad > 0) fs.Write(new byte[pad], 0, pad);
                fs.Flush(true);

                // point the directory entry at it (offset, streaming size, size in archive = 0)
                var w = new BinaryWriter(fs);
                fs.Position = dirPos;
                w.Write((uint)end);
                w.Write((ushort)newSectors);
                w.Write((ushort)0);
                w.Flush();
                fs.Flush(true);
            }
        }
    }

    // ------------------------------------------------------------------ manifest builder (admin side)
    // MegaLifeLauncher.exe --make-manifest <folder>
    //   <folder>\files\...   what players must have, laid out like the game folder
    //   <folder>\allow\...   mods you accept (.asi / .dll): only their fingerprint goes into the manifest
    //   -> writes <folder>\manifest.json
    static class ManifestBuilder
    {
        public static void Run(string dir)
        {
            try
            {
                dir = Path.GetFullPath(dir);
                string filesDir = Path.Combine(dir, "files");
                string allowDir = Path.Combine(dir, "allow");
                Directory.CreateDirectory(filesDir);
                Directory.CreateDirectory(allowDir);

                var files = new List<ManifestFile>();
                var allow = new List<ManifestAllow>();
                var skipped = new List<string>();
                long bytes = 0;

                string root = filesDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string[] all = Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories);
                Array.Sort(all, StringComparer.OrdinalIgnoreCase);
                foreach (string f in all)
                {
                    if (Path.GetFileName(f).StartsWith("_")) continue;   // notes such as _README.txt
                    string rel = f.Substring(root.Length).Replace('\\', '/');
                    string norm = Util.NormalizeRel(rel);
                    if (norm == null || !Util.HasDataExt(norm)) { skipped.Add(rel); continue; }
                    var mf = new ManifestFile();
                    mf.path = norm;
                    mf.size = new FileInfo(f).Length;
                    mf.sha256 = Util.Sha256File(f);
                    bytes += mf.size;
                    files.Add(mf);
                }

                // plugins: SampVoice and the loader it needs; client: SA-MP 0.3.DL (.asi / .dll / .saa accepted here only)
                List<ManifestFile> plugins = ReadFolder(Path.Combine(dir, "plugins"), "plugins/", skipped, ref bytes);
                List<ManifestFile> client = ReadFolder(Path.Combine(dir, "client"), "client/", skipped, ref bytes);
                // img: models\gta3.img\wmymech.dff  ->  replaces wmymech.dff inside the player's models\gta3.img
                List<ManifestFile> img = ReadFolder(Path.Combine(dir, "img"), "img/", skipped, ref bytes);
                img.RemoveAll(delegate(ManifestFile x)
                {
                    if (ImgPatch.ValidPath(x.path)) return false;
                    skipped.Add("img/" + x.path + " (must be img\\models\\<archive>.img\\<name>.dff/.txd)");
                    return true;
                });

                string[] allowed = Directory.GetFiles(allowDir, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(allowed, StringComparer.OrdinalIgnoreCase);
                foreach (string f in allowed)
                {
                    if (Path.GetFileName(f).StartsWith("_")) continue;
                    var ma = new ManifestAllow();
                    ma.name = Path.GetFileName(f);
                    ma.sha256 = Util.Sha256File(f);
                    allow.Add(ma);
                }

                // version = fingerprint of the whole list: it only changes when a file changes
                var sig = new StringBuilder();
                foreach (ManifestFile x in files) sig.Append(x.path).Append(':').Append(x.sha256).Append('\n');
                foreach (ManifestFile q in img) sig.Append("img:").Append(q.path).Append(':').Append(q.sha256).Append('\n');
                foreach (ManifestFile c in client) sig.Append("client:").Append(c.path).Append(':').Append(c.sha256).Append('\n');
                foreach (ManifestFile z in plugins) sig.Append("plugin:").Append(z.path).Append(':').Append(z.sha256).Append('\n');
                foreach (ManifestAllow y in allow) sig.Append("allow:").Append(y.sha256).Append('\n');
                string version;
                using (var sha = new SHA256CryptoServiceProvider())
                {
                    version = Util.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sig.ToString()))).Substring(0, 12);
                }

                var js = new StringBuilder();
                js.Append("{\r\n  \"version\": \"").Append(version).Append("\",\r\n  \"files\": [");
                for (int i = 0; i < files.Count; i++)
                {
                    js.Append(i == 0 ? "\r\n" : ",\r\n");
                    js.Append("    {\"path\": \"").Append(Esc(files[i].path)).Append("\", \"size\": ").Append(files[i].size);
                    js.Append(", \"sha256\": \"").Append(files[i].sha256).Append("\"}");
                }
                js.Append(files.Count > 0 ? "\r\n  ],\r\n" : "],\r\n");
                js.Append("  \"plugins\": [");
                for (int i = 0; i < plugins.Count; i++)
                {
                    js.Append(i == 0 ? "\r\n" : ",\r\n");
                    js.Append("    {\"path\": \"").Append(Esc(plugins[i].path)).Append("\", \"size\": ").Append(plugins[i].size);
                    js.Append(", \"sha256\": \"").Append(plugins[i].sha256).Append("\"}");
                }
                js.Append(plugins.Count > 0 ? "\r\n  ],\r\n" : "],\r\n");
                js.Append("  \"img\": [");
                for (int i = 0; i < img.Count; i++)
                {
                    js.Append(i == 0 ? "\r\n" : ",\r\n");
                    js.Append("    {\"path\": \"").Append(Esc(img[i].path)).Append("\", \"size\": ").Append(img[i].size);
                    js.Append(", \"sha256\": \"").Append(img[i].sha256).Append("\"}");
                }
                js.Append(img.Count > 0 ? "\r\n  ],\r\n" : "],\r\n");
                js.Append("  \"client\": [");
                for (int i = 0; i < client.Count; i++)
                {
                    js.Append(i == 0 ? "\r\n" : ",\r\n");
                    js.Append("    {\"path\": \"").Append(Esc(client[i].path)).Append("\", \"size\": ").Append(client[i].size);
                    js.Append(", \"sha256\": \"").Append(client[i].sha256).Append("\"}");
                }
                js.Append(client.Count > 0 ? "\r\n  ],\r\n" : "],\r\n");
                js.Append("  \"allow\": [");
                for (int i = 0; i < allow.Count; i++)
                {
                    js.Append(i == 0 ? "\r\n" : ",\r\n");
                    js.Append("    {\"name\": \"").Append(Esc(allow[i].name)).Append("\", \"sha256\": \"").Append(allow[i].sha256).Append("\"}");
                }
                js.Append(allow.Count > 0 ? "\r\n  ]\r\n}\r\n" : "]\r\n}\r\n");
                File.WriteAllText(Path.Combine(dir, "manifest.json"), js.ToString(), new UTF8Encoding(false));

                var msg = new StringBuilder();
                msg.Append("manifest.json written.\n\nVersion: ").Append(version);
                msg.Append("\nFiles: ").Append(files.Count).Append(" (").Append(Util.SizeText(bytes)).Append(")");
                msg.Append("\nModels inside IMG (skins, cars): ").Append(img.Count);
                msg.Append("\nSA-MP 0.3.DL files: ").Append(client.Count);
                msg.Append("\nSampVoice / plugin files: ").Append(plugins.Count);
                msg.Append("\nAllowed mods: ").Append(allow.Count);
                if (skipped.Count > 0)
                {
                    msg.Append("\n\nNot included (only data files such as .txd / .dff / .img are sent to players):");
                    for (int i = 0; i < skipped.Count && i < 12; i++) msg.Append("\n  ").Append(skipped[i]);
                    if (skipped.Count > 12) msg.Append("\n  ...");
                }
                MessageBox.Show(msg.ToString(), Config.AppName, MessageBoxButtons.OK,
                    skipped.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The manifest could not be written:\n" + ex.Message, Config.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Every file of a folder (sub-folders kept as the path inside the game folder).
        static List<ManifestFile> ReadFolder(string folder, string label, List<string> skipped, ref long bytes)
        {
            Directory.CreateDirectory(folder);
            var list = new List<ManifestFile>();
            string root = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string[] all = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            Array.Sort(all, StringComparer.OrdinalIgnoreCase);
            foreach (string f in all)
            {
                if (Path.GetFileName(f).StartsWith("_")) continue;
                string rel = f.Substring(root.Length).Replace('\\', '/');
                string norm = Util.NormalizeRel(rel);
                if (norm == null || !Util.HasPluginExt(norm)) { skipped.Add(label + rel); continue; }
                var mf = new ManifestFile();
                mf.path = norm;
                mf.size = new FileInfo(f).Length;
                mf.sha256 = Util.Sha256File(f);
                bytes += mf.size;
                list.Add(mf);
            }
            return list;
        }

        static string Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
