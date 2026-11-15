// 選べるポーズの一覧: ドロップした .unitypackage・.zip の中のアニメーションとフォルダの下のアニメーション、
// pose_aliases.txt のエイリアス(グループつき)、emote_poses.json のエモート、
// anim/ と poses/ のうちエイリアス・エモートに登録されていないもの、pose_state.json(pose_send.py が最後に送った値)。
// pose_aliases.txt の書き方は、そのファイルの先頭のコメントと pose_aliases.py を参照(読み方を合わせてある)。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class PoseEntry
{
    public string Group;     // エイリアス・エモート・保存したポーズ・いまの任意ポーズ
    public string Name;
    public string Kind;      // emote・anim・pose・state・pkg(.unitypackage の中のアニメーション)
    public string Target;
    public float? Time;      // 秒
    public float? Frame;     // フレーム(Time がなければ)
    public string Label;     // 一覧に出す文字
    public string PackageFile;          // pkg: .unitypackage のファイル。ドロップしたフォルダのアニメーションでは、そのフォルダ
    public UnityPackage.Anim PkgAnim;   // pkg: 中のアニメーション
    public string AliasGroup;           // エイリアスのグループ(ルートは ""。エイリアスでなければ null)
    public string When;                 // エイリアスの時刻の書き方("1.6"・"f30"・null)
    public string AliasKey => AliasGroup == null ? null : $"{Name}\n{Kind}\n{Target}\n{When}";   // AliasFile.Item.Key と同じ
}

public class PoseLibrary
{
    public string AgentDir;      // pose_send.py などのあるフォルダ(D:\vrchat_osc_agent)
    public string ProjectDir;    // Unity のプロジェクト(相対パスの .anim はここから)
    public readonly List<PoseEntry> Entries = new List<PoseEntry>();
    public readonly List<string> Errors = new List<string>();
    readonly Dictionary<string, string> emoteClips = new Dictionary<string, string>();
    readonly Dictionary<string, string> emoteNames = new Dictionary<string, string>();
    readonly Dictionary<string, string> emoteAnims = new Dictionary<string, string>();   // anim/emote/ のコピー
    public AliasFile Aliases;
    public AnimSources Sources;   // anim_sources.txt(エイリアスの anim/<フォルダ>/... をどこから取り込むか)
    public List<string> AliasGroups => Aliases?.Groups ?? new List<string>();
    readonly List<PoseEntry> packageEntries = new List<PoseEntry>();

    // 任意ポーズの体の位置・傾きの幅(pose_muscles.json の値。読めなければ既定)
    float yRange = 1.5f, xzRange = 1.5f, yawRange = 90f, pitchRange = 180f, rollRange = 90f;
    const float BaseY = 1f;

    public string AliasPath => Path.Combine(AgentDir, "pose_aliases.txt");
    public string StatePath => Path.Combine(AgentDir, "pose_state.json");
    public string SaveDir => Path.Combine(AgentDir, "anim");   // アニメーションを保存する所

    public void Reload()
    {
        Entries.Clear();
        Errors.Clear();
        emoteClips.Clear();
        emoteNames.Clear();
        emoteAnims.Clear();
        Try("pose_muscles.json", LoadRanges);
        Try("emote_poses.json", LoadEmotes);
        Try("pose_aliases.txt", LoadAliases);
        Sources = null;
        Try("anim_sources.txt", () => { Sources = new AnimSources(AgentDir, Aliases); Errors.AddRange(Sources.Errors); });
        Entries.AddRange(packageEntries);
        // anim/ と poses/ は、エイリアスとエモートに登録されていないものだけ(anim/ をフォルダとしてドロップすれば全部見られる)
        var aliases = Entries.Where(x => x.AliasGroup != null).ToList();
        var usedAnims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in aliases.Where(x => x.Kind == "anim")) usedAnims.Add(Full(AnimPath(e.Target)));
        foreach (var a in emoteAnims.Values) usedAnims.Add(Full(Path.Combine(AgentDir, a)));
        if (Directory.Exists(SaveDir))
            foreach (var f in Directory.GetFiles(SaveDir, "*.anim", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (usedAnims.Contains(Full(f))) continue;
                var rel = "anim/" + f.Substring(SaveDir.Length + 1).Replace('\\', '/');
                Entries.Add(new PoseEntry { Group = "保存したアニメ(未登録)", Name = rel, Kind = "anim", Target = rel, Label = rel.Substring(5, rel.Length - 10) });
            }
        foreach (var kv in emoteClips)
            Entries.Add(new PoseEntry { Group = "エモート", Name = kv.Key, Kind = "emote", Target = kv.Key, Label = $"{kv.Key,2} {emoteNames[kv.Key]}" });
        var usedPoses = new HashSet<string>(aliases.Where(x => x.Kind == "pose").Select(x => x.Target));
        var poses = Path.Combine(AgentDir, "poses");
        if (Directory.Exists(poses))
            foreach (var f in Directory.GetFiles(poses, "*.json"))
            {
                var n = Path.GetFileNameWithoutExtension(f);
                if (usedPoses.Contains(n)) continue;
                Entries.Add(new PoseEntry { Group = "保存したポーズ(未登録)", Name = n, Kind = "pose", Target = n, Label = n });
            }
        // pose_send.py が送った値の控え。相手ごと(pose_state.json がプロフィール 0、pose_state_9010.json が 1 …)
        var states = Directory.Exists(AgentDir) ? Directory.GetFiles(AgentDir, "pose_state*.json").OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
        foreach (var f in states)
        {
            var n = Path.GetFileNameWithoutExtension(f);
            int port = n == "pose_state" ? 9000 : int.TryParse(n.Substring(n.LastIndexOf('_') + 1), out var q) ? q : 0;
            var who = port >= 9000 && (port - 9000) % 10 == 0 ? $"プロフィール {(port - 9000) / 10}" : $"ポート {port}";
            Entries.Add(new PoseEntry { Group = "いまの任意ポーズ", Name = n, Kind = "state", Target = f, Label = $"{Path.GetFileName(f)}({who}に送った値。変われば読み直す)" });
        }
    }

    static string Full(string p)
    {
        try { return Path.GetFullPath(p); }
        catch (Exception) { return p; }
    }

    void Try(string what, Action f)
    {
        try { f(); }
        catch (Exception e) { Errors.Add($"{what}: {e.Message}"); }
    }

    void LoadRanges()
    {
        var f = Path.Combine(AgentDir, "pose_muscles.json");
        if (!File.Exists(f)) return;   // なければ既定の幅
        var t = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(f));
        if (t.TryGetValue("y_range", out var y)) yRange = Convert.ToSingle(y);
        if (t.TryGetValue("xz_range", out var xz)) xzRange = Convert.ToSingle(xz);
        if (t.TryGetValue("rot_ranges_deg", out var r) && r is Dictionary<string, object> rr)
        {
            yawRange = Convert.ToSingle(rr["PoseYaw"]);
            pitchRange = Convert.ToSingle(rr["PosePitch"]);
            rollRange = Convert.ToSingle(rr["PoseRoll"]);
        }
    }

    void LoadEmotes()
    {
        var f = Path.Combine(AgentDir, "emote_poses.json");
        if (!File.Exists(f)) return;   // 便利セットのエモートの表(なければエモートの一覧を出さない)
        var t = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(f));
        var keys = new List<string>(t.Keys);
        keys.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b)));
        foreach (var k in keys)
        {
            var e = (Dictionary<string, object>)t[k];
            emoteClips[k] = (string)e["clip"];
            emoteNames[k] = e.TryGetValue("name", out var n) ? (string)n : "";
            if (e.TryGetValue("anim", out var a)) emoteAnims[k] = (string)a;
        }
    }

    void LoadAliases()
    {
        Aliases = new AliasFile(AliasPath);
        Errors.AddRange(Aliases.Errors);
        foreach (var it in Aliases.Items)
        {
            var e = new PoseEntry { Group = "エイリアス", Name = it.Name, Kind = it.Kind, Target = it.Target, AliasGroup = it.Group, When = it.When };
            if (it.When != null)
            {
                if (it.When.StartsWith("f")) e.Frame = float.Parse(it.When.Substring(1), CultureInfo.InvariantCulture);
                else e.Time = float.Parse(it.When, CultureInfo.InvariantCulture);
            }
            var when = it.When != null ? " " + it.When : "";
            e.Label = $"{e.Name}  ({e.Kind} {Path.GetFileNameWithoutExtension(e.Target)}{when})";
            Entries.Add(e);
        }
    }

    // 空白で区切る。"..." と '...' は 1 語。引用符の外の # から後はコメント。\ はそのまま(Windows のパス)。
    public static List<string> SplitWords(string line)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        bool inWord = false;
        char quote = '\0';
        foreach (var c in line)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else sb.Append(c);
                continue;
            }
            if (c == '#') break;
            if (c == '"' || c == '\'') { quote = c; inWord = true; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (inWord) { words.Add(sb.ToString()); sb.Clear(); inWord = false; }
                continue;
            }
            sb.Append(c);
            inWord = true;
        }
        if (inWord) words.Add(sb.ToString());
        return words;
    }

    // ドロップした .unitypackage・.zip の中のアニメーションを一覧の先頭に足す(同じパッケージは入れ替え)
    public void AddPackage(string file, List<UnityPackage.Anim> anims)
    {
        packageEntries.RemoveAll(x => x.PackageFile == file);
        // 一覧の名前は、全部に共通するフォルダを省き、.anim を取ったもの
        var common = anims.Count > 0 ? anims[0].PathName.Substring(0, anims[0].PathName.LastIndexOf('/') + 1) : "";
        foreach (var a in anims)
            while (common.Length > 0 && !a.PathName.StartsWith(common))
                common = common.Substring(0, common.LastIndexOf('/', common.Length - 2) + 1);
        var group = $"パッケージ: {Path.GetFileNameWithoutExtension(file)}";
        packageEntries.InsertRange(0, anims.Select(a => new PoseEntry
        {
            Group = group, Name = a.PathName, Kind = "pkg", Target = a.PathName, PackageFile = file, PkgAnim = a,
            Label = a.PathName.Substring(common.Length, a.PathName.Length - common.Length - 5),
        }));
        Reload();
    }

    // ドロップしたフォルダの下のアニメーション(files は絶対パス)を一覧の先頭に足す(同じフォルダは入れ替え)
    public void AddFolder(string dir, List<string> files)
    {
        packageEntries.RemoveAll(x => x.PackageFile == dir);
        var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        packageEntries.InsertRange(0, files.Select(f => new PoseEntry
        {
            Group = $"フォルダ: {Path.GetFileName(root.TrimEnd('\\'))}", Name = f, Kind = "anim", Target = f, PackageFile = dir,
            Label = f.Substring(root.Length, f.Length - root.Length - 5).Replace('\\', '/'),
        }));
        Reload();
    }

    // .anim のパス。相対なら、pose_aliases.txt の所(anim/ など)にあればそれ、なければ Unity プロジェクトから
    public string AnimPath(string target)
    {
        if (Path.IsPathRooted(target)) return target;
        var rel = target.Replace('/', Path.DirectorySeparatorChar);
        var local = Path.Combine(AgentDir, rel);
        var project = Path.Combine(ProjectDir, rel);
        if (File.Exists(local)) return local;
        // どちらにもなければ、anim/... はこちらの場所を返す(「ファイルがありません」に正しい場所が出るように)
        return File.Exists(project) || !target.StartsWith("anim/") ? project : local;
    }

    // 保存するときの名前: [パッケージ名]-[ファイル名].anim。パッケージ名は Assets/ のすぐ下のフォルダ
    // (なければ .unitypackage のファイル名か、ドロップしたフォルダの名前)。使えない文字は _ に。
    public static string SaveName(string assetPath, string packageFile)
    {
        var parts = assetPath.Replace('\\', '/').Split('/');
        int i = Array.FindIndex(parts, x => x == "Assets");
        string pkg = i >= 0 && i + 2 < parts.Length ? parts[i + 1]
            : packageFile != null ? Path.GetFileNameWithoutExtension(packageFile.TrimEnd('\\', '/')) : "anim";
        var name = $"{pkg}-{Path.GetFileNameWithoutExtension(parts[parts.Length - 1])}.anim";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    public string ClipPathOf(PoseEntry e)
    {
        if (e.Kind == "anim") return AnimPath(e.Target);
        if (e.Kind == "emote")
        {
            if (!emoteClips.TryGetValue(e.Target, out var clip)) throw new Exception($"エモート {e.Target} が emote_poses.json にありません");
            // anim/emote/ のコピー(extract_emote_poses.py --copy-to)があればそれを
            if (emoteAnims.TryGetValue(e.Target, out var copy) && File.Exists(Path.Combine(AgentDir, copy))) return Path.Combine(AgentDir, copy);
            return AnimPath(clip);
        }
        return null;
    }

    public string PosePathOf(PoseEntry e) => e.Kind == "state" ? e.Target : Path.Combine(AgentDir, "poses", e.Target + ".json");

    // 保存したポーズ(筋肉の名前 → 値。体の位置・傾きは任意ポーズの -1〜1 の値)を姿勢にする
    public PoseData PoseFromJson(string path)
    {
        var t = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
        var p = new PoseData();
        float G(string k) => t.TryGetValue(k, out var v) ? Convert.ToSingle(v) : 0f;
        foreach (var kv in t)
            if (Muscles.Index.ContainsKey(kv.Key))
                p.Muscles[kv.Key] = Convert.ToSingle(kv.Value);
        p.RootT = new Vector3(G("(RootT.x)") * xzRange, BaseY + G("(RootT.y)") * yRange, G("(RootT.z)") * xzRange);
        // Unity の YXZ(Yaw → Pitch → Roll)= Quaternion.Euler(pitch, yaw, roll)
        p.RootQ = Quaternion.Euler(G("(RootQ.pitch)") * pitchRange, G("(RootQ.yaw)") * yawRange, G("(RootQ.roll)") * rollRange);
        return p;
    }
}
