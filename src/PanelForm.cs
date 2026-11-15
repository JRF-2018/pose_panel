// VrcPoseAliasesPanel: OscPoser を外から(-api の HTTP で)操作する小さなパネル。
// 相手 0・1・2(VRChat のプロフィール番号。OSC は 9000 + 10N)を選び、pose_aliases.txt のポーズを OscPoser で開く・送る。
// VRChat の起動、VRM ポーズビューアー(pose_aliases.txt の編集・アニメーションの取り込み)、VRChat の窓の撮影、チャット。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static L;

class PanelForm : Form
{
    public const string Version = "0.1.0";
    static readonly string Here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    static string AliasPath => Path.Combine(Here, "pose_aliases.txt");
    static string ViewerExe => Path.Combine(Here, "VrmPoseViewer", "VrmPoseViewer.exe");
    const int Profiles = 3;

    readonly Settings st = Settings.Load();
    readonly ToolTip tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 20000, ShowAlways = true };
    readonly Font iconFont = new Font("Segoe UI Symbol", 12f);
    readonly Font textFont = new Font(Ja ? "Meiryo UI" : "Segoe UI", 9f);
    readonly CheckBox[] targetButtons = new CheckBox[Profiles];
    readonly bool[] poseOn = new bool[Profiles];
    readonly string[] poseName = new string[Profiles];
    readonly Alias[] poseAlias = new Alias[Profiles];
    readonly string[] poseGroup = new string[Profiles];
    readonly List<string> log = new List<string>();
    CheckBox pinButton, poseToggle;
    Button poseButton;
    TextBox chatBox;
    Label status;
    ContextMenuStrip poseMenu;
    bool oscposerOk, launching;
    System.Threading.Timer poll;

    int Cur => st.Target;
    int Port(int n) => VrchatLauncher.Port(n);

    public PanelForm()
    {
        Text = "VrcPoseAliasesPanel";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = textFont;
        TopMost = st.Topmost;
        AllowDrop = true;
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = st.X >= 0 && Screen.AllScreens.Any(s => s.WorkingArea.Contains(st.X + 20, st.Y + 20)) ? new Point(st.X, st.Y) : new Point(wa.Right - 420, wa.Top + 80);
        OscPoser.ApiPort = st.ApiPort;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        Build();
        RefreshButtons();
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) => Dropped((string[])e.Data.GetData(DataFormats.FileDrop));
        Shown += (s, e) => Task.Run(StartOscPoser);
        FormClosing += (s, e) => { st.X = Location.X; st.Y = Location.Y; st.Save(); poll?.Dispose(); };
    }

    // ---- 画面 ----

    FlowLayoutPanel Row(Control parent)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
        parent.Controls.Add(r);
        return r;
    }

    Button Btn(Control row, string icon, string tipText, Action click, int width = 34)
    {
        var b = new Button { Text = icon, Font = iconFont, Width = width, Height = 30, Margin = new Padding(1), UseVisualStyleBackColor = true };
        if (click != null) b.Click += (s, e) => click();
        tip.SetToolTip(b, tipText);
        row.Controls.Add(b);
        return b;
    }

    CheckBox Toggle(Control row, string icon, string tipText, int width = 34)
    {
        var c = new CheckBox { Text = icon, Font = iconFont, Appearance = Appearance.Button, Width = width, Height = 30, Margin = new Padding(1), TextAlign = ContentAlignment.MiddleCenter };
        tip.SetToolTip(c, tipText);
        row.Controls.Add(c);
        return c;
    }

    void Build()
    {
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(4, 3, 4, 3) };
        Controls.Add(body);

        // 1 段目: 相手・起動・撮影・ビューアー・OscPoser・設定・手前・止める
        var r = Row(body);
        for (int n = 0; n < Profiles; n++)
        {
            int k = n;
            var c = Toggle(r, n.ToString(), T($"相手: VRChat のプロフィール {n}(OSC {Port(n)})。起動・ポーズ・チャットはこの相手に",
                                              $"Target: VRChat profile {n} (OSC {Port(n)}). Launch, poses and chat go to this target"), 30);
            c.Click += (s, e) => SelectTarget(k);
            targetButtons[n] = c;
        }
        Btn(r, "⏻", T("選んでいる相手(プロフィール)の VRChat を起動する", "Launch VRChat for the selected target (profile)"), LaunchVrchat);
        var shot = Btn(r, "◉", T("VRChat の窓を撮る(shots\\ に保存。パスはクリップボードへ)", "Screenshot a VRChat window (saved in shots\\, path copied)"), null);
        var shotMenu = new ContextMenuStrip { Font = textFont };
        for (int n = 0; n < Profiles; n++)
        {
            int k = n;
            shotMenu.Items.Add(T($"プロフィール {n} の窓を撮る", $"Window of profile {n}"), null, (s, e) => Screenshot(k));
        }
        shotMenu.Items.Add(T("画面全体を撮る", "Whole screen"), null, (s, e) => Screenshot(-1));
        shot.Click += (s, e) => shotMenu.Show(shot, new Point(0, shot.Height));
        Btn(r, "▣", T("VRM ポーズビューアーを開く(ポーズの確認、pose_aliases.txt の編集、アニメーションの取り込み)",
                      "Open the VRM Pose Viewer (check poses, edit pose_aliases.txt, import animations)"), OpenViewer);
        Btn(r, "◫", T("OscPoser の窓を前に出す(動いていなければ開く)", "Bring the OscPoser window to front (open it if not running)"), ShowOscPoser);
        Btn(r, "⚙", T("設定(OscPoser・VRChat の場所など)", "Settings (OscPoser and VRChat locations, ...)"), ShowSettings);
        pinButton = Toggle(r, "⊤", T("いつも手前に表示", "Always on top"));
        pinButton.Checked = st.Topmost;
        pinButton.CheckedChanged += (s, e) => { TopMost = st.Topmost = pinButton.Checked; st.Save(); };
        var stop = Btn(r, "⛔", T("全部止める(OscPoser のまだ送っていない分)", "Stop all (OscPoser's pending sends)"), StopAll);
        stop.ForeColor = Color.FromArgb(0xc0, 0, 0);

        // 2 段目: ポーズ
        r = Row(body);
        poseToggle = Toggle(r, "♙", T("任意ポーズを入れる・切る(OscPoser)", "Pose on / off (OscPoser)"));
        poseToggle.Click += (s, e) => SetPose(!poseOn[Cur]);
        poseButton = new Button { Width = 210, Height = 30, Margin = new Padding(1), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseVisualStyleBackColor = true };
        tip.SetToolTip(poseButton, T("ポーズを選ぶ(pose_aliases.txt と poses\\)。選ぶと OscPoser で開く。VRChat に送るのは ➤ か OscPoser の「送る」",
                                     "Choose a pose (pose_aliases.txt and poses\\). It opens in OscPoser; send it with ➤ or OscPoser's Send"));
        poseMenu = new ContextMenuStrip { Font = textFont };
        poseButton.Click += (s, e) => { FillPoseMenu(); poseMenu.Show(poseButton, new Point(0, poseButton.Height)); };
        r.Controls.Add(poseButton);
        Btn(r, "➤", T("今のポーズを VRChat に送る(OscPoser の「送る」と同じ)", "Send the current pose to VRChat (same as OscPoser's Send)"), SendPose);
        Btn(r, "⇩", T("今のポーズを poses\\ に保存する(OscPoser の姿勢。手の調整なども含む)", "Save the current pose to poses\\ (as in OscPoser, including hand edits)"), SavePose);

        // 3 段目: チャット
        r = Row(body);
        var lbl = new Label { Text = "✉", Font = iconFont, AutoSize = true, Margin = new Padding(2, 5, 2, 0) };
        r.Controls.Add(lbl);
        chatBox = new TextBox { Width = 330, Margin = new Padding(1, 4, 1, 1) };
        tip.SetToolTip(chatBox, T("チャット(Enter で送る)", "Chatbox (Enter to send)"));
        chatBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendChat(); } };
        r.Controls.Add(chatBox);

        // 4 段目: 状態
        status = new Label { AutoSize = false, Width = 368, Height = 20, AutoEllipsis = true, ForeColor = Color.FromArgb(0x40, 0x40, 0x40), Margin = new Padding(2, 2, 2, 0) };
        status.DoubleClick += (s, e) => { if (log.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, log)); };
        body.Controls.Add(status);
        Log(T("準備中…", "Starting..."));
    }

    void RefreshButtons()
    {
        if (InvokeRequired) { BeginInvoke((Action)RefreshButtons); return; }
        for (int n = 0; n < Profiles; n++)
        {
            targetButtons[n].Checked = n == Cur;
            targetButtons[n].BackColor = n == Cur ? Color.FromArgb(0x9c, 0xd0, 0xff) : SystemColors.Control;
        }
        poseToggle.Checked = poseOn[Cur];
        poseToggle.BackColor = poseOn[Cur] ? Color.FromArgb(0x9c, 0xd0, 0xff) : SystemColors.Control;
        poseButton.Text = poseName[Cur] ?? T("(ポーズを選ぶ)", "(choose a pose)");
        poseButton.ForeColor = poseName[Cur] == null ? Color.Gray : SystemColors.ControlText;
        Text = $"VrcPoseAliasesPanel  [{Cur}]" + (oscposerOk ? "" : T("  (OscPoser なし)", "  (no OscPoser)"));
    }

    void Log(string text, int? target = null)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => Log(text, target))); return; }
        var line = $"{DateTime.Now:HH:mm:ss} " + (target != null ? $"[{target}] " : "") + text;
        log.Add(line);
        if (log.Count > 200) log.RemoveAt(0);
        status.Text = text;
        tip.SetToolTip(status, string.Join("\n", log.Skip(Math.Max(0, log.Count - 12))) + "\n" + T("(ダブルクリックで記録をコピー)", "(double-click to copy the log)"));
        try { File.AppendAllText(Path.Combine(Here, "VrcPoseAliasesPanel_log.txt"), line + Environment.NewLine); } catch (Exception) { }
    }

    void Bg(Action a) => Task.Run(() =>
    {
        try { a(); }
        catch (OscPoserException e) { Log("OscPoser: " + e.Message); }
        catch (Exception e) { Log(e.Message); }
    });

    // ---- 相手 ----

    void SelectTarget(int n)
    {
        st.Target = n;
        st.Save();
        RefreshButtons();
        Bg(() => { if (oscposerOk) OscPoser.Call("target", Port(n)); });
    }

    // ---- OscPoser ----

    void StartOscPoser()
    {
        if (!OscPoser.Alive()) OpenOscPoser(true);
        poll = new System.Threading.Timer(_ => PollState(), null, 0, 1000);
        // 試し用: -pick "グループ|名前"(ルートは "|名前")で、そのポーズを選んだことにする
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, "-pick");
        if (i > 0 && i + 1 < a.Length)
        {
            Thread.Sleep(1500);
            var parts = a[i + 1].Split('|');
            var g = Aliases.Load(AliasPath, new List<string>()).FirstOrDefault(x => x.Key == parts[0]);
            var al = g.Value?.FirstOrDefault(x => x.Name == parts[1]);
            if (al != null) Invoke((Action)(() => PickPose(al.Name, al, parts[0])));
            else Log("-pick: not found");
        }
    }

    void PollState()
    {
        bool was = oscposerOk;
        try
        {
            var s = OscPoser.State();
            oscposerOk = true;
            bool changed = !was;
            if (s.TryGetValue("targets", out var ts) && ts is System.Collections.ArrayList list)
                foreach (Dictionary<string, object> t in list)
                {
                    int port = Convert.ToInt32(t["port"]);
                    int n = (port - 9000) / 10;
                    if (n < 0 || n >= Profiles || (port - 9000) % 10 != 0) continue;
                    bool on = t.TryGetValue("poseOn", out var p) && p is bool b && b;
                    if (poseOn[n] != on) { poseOn[n] = on; changed = true; }
                }
            if (!was) Log(T("OscPoser とつながっています", "Connected to OscPoser"));
            if (changed) RefreshButtons();
        }
        catch (Exception)
        {
            oscposerOk = false;
            if (was) { Log(T("OscPoser とつながっていません(◫ で開けます)", "Not connected to OscPoser (◫ opens it)")); RefreshButtons(); }
        }
    }

    // OscPoser を開く。見つからなければ場所を聞く(ask)
    bool OpenOscPoser(bool ask)
    {
        if (launching) return false;
        launching = true;
        try
        {
            var exe = OscPoser.FindExe(st.OscPoserExe);
            if (exe == null && ask && st.AskOscPoser) exe = (string)Invoke((Func<string>)AskOscPoserExe);
            if (exe == null) { Log(T("OscPoser.exe が見つかりません(⚙ で場所を指定してください)", "OscPoser.exe not found (set its location in ⚙)")); return false; }
            OscPoser.Launch(exe, Enumerable.Range(0, Profiles).Select(Port).ToArray(), Cur);
            Log(T("OscPoser を開いています: ", "Opening OscPoser: ") + exe);
            for (int i = 0; i < 60; i++)
            {
                if (OscPoser.Alive()) { Log(T("OscPoser とつながりました", "Connected to OscPoser")); return true; }
                Thread.Sleep(500);
            }
            Log(T("OscPoser の操作の口が開きません(OscPoser 0.4.0 以降が要ります)", "OscPoser's API did not open (OscPoser 0.4.0 or later is needed)"));
            return false;
        }
        finally { launching = false; }
    }

    string AskOscPoserExe()
    {
        var msg = T("OscPoser.exe が見つかりません。\n\nOscPoser(無料)を入手して展開し、OscPoser.exe の場所を指定してください。\n" +
                    "このパネルのフォルダの中か横に OscPoser_<版> のフォルダとして置けば、自動で見つけます。\n\n" +
                    "「はい」: OscPoser.exe を選ぶ\n「いいえ」: 入手先(GitHub)を開く\n「キャンセル」: あとで(⚙ でも指定できます)",
                    "OscPoser.exe was not found.\n\nGet OscPoser (free), extract it, and choose OscPoser.exe.\n" +
                    "If you put its folder (OscPoser_<version>) in or next to this panel's folder, it is found automatically.\n\n" +
                    "Yes: choose OscPoser.exe\nNo: open the download page (GitHub)\nCancel: later (also in ⚙)");
        var r = MessageBox.Show(this, msg, "VrcPoseAliasesPanel", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.No) { Process.Start("https://github.com/JRF-2018/osc_poser/releases"); return null; }
        if (r != DialogResult.Yes) return null;
        using (var d = new OpenFileDialog { Title = "OscPoser.exe", Filter = "OscPoser.exe|OscPoser.exe|*.exe|*.exe" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return null;
            st.OscPoserExe = d.FileName;
            st.Save();
            return d.FileName;
        }
    }

    void ShowOscPoser() => Bg(() =>
    {
        if (OscPoser.Alive()) OscPoser.Call("front");
        else OpenOscPoser(true);
    });

    void SetPose(bool on)
    {
        int n = Cur;
        poseOn[n] = on;
        RefreshButtons();
        Bg(() => { OscPoser.Call(on ? "on" : "off", Port(n)); Log(on ? T("任意ポーズを入れました", "Pose on") : T("任意ポーズを切りました", "Pose off"), n); });
    }

    void SendPose()
    {
        int n = Cur;
        Bg(() => { OscPoser.Call("send", Port(n)); Log(T("ポーズを送っています(OscPoser)", "Sending the pose (OscPoser)"), n); });
    }

    void StopAll() => Bg(() => { OscPoser.Call("stop"); Log(T("OscPoser のまだ送っていない分をやめました", "Stopped OscPoser's pending sends")); });

    // ---- ポーズ ----

    void FillPoseMenu()
    {
        poseMenu.Items.Clear();
        var errors = new List<string>();
        var groups = Aliases.Load(AliasPath, errors);
        foreach (var e in errors.Take(3)) Log(e);
        ToolStripItem Item(Alias a, string group) => new ToolStripMenuItem(a.Name, null, (s, ev) => PickPose(a.Name, a, group));
        foreach (var a in groups[0].Value) poseMenu.Items.Add(Item(a, ""));
        var subs = groups.Skip(1).Where(g => g.Value.Count > 0).ToList();
        if (subs.Count > 0 && poseMenu.Items.Count > 0) poseMenu.Items.Add(new ToolStripSeparator());
        foreach (var g in subs)
        {
            var sub = new ToolStripMenuItem($"{g.Key}  ({g.Value.Count})");
            foreach (var a in g.Value) sub.DropDownItems.Add(Item(a, g.Key));
            poseMenu.Items.Add(sub);
        }
        // 保存したポーズのうち、エイリアスにないもの
        var used = new HashSet<string>(groups.SelectMany(g => g.Value).Where(a => a.Kind == "pose").Select(a => a.Target));
        var dir = Path.Combine(Here, "poses");
        var saved = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(x => !used.Contains(x)).OrderBy(x => x).ToList() : new List<string>();
        if (saved.Count > 0)
        {
            poseMenu.Items.Add(new ToolStripSeparator());
            var sub = new ToolStripMenuItem(T($"保存したポーズ(未登録)  ({saved.Count})", $"Saved poses (not in aliases)  ({saved.Count})"));
            foreach (var x in saved) sub.DropDownItems.Add(new ToolStripMenuItem(x, null, (s, ev) => PickPose(x, new Alias { Name = x, Kind = "pose", Target = x }, null)));
            poseMenu.Items.Add(sub);
        }
        if (poseMenu.Items.Count == 0)
            poseMenu.Items.Add(new ToolStripMenuItem(T("(pose_aliases.txt にポーズがありません。▣ のビューアーで登録できます)", "(No poses in pose_aliases.txt. Register them in the viewer ▣)")) { Enabled = false });
    }

    void PickPose(string name, Alias a, string group)
    {
        int n = Cur;
        poseName[n] = name;
        poseAlias[n] = a;
        poseGroup[n] = group;
        RefreshButtons();
        var args = new Dictionary<string, object>();
        string cmd;
        if (a.Kind == "pose")
        {
            cmd = "pose";
            args["path"] = Path.Combine(Here, "poses", a.Target + ".json");
        }
        else if (a.Kind == "anim")
        {
            cmd = "anim";
            var path = Path.IsPathRooted(a.Target) ? a.Target : Path.Combine(Here, a.Target.Replace('/', '\\'));
            if (!File.Exists(path))
            {
                Log(T($"「{name}」のアニメーションがありません: {a.Target}", $"Animation for \"{name}\" is missing: {a.Target}"), n);
                if (a.Target.StartsWith("anim/") && File.Exists(Path.Combine(Here, "anim_sources.txt"))
                    && MessageBox.Show(this, T("このアニメーションはまだ取り込んでいないようです。ビューアーを開いて取り込みますか?",
                                               "This animation does not seem to be imported yet. Open the viewer to import it?"),
                                       "VrcPoseAliasesPanel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    StartViewer("-dialog import");
                return;
            }
            args["path"] = path;
            if (a.When != null)
            {
                if (a.When.StartsWith("f")) args["frame"] = double.Parse(a.When.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                else args["time"] = double.Parse(a.When, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else { Log(T($"この種類のエイリアスは使えません: {a.Kind}", $"This kind of alias is not supported: {a.Kind}"), n); return; }
        Bg(() =>
        {
            if (!oscposerOk && !OpenOscPoser(true)) return;
            OscPoser.Call(cmd, Port(n), args);
            Log(T($"「{name}」を OscPoser で開きました(送るのは ➤ か OscPoser の「送る」)", $"Opened \"{name}\" in OscPoser (send with ➤ or OscPoser's Send)"), n);
        });
    }

    void SavePose()
    {
        var name = Prompt(T("ポーズを保存", "Save pose"), T("名前(poses\\<名前>.json)", "Name (poses\\<name>.json)"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Log(T("名前に使えない文字があります", "The name has invalid characters")); return; }
        var path = Path.Combine(Here, "poses", name + ".json");
        if (File.Exists(path)) { Log(T($"もうあります: poses\\{name}.json", $"Already exists: poses\\{name}.json")); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        int n = Cur;
        Bg(() => { OscPoser.Call("save", Port(n), new Dictionary<string, object> { ["path"] = path }); Log(T($"保存しました: poses\\{name}.json", $"Saved: poses\\{name}.json"), n); });
    }

    string Prompt(string title, string label)
    {
        using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 90), Font = textFont, TopMost = TopMost })
        {
            var l = new Label { Text = label, Left = 10, Top = 10, AutoSize = true };
            var t = new TextBox { Left = 10, Top = 30, Width = 300 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 150, Top = 58, Width = 75 };
            var cancel = new Button { Text = T("キャンセル", "Cancel"), DialogResult = DialogResult.Cancel, Left = 235, Top = 58, Width = 75 };
            f.Controls.AddRange(new Control[] { l, t, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }
    }

    // ---- ビューアー ----

    void OpenViewer()
    {
        var extra = "";
        int n = Cur;
        var a = poseAlias[n];
        if (a != null && poseGroup[n] == null && a.Kind == "pose") extra = $"-pose \"{a.Target}\"";
        else if (a != null && poseGroup[n] != null) extra = $"-alias \"{a.Name}\" -in \"{(poseGroup[n] == "" ? "ROOT" : poseGroup[n])}\"";
        StartViewer(extra);
    }

    void StartViewer(string extra)
    {
        if (!File.Exists(ViewerExe)) { Log(T("ビューアーがありません: ", "Viewer not found: ") + ViewerExe); return; }
        Process.Start(new ProcessStartInfo(ViewerExe, $"-agent \"{Here}\" {extra}") { WorkingDirectory = Path.GetDirectoryName(ViewerExe), UseShellExecute = false });
        Log(T("ビューアーを開きました", "Opened the viewer"));
    }

    // ---- VRChat ----

    void LaunchVrchat()
    {
        int n = Cur;
        var exe = VrchatLauncher.Find(st.LaunchExe);
        if (exe == null) { Log(T("VRChat の launch.exe が見つかりません(⚙ で場所を指定してください)", "VRChat's launch.exe not found (set it in ⚙)")); return; }
        if (Win.PidOfUdpPort(Port(n)) != 0 && MessageBox.Show(this, T($"OSC のポート {Port(n)} はもう使われています(プロフィール {n} の VRChat が動いている?)。起動しますか?",
                                                                     $"OSC port {Port(n)} is already in use (is VRChat profile {n} running?). Launch anyway?"),
                                                                    "VrcPoseAliasesPanel", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            VrchatLauncher.Launch(exe, n, st.NoVr);
            Log(T($"プロフィール {n} の VRChat を起動しました(OSC {Port(n)})。窓のタイトルは「VRChat [{n}]」に", $"Launched VRChat profile {n} (OSC {Port(n)}). Its window title becomes \"VRChat [{n}]\""), n);
            Task.Run(() => KeepTitle(n));
        }
        catch (Exception e) { Log(T("起動できません: ", "Cannot launch: ") + e.Message); }
    }

    // 起動したての VRChat の窓を待って、タイトルに番号を付ける(起動の途中で戻されても付け直すよう 5 分見張る)
    void KeepTitle(int n)
    {
        var t0 = DateTime.Now;
        while ((DateTime.Now - t0).TotalMinutes < 5)
        {
            var h = Win.FindVrchat(Port(n));
            if (h != IntPtr.Zero && Win.Title(h) != $"VRChat [{n}]") Win.SetTitle(h, $"VRChat [{n}]");
            Thread.Sleep(3000);
        }
    }

    void Screenshot(int n)
    {
        var me = Handle;
        Bg(() =>
        {
            Bitmap b;
            if (n < 0) b = Win.CaptureRect(SystemInformation.VirtualScreen);
            else
            {
                var h = Win.FindVrchat(Port(n));
                if (h == IntPtr.Zero) { Log(T($"プロフィール {n} の VRChat の窓が見つかりません", $"No VRChat window for profile {n}")); return; }
                Win.BringToFront(h);
                Thread.Sleep(400);
                b = Win.Capture(h);
            }
            var dir = Path.Combine(Here, "shots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}" + (n >= 0 ? $"_p{n}" : "") + ".png");
            using (b) b.Save(path, ImageFormat.Png);
            Invoke((Action)(() => { Clipboard.SetText(path); Win.BringToFront(me); }));
            Log(T("撮りました(パスをコピー): ", "Saved (path copied): ") + path);
        });
    }

    void SendChat()
    {
        var text = chatBox.Text;
        if (text.Trim() == "") return;
        int n = Cur;
        try
        {
            Osc.Chat(Port(n), text);
            chatBox.Clear();
            Log(T("チャット: ", "Chat: ") + text, n);
        }
        catch (Exception e) { Log(e.Message); }
    }

    void Dropped(string[] files)
    {
        var vrm = files.FirstOrDefault(f => f.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase));
        if (vrm == null) { Log(T("ドロップできるのは .vrm です(OscPoser のこの相手の VRM にする)", "Drop a .vrm (it becomes this target's VRM in OscPoser)")); return; }
        int n = Cur;
        Bg(() => { OscPoser.Call("vrm", Port(n), new Dictionary<string, object> { ["path"] = vrm }); Log(T("OscPoser で VRM を開きました: ", "Opened the VRM in OscPoser: ") + Path.GetFileName(vrm), n); });
    }

    // ---- 設定 ----

    void ShowSettings()
    {
        using (var f = new Form { Text = T("設定", "Settings"), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(520, 260), Font = textFont, TopMost = TopMost })
        {
            int y = 10;
            TextBox PathRow(string label, string value, string filter)
            {
                f.Controls.Add(new Label { Text = label, Left = 10, Top = y, AutoSize = true });
                var t = new TextBox { Left = 10, Top = y + 20, Width = 410, Text = value };
                var b = new Button { Text = T("参照…", "Browse..."), Left = 428, Top = y + 18, Width = 80 };
                b.Click += (s, e) =>
                {
                    using (var d = new OpenFileDialog { Filter = filter })
                        if (d.ShowDialog(f) == DialogResult.OK) t.Text = d.FileName;
                };
                f.Controls.Add(t);
                f.Controls.Add(b);
                y += 52;
                return t;
            }
            var os = PathRow(T("OscPoser.exe の場所(空なら自動で探す: このフォルダの中と横の OscPoser*、D:\\osc_poser など)",
                               "OscPoser.exe (empty: search OscPoser* in and next to this folder, D:\\osc_poser, ...)"), st.OscPoserExe, "OscPoser.exe|OscPoser.exe|*.exe|*.exe");
            var la = PathRow(T("VRChat の launch.exe の場所(空なら Steam のライブラリから探す)", "VRChat launch.exe (empty: search the Steam libraries)"), st.LaunchExe, "launch.exe|launch.exe|*.exe|*.exe");
            var novr = new CheckBox { Text = T("デスクトップモードで起動する(--no-vr)", "Launch in desktop mode (--no-vr)"), Left = 10, Top = y, AutoSize = true, Checked = st.NoVr };
            f.Controls.Add(novr);
            y += 26;
            f.Controls.Add(new Label { Text = T("OscPoser の操作の口のポート(-api)", "OscPoser API port (-api)"), Left = 10, Top = y + 3, AutoSize = true });
            var api = new NumericUpDown { Left = 300, Top = y, Width = 80, Minimum = 1024, Maximum = 65535, Value = st.ApiPort };
            f.Controls.Add(api);
            y += 30;
            f.Controls.Add(new Label { Text = T("画面の言語(次に起動したときから)", "Language (from the next start)"), Left = 10, Top = y + 3, AutoSize = true });
            var langs = new[] { "", "ja", "en" };
            var lang = new ComboBox { Left = 300, Top = y, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            lang.Items.AddRange(new object[] { T("Windows に合わせる", "Same as Windows"), "日本語", "English" });
            lang.SelectedIndex = Math.Max(0, Array.IndexOf(langs, st.Lang));
            f.Controls.Add(lang);
            y += 34;
            f.Controls.Add(new Label { Text = $"VrcPoseAliasesPanel {Version}", Left = 10, Top = y + 5, AutoSize = true, ForeColor = Color.Gray });
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 340, Top = y, Width = 80 };
            var cancel = new Button { Text = T("キャンセル", "Cancel"), DialogResult = DialogResult.Cancel, Left = 428, Top = y, Width = 80 };
            f.Controls.Add(ok);
            f.Controls.Add(cancel);
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            f.ClientSize = new Size(520, y + 36);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            st.OscPoserExe = os.Text.Trim().Trim('"');
            st.LaunchExe = la.Text.Trim().Trim('"');
            st.NoVr = novr.Checked;
            st.ApiPort = OscPoser.ApiPort = (int)api.Value;
            st.Lang = langs[lang.SelectedIndex];
            st.Save();
            Log(T("設定を保存しました", "Settings saved"));
        }
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Win.SetProcessDPIAware();
        var lang = Settings.Load().Lang;
        if (lang == "ja") L.Ja = true;
        else if (lang == "en") L.Ja = false;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PanelForm());
    }
}
