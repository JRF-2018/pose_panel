// VrcPoseAliasesPanel の部品: OscPoser の操作の口(HTTP)、OSC(チャット)、窓(VRChat を探す・前面に・撮る)、
// pose_aliases.txt、設定、VRChat の launch.exe を探す。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

static class L
{
    public static bool Ja = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
    public static string T(string ja, string en) => Ja ? ja : en;
}

// ---- OscPoser(-api の HTTP。POST /cmd に JSON、GET /state)----

class OscPoserException : Exception { public OscPoserException(string m) : base(m) { } }

static class OscPoser
{
    public static int ApiPort = 9330;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    static string Url(string path) => $"http://127.0.0.1:{ApiPort}{path}";

    static string Http(string method, string path, string body, int timeoutMs)
    {
        var req = (HttpWebRequest)WebRequest.Create(Url(path));
        req.Method = method;
        req.Timeout = req.ReadWriteTimeout = timeoutMs;
        req.Proxy = null;
        try
        {
            if (body != null)
            {
                var b = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json";
                req.ContentLength = b.Length;
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            using (var res = req.GetResponse())
            using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
        }
        catch (WebException e) { throw new OscPoserException(L.T($"OscPoser に届きません({e.Message})", $"Cannot reach OscPoser ({e.Message})")); }
    }

    // コマンドを送って答えを返す。ok でなければ例外。port: 相手(VRChat の OSC の受信ポート)
    public static Dictionary<string, object> Call(string cmd, int? port = null, Dictionary<string, object> args = null, int timeoutMs = 130000)
    {
        var q = new Dictionary<string, object> { ["cmd"] = cmd };
        if (port != null) q["port"] = port.Value;
        if (args != null) foreach (var kv in args) q[kv.Key] = kv.Value;
        var r = Json.Deserialize<Dictionary<string, object>>(Http("POST", "/cmd", Json.Serialize(q), timeoutMs));
        if (!(r.TryGetValue("ok", out var ok) && ok is bool b && b))
            throw new OscPoserException((r.TryGetValue("error", out var e) ? e : r.TryGetValue("status", out var s) ? s : "failed")?.ToString());
        return r;
    }

    public static Dictionary<string, object> State(int timeoutMs = 2000)
    {
        var r = Json.Deserialize<Dictionary<string, object>>(Http("GET", "/state", null, timeoutMs));
        return (Dictionary<string, object>)r["state"];
    }

    public static bool Alive()
    {
        try { State(1000); return true; }
        catch (Exception) { return false; }
    }

    // 探す順: 設定 → この exe のフォルダ → その中と横(一つ上のフォルダ)の OscPoser*\OscPoser.exe と
    // OscPoser*\*\OscPoser.exe(zip を「すべて展開」すると OscPoser_0.4.3\OscPoser_0.4.3\ になる。版の新しいものから)
    // → D:\osc_poser・C:\osc_poser
    public static string FindExe(string setting)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(setting)) list.Add(setting);
        var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        list.Add(Path.Combine(here, "OscPoser.exe"));
        foreach (var dir in new[] { here, Path.GetDirectoryName(here) })
            if (dir != null && Directory.Exists(dir))
                foreach (var d in Directory.GetDirectories(dir, "OscPoser*").OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(Path.Combine(d, "OscPoser.exe"));
                    try { list.AddRange(Directory.GetDirectories(d).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).Select(x => Path.Combine(x, "OscPoser.exe"))); }
                    catch (Exception) { }
                }
        foreach (var drive in new[] { "D:", "C:" })
            list.Add(drive + @"\osc_poser\OscPoser.exe");
        return list.FirstOrDefault(File.Exists);
    }

    public static void Launch(string exe, int[] ports, int current)
    {
        var args = $"-targets {string.Join(",", ports)} -api {ApiPort} -target {current}";
        Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
    }

    public static string Str(object o) => Convert.ToString(o, CultureInfo.InvariantCulture);
}

// ---- OSC(UDP)----

static class Osc
{
    static readonly UdpClient Udp = new UdpClient();

    static void Pad(List<byte> b) { do b.Add(0); while (b.Count % 4 != 0); }

    static void Str(List<byte> b, string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); Pad(b); }

    // /chatbox/input に文字を送る(すぐ出す、通知の音なし)
    public static void Chat(int port, string text)
    {
        var b = new List<byte>();
        Str(b, "/chatbox/input");
        Str(b, ",sTF");
        Str(b, text);
        var a = b.ToArray();
        Udp.Send(a, a.Length, "127.0.0.1", port);
    }
}

// ---- 窓 ----

static class Win
{
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr h, string s);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    // UDP の port を開いているプロセス(IPv4 と IPv6)。VRChat はアンチチートでコマンドラインが読めないので、
    // OSC の受信ポート(プロフィール N なら 9000 + 10N)で見分ける
    public static uint PidOfUdpPort(int port)
    {
        foreach (var (af, rowSize, portOff) in new[] { (2, 12, 4), (23, 28, 20) })
        {
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, false, af, 1, 0);
            if (size <= 0) continue;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedUdpTable(buf, ref size, false, af, 1, 0) != 0) continue;
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    var row = buf + 4 + i * rowSize;
                    int raw = Marshal.ReadInt32(row + portOff);
                    int p = ((raw & 0xff) << 8) | ((raw >> 8) & 0xff);
                    uint pid = (uint)Marshal.ReadInt32(row + portOff + 4);
                    if (p == port && pid != (uint)Process.GetCurrentProcess().Id) return pid;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return 0;
    }

    // OSC の受信ポートを開いている VRChat の、見えている一番上の窓(なければ 0)
    public static IntPtr FindVrchat(int port)
    {
        uint pid = PidOfUdpPort(port);
        if (pid == 0) return IntPtr.Zero;
        try { if (!Process.GetProcessById((int)pid).ProcessName.Equals("VRChat", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero; }
        catch (Exception) { return IntPtr.Zero; }
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p == pid && IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool BringToFront(IntPtr h)
    {
        if (h == IntPtr.Zero || !IsWindow(h)) return false;
        if (IsIconic(h)) ShowWindow(h, 9);
        var fg = GetForegroundWindow();
        uint me = GetCurrentThreadId();
        uint other = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, IntPtr.Zero) : 0;
        if (other != 0 && other != me) AttachThreadInput(me, other, true);
        bool ok;
        try { ok = SetForegroundWindow(h); BringWindowToTop(h); }
        finally { if (other != 0 && other != me) AttachThreadInput(me, other, false); }
        if (!ok || GetForegroundWindow() != h)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            ok = SetForegroundWindow(h);
            BringWindowToTop(h);
        }
        return ok;
    }

    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void SetTitle(IntPtr h, string s) => SetWindowText(h, s);

    // 窓の見えている範囲(影を除く)を撮る。前面に出してから呼ぶ
    public static Bitmap Capture(IntPtr h)
    {
        RECT r;
        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);   // DWMWA_EXTENDED_FRAME_BOUNDS
        return CaptureRect(Rectangle.FromLTRB(r.L, r.T, r.R, r.B));
    }

    public static Bitmap CaptureRect(Rectangle r)
    {
        var b = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Left, r.Top, 0, 0, b.Size);
        return b;
    }
}

// ---- pose_aliases.txt(ビューアー・pose_aliases.py と同じ読み方)----

class Alias
{
    public string Name, Kind, Target, When;   // When: "1.6"・"f30"・null(最後)
}

static class Aliases
{
    static readonly Regex GroupRe = new Regex(@"^\s*\[(.+?)\]\s*(#.*)?$");

    // グループ(ルートは "")ごとに、ファイルに書いた順。同じ中身の行が複数のグループにあれば、それぞれに出す
    public static List<KeyValuePair<string, List<Alias>>> Load(string path, List<string> errors)
    {
        var groups = new List<KeyValuePair<string, List<Alias>>> { new KeyValuePair<string, List<Alias>>("", new List<Alias>()) };
        if (!File.Exists(path)) return groups;
        var cur = groups[0].Value;
        var lines = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            var m = GroupRe.Match(lines[n]);
            if (m.Success)
            {
                var g = m.Groups[1].Value.Trim();
                var found = groups.FirstOrDefault(x => x.Key == g);
                if (found.Value == null) { found = new KeyValuePair<string, List<Alias>>(g, new List<Alias>()); groups.Add(found); }
                cur = found.Value;
                continue;
            }
            var w = SplitWords(lines[n]);
            if (w.Count == 0) continue;
            if ((w.Count != 3 && w.Count != 4) || (w[1] != "anim" && w[1] != "pose" && w[1] != "emote"))
            {
                errors.Add($"pose_aliases.txt:{n + 1}: " + L.T("「エイリアス名 種類 対象 [時刻]」の形ではありません", "not in the form \"name kind target [time]\""));
                continue;
            }
            var a = new Alias { Name = w[0], Kind = w[1], Target = w[2], When = w.Count == 4 && w[3] != "end" ? w[3] : null };
            if (!cur.Any(x => x.Name == a.Name && x.Kind == a.Kind && x.Target == a.Target && x.When == a.When)) cur.Add(a);
        }
        return groups;
    }

    public static List<string> SplitWords(string line)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        bool inWord = false;
        char quote = '\0';
        foreach (var c in line)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else sb.Append(c); continue; }
            if (c == '#') break;
            if (c == '"' || c == '\'') { quote = c; inWord = true; continue; }
            if (char.IsWhiteSpace(c)) { if (inWord) { words.Add(sb.ToString()); sb.Clear(); inWord = false; } continue; }
            sb.Append(c);
            inWord = true;
        }
        if (inWord) words.Add(sb.ToString());
        return words;
    }
}

// ---- 設定(exe の横の VrcPoseAliasesPanel.json)----

class Settings
{
    public int Target = 0;
    public bool Topmost = true;
    public int X = -1, Y = -1;
    public string OscPoserExe = "", LaunchExe = "";
    public bool NoVr = true;
    public bool AskOscPoser = true;   // OscPoser が見つからないとき場所を聞く
    public int ApiPort = 9330;
    public string Lang = "";          // ""(Windows の言語に合わせる)・"ja"・"en"

    static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VrcPoseAliasesPanel.json");

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new Settings(); }
        catch (Exception) { }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); }
        catch (Exception) { }
    }
}

// ---- VRChat の起動 ----

static class VrchatLauncher
{
    // 設定 → Steam のライブラリ(レジストリの SteamPath と libraryfolders.vdf)の steamapps\common\VRChat\launch.exe
    public static string Find(string setting)
    {
        if (!string.IsNullOrWhiteSpace(setting) && File.Exists(setting)) return setting;
        var libs = new List<string>();
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var steam = (k?.GetValue("SteamPath") as string)?.Replace('/', '\\');
                if (steam != null)
                {
                    libs.Add(steam);
                    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
        }
        catch (Exception) { }
        libs.AddRange(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"C:\SteamLibrary", @"E:\SteamLibrary" });
        return libs.Select(l => Path.Combine(l, "steamapps", "common", "VRChat", "launch.exe")).FirstOrDefault(File.Exists);
    }

    public static int Port(int profile) => 9000 + 10 * profile;

    public static void Launch(string exe, int profile, bool noVr)
    {
        var args = new List<string>();
        if (profile != 0) { args.Add($"--profile={profile}"); args.Add($"--osc={Port(profile)}:127.0.0.1:{Port(profile) + 1}"); }
        if (noVr) args.Add("--no-vr");
        Process.Start(new ProcessStartInfo(exe, string.Join(" ", args)) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
    }
}
