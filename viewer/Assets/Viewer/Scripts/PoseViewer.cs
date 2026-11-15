// VRM ポーズビューアー: VRM を読み込み、.anim(人型)のある時刻の姿勢や、保存したポーズを当てて見る。
// 姿勢は HumanPoseHandler.SetHumanPose で当てるので、Unity(VRChat)の筋肉の計算そのまま。
//
// 起動オプション(どれも省略可):
//   -vrm FILE  -anim FILE  -time 秒  -frame N  -alias 名前  -pose 名前か FILE
//   -agent DIR(pose_aliases.txt などのある所。既定: exe から上にたどって pose_muscles.json か pose_aliases.txt のある所)
//   -project DIR(Unity プロジェクト。既定 D:\Unity\Unity VRM project 2)
//   -save(登録)  -group G(ROOT はルート)  -name 名前  -rename 名前  -tags "ROOT,G1"  -unregister  -in G(-alias のグループ)  -dialog register|edit|import(試し用。register・edit は -alias と組み合わせる)
//   -package FILE.unitypackage か .zip か DIR  -pick 文字(パッケージの中で、その文字を含む最初のアニメーションを選ぶ)  -save(anim/ に保存)
//   -import FILE(.zip・.unitypackage・フォルダから、エイリアスに登録したアニメーションを anim/ に取り込む。anim_sources.txt)
//   -view front|right|left|back|top  -inplace 0|1  -noui  -shot FILE.png(撮って)  -quit(撮ったら終わる)
//   -handinfo FILE.json(姿勢を当てたときの右手などの位置・向きを書き出す。持った物を手に合わせる計算用。
//                       -inplace を書かなければ、体の左右・前後もそのまま当てる。-quit で書いたら終わる)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UniVRM10;
using UnityEngine;

public partial class PoseViewer : MonoBehaviour
{
    const string DefaultProject = @"D:\Unity\Unity VRM project 2";
    const string DefaultAgent = @"D:\vrchat_osc_agent";
    const float PanelW = 340, TopH = 84, BottomH = 76;

    PoseLibrary lib;
    Camera cam;
    Material lineMat;
    Font font;

    // VRM
    Vrm10Instance vrm;
    HumanPoseHandler handler;
    HumanPose pose;
    string vrmPath;
    float modelHeight = 1.5f;
    readonly Dictionary<string, Vector3> rest = new Dictionary<string, Vector3>();   // 読み込んだときの(T ポーズの)骨の位置
    readonly Dictionary<string, Quaternion> restRot = new Dictionary<string, Quaternion>();   // 同じく骨の向き(根元から)
    bool loading;
    string viewAfterLoad = "front";   // 最初の VRM を読んだあとの視点(-view)

    // 姿勢の元: アニメーション(clip)か、決まった姿勢(staticPose)
    AnimClip clip;
    PoseData staticPose;
    string sourceLabel = "";
    PoseEntry current;          // 一覧から選んだもの(なければ null)
    string currentPosePath;     // 決まった姿勢のファイル
    DateTime poseFileTime;
    float nextPoll;
    DateTime aliasFileTime;     // pose_aliases.txt・anim_sources.txt の更新時刻(メモ帳などで書き換えたら読み直す)
    float nextAliasPoll;
    float time;
    bool playing, loop = true, inPlace = true;
    float speed = 1f;

    // カメラ
    Vector3 target;
    float yaw = 180, pitch = 5, dist = 3;
    bool dragging;
    Vector3 lastMouse;

    // 画面
    bool showUi = true;
    string filter = "";
    Vector2 scroll;
    string status = "";
    bool styled;
    GUIStyle plain;   // 折り返さず、はみ出しても切らないラベル(「_」の下が欠けないように)
    readonly Dictionary<string, string> args = new Dictionary<string, string>();
    readonly Dictionary<string, bool> opened = new Dictionary<string, bool>();   // 一覧の見出しの開け閉め
    Dialog dlg;   // 登録・編集のダイアログ(出ていなければ null)
    ImportDialog imp;   // 取り込みのダイアログ(出ていなければ null)

    class ImportDialog
    {
        public string Src = "", Message = "";
        public bool Busy;
        public Vector2 Scroll;
    }

    class Dialog
    {
        public bool Edit;            // false: 登録、true: 編集(current がエイリアス)
        public string Name = "", Message = "";
        public HashSet<string> Tags = new HashSet<string> { "" };   // 入れるグループ(タグ)。ルートは ""
        public string NewTag = "";   // 新しいグループ(書けば、そのグループも作って入れる)
        public bool UseTime = true, ConfirmDelete;
        public bool Copy;            // 登録: anim/(アニメーション)か poses/(ポーズ)にコピーする
        public string FileName = ""; // コピーするときのファイル名(拡張子なし)。エイリアス名とは別(日本語を避けられるように)
        public Vector2 Scroll;
    }

    void Awake()
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 1; i < a.Length; i++)
        {
            if (!a[i].StartsWith("-")) continue;
            var key = a[i].Substring(1).ToLowerInvariant();
            args[key] = i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[++i] : "1";
        }

        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
        cam.nearClipPlane = 0.02f;
        cam.fieldOfView = 30;
        var light = new GameObject("Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.transform.rotation = Quaternion.Euler(30, 160, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        lineMat.SetInt("_ZWrite", 0);
        Camera.onPostRender += DrawGrid;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Meiryo UI", "Meiryo", "Yu Gothic UI", "MS UI Gothic" }, 14);
        Application.targetFrameRate = 60;
    }

    void OnDestroy()
    {
        Camera.onPostRender -= DrawGrid;
        WinNative.UninstallDrop();
        handler?.Dispose();
    }

    IEnumerator Start()
    {
        L.Init(Arg("lang"));
        WinNative.InstallDrop();
        lib = new PoseLibrary
        {
            AgentDir = Arg("agent") ?? FindAgentDir(),
            ProjectDir = Arg("project") ?? DefaultProject,
        };
        lib.Reload();
        // 取り込み元(anim_sources.txt)があって、まだ一つも取り込んでいなければ、取り込みの画面を出す
        bool batch = Arg("shot") != null || Arg("quit") != null || Arg("handinfo") != null || Arg("ik") != null || Arg("noui") != null;
        if (Arg("import") != null)
        {
            var task = Import(Arg("import"));
            while (!task.IsCompleted) yield return null;
            Debug.Log("import: " + imp?.Message);
            if (Arg("quit") != null && Arg("shot") == null) Application.Quit();
        }
        else if (!batch && lib.Sources != null && lib.Sources.NothingImported && PlayerPrefs.GetInt(NoAskKey, 0) == 0)
            imp = new ImportDialog();
        if (Arg("inplace") != null) inPlace = Arg("inplace") != "0";
        if (Arg("noui") != null) showUi = false;
        viewAfterLoad = Arg("view") ?? "front";
        ResetView(viewAfterLoad);

        var path = Arg("vrm") ?? PlayerPrefs.GetString("vrm", "");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VRoid", "jilpa_mm_lolitaf.vrm");
            path = File.Exists(guess) ? guess : null;
        }
        if (path != null) LoadVrm(path);
        else status = L.T("VRM をこの窓にドロップするか、「VRM を開く」で選んでください", "Drop a VRM on this window, or choose one with \"Open VRM\"");

        if (Arg("alias") != null)
        {
            var inGroup = Arg("in") == "ROOT" ? "" : Arg("in");   // 試し用: 複数のグループにあるとき、どのグループのものか
            var e = lib.Entries.FirstOrDefault(x => x.Group == "エイリアス" && x.Name == Arg("alias") && (inGroup == null || x.AliasGroup == inGroup));
            if (e != null) Select(e);
            else status = L.T("エイリアスがありません: ", "No such alias: ") + Arg("alias");
        }
        else if (Arg("anim") != null) OpenClip(Arg("anim"));
        else if (Arg("pose") != null)
        {
            var p = Arg("pose");
            OpenPoseFile(File.Exists(p) ? p : Path.Combine(lib.AgentDir, "poses", p + ".json"));
        }
        if (Arg("package") != null)
        {
            var task = LoadPackage(Arg("package"));
            while (!task.IsCompleted) yield return null;
            var pick = Arg("pick");
            var e = lib.Entries.FirstOrDefault(x => x.PackageFile != null && (pick == null || x.Name.Contains(pick)));
            if (e != null) Select(e);
        }
        if (clip != null)
        {
            if (Arg("time") != null) time = Mathf.Clamp(float.Parse(Arg("time"), CultureInfo.InvariantCulture), 0, clip.Length);
            else if (Arg("frame") != null) time = Mathf.Clamp(float.Parse(Arg("frame"), CultureInfo.InvariantCulture) / clip.SampleRate, 0, clip.Length);
        }

        // 試し用: -save(既定の名前で登録)、-alias 名前 [-in G] -rename 新しい名前 / -tags "ROOT,G1,G2"(編集)、
        // -unregister(外す)、-dialog register|edit(ダイアログを開いたまま)。-tags はグループの「,」区切り(ROOT はルート)
        HashSet<string> TagsArg() => new HashSet<string>(Arg("tags").Split(',').Select(s => s.Trim() == "ROOT" ? "" : s.Trim()));
        if (Arg("save") != null)
        {
            OpenRegister();
            if (Arg("tags") != null) dlg.Tags = TagsArg();
            if (Arg("name") != null) dlg.Name = Arg("name");
            DoRegister(dlg);
            if (dlg != null) { status = dlg.Message; dlg = null; }
        }
        else if (Arg("rename") != null || Arg("tags") != null)
        {
            OpenEdit();
            if (Arg("rename") != null) dlg.Name = Arg("rename");
            if (Arg("tags") != null) dlg.Tags = TagsArg();
            DoChange(dlg);
            if (dlg != null) { status = dlg.Message; dlg = null; }
        }
        if (Arg("unregister") != null) { OpenEdit(); DoDelete(dlg); }
        if (Arg("dialog") == "register") OpenRegister();
        if (Arg("dialog") == "edit") OpenEdit();
        if (Arg("dialog") == "import") imp = imp ?? new ImportDialog();

        if (Arg("ik") != null)   // 手の IK(hand_ik.py から。HandIK.cs)
        {
            if (Arg("inplace") == null) inPlace = false;
            while (loading) yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            ApplyPose();
            RunIK(Arg("ik"));
            if (Arg("shot") == null && Arg("quit") != null) Application.Quit();
        }

        if (Arg("handinfo") != null)
        {
            if (Arg("inplace") == null) inPlace = false;
            while (loading) yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            ApplyPose();
            WriteHandInfo(Arg("handinfo"));
            if (Arg("shot") == null && Arg("quit") != null) Application.Quit();
        }

        if (Arg("shot") != null)
        {
            while (loading) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            ScreenCapture.CaptureScreenshot(Arg("shot"));
            for (int i = 0; i < 5; i++) yield return null;
            if (Arg("quit") != null) Application.Quit();
        }
    }

    string Arg(string key) => args.TryGetValue(key, out var v) ? v : null;

    static string FindAgentDir()
    {
        var d = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath));
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "pose_muscles.json")) || File.Exists(Path.Combine(d.FullName, "pose_aliases.txt"))) return d.FullName;
        return DefaultAgent;
    }

    // ---- 読み込み ----

    async void LoadVrm(string path)
    {
        if (loading) return;
        loading = true;
        status = L.T("読み込み中: ", "Loading: ") + Path.GetFileName(path);
        try
        {
            var inst = await Vrm10.LoadPathAsync(path, canLoadVrm0X: true,
                controlRigGenerationOption: ControlRigGenerationOption.None, showMeshes: true);
            if (inst == null) throw new Exception(L.T("VRM として読めませんでした", "Could not read it as a VRM"));
            if (vrm != null) Destroy(vrm.gameObject);
            handler?.Dispose();
            vrm = inst;
            var animator = inst.GetComponent<Animator>();
            handler = new HumanPoseHandler(animator.avatar, inst.transform);
            handler.GetHumanPose(ref pose);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            modelHeight = head != null ? head.position.y + 0.12f : 1.5f;
            rest.Clear();
            restRot.Clear();
            foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Hips,
                                      HumanBodyBones.RightHand, HumanBodyBones.RightShoulder })
            {
                var t = animator.GetBoneTransform(b);
                if (t != null)
                {
                    rest[b.ToString()] = inst.transform.InverseTransformPoint(t.position);
                    restRot[b.ToString()] = Quaternion.Inverse(inst.transform.rotation) * t.rotation;
                }
            }
            vrmPath = path;
            if (Arg("handinfo") == null && Arg("ik") == null)   // hand_fit.py・hand_ik.py から呼ばれたときは、ふだんの「前に開いた VRM」を変えない
            {
                PlayerPrefs.SetString("vrm", path);
                PlayerPrefs.Save();
            }
            status = $"VRM: {Path.GetFileName(path)}";
            ResetView(viewAfterLoad);
            viewAfterLoad = "front";
        }
        catch (Exception e)
        {
            status = L.T("VRM を読めません: ", "Cannot read the VRM: ") + e.Message;
            Debug.LogException(e);
        }
        loading = false;
    }

    bool OpenClip(string path, PoseEntry entry = null)
    {
        if (!File.Exists(path)) { status = L.T("ファイルがありません: ", "File not found: ") + path; return false; }
        try
        {
            SetClip(AnimClip.Load(path), entry);
            return true;
        }
        catch (Exception e)
        {
            status = L.T("アニメーションを読めません: ", "Cannot read the animation: ") + e.Message;
            return false;
        }
    }

    void SetClip(AnimClip c, PoseEntry entry)
    {
        var path = c.Path;
        clip = c;
        staticPose = null;
        currentPosePath = null;
        current = entry;
        time = clip.Length;
        if (entry?.Time != null) time = Mathf.Clamp(entry.Time.Value, 0, clip.Length);
        else if (entry?.Frame != null) time = Mathf.Clamp(entry.Frame.Value / clip.SampleRate, 0, clip.Length);
        playing = false;
        sourceLabel = (entry != null ? $"[{entry.Name}] " : "") + Path.GetFileName(path);
        status = L.T($"アニメーション: {Path.GetFileName(path)}(曲線 {clip.Curves.Count} 本、{clip.Length:0.###} 秒)", $"Animation: {Path.GetFileName(path)} ({clip.Curves.Count} curves, {clip.Length:0.###} s)");
    }

    // .unitypackage・.zip か、フォルダ(Unity に展開したアセットなど)を読み、中のアニメーションを一覧に出す(読むのは別のスレッドで)
    async System.Threading.Tasks.Task LoadPackage(string path)
    {
        path = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(path);
        status = L.T("読んでいます: ", "Reading: ") + name;
        try
        {
            int n;
            if (Directory.Exists(path))
            {
                var files = await System.Threading.Tasks.Task.Run(() =>
                    Directory.GetFiles(path, "*.anim", SearchOption.AllDirectories).Where(IsTextAnim).OrderBy(f => f, StringComparer.Ordinal).ToList());
                lib.AddFolder(path, files);
                n = files.Count;
            }
            else
            {
                var anims = await System.Threading.Tasks.Task.Run(() => Archive.ReadAnims(path));
                lib.AddPackage(path, anims);
                n = anims.Count;
            }
            filter = "";
            scroll = Vector2.zero;
            status = n > 0
                ? L.T($"{name}: アニメーション {n} 個(左の一覧の先頭)", $"{name}: {n} animations (at the top of the list)")
                : L.T($"{name}: テキスト形式のアニメーション(.anim)がありません", $"{name}: no text-format animations (.anim)");
        }
        catch (Exception e)
        {
            status = L.T("パッケージを読めません: ", "Cannot read the package: ") + e.Message;
            Debug.LogException(e);
        }
    }

    DateTime AliasFilesTime()
    {
        try
        {
            var a = File.Exists(lib.AliasPath) ? File.GetLastWriteTime(lib.AliasPath) : DateTime.MinValue;
            var f = Path.Combine(lib.AgentDir, "anim_sources.txt");
            var b = File.Exists(f) ? File.GetLastWriteTime(f) : DateTime.MinValue;
            return a > b ? a : b;
        }
        catch (IOException) { return aliasFileTime; }
    }

    // pose_aliases.txt をメモ帳で開く(保存すれば、見張っているので一覧が読み直される)
    void EditAliasFile()
    {
        try
        {
            if (!File.Exists(lib.AliasPath))
                File.WriteAllText(lib.AliasPath, L.T("# ポーズのエイリアス:  エイリアス名  種類(anim・pose)  対象  [時刻]\n", "# Pose aliases:  name  kind (anim, pose)  target  [time]\n"), new System.Text.UTF8Encoding(false));
            System.Diagnostics.Process.Start("notepad.exe", $"\"{lib.AliasPath}\"");
            status = L.T("pose_aliases.txt をメモ帳で開きました(保存すると一覧が読み直されます)", "Opened pose_aliases.txt in Notepad (the list reloads when you save)");
        }
        catch (Exception e) { status = L.T("メモ帳を開けません: ", "Cannot open Notepad: ") + e.Message; }
    }

    // ---- 取り込み(anim_sources.txt) ----

    string NoAskKey
    {
        get
        {
            uint h = 2166136261;   // フォルダごとに覚える(FNV-1a)
            foreach (var c in Path.GetFullPath(lib.AgentDir).ToLowerInvariant()) h = (h ^ c) * 16777619;
            return $"importNoAsk_{h:x8}";
        }
    }

    // zip・.unitypackage・フォルダを読み、エイリアスに登録してあって anim/ にないアニメーションを取り込む
    async System.Threading.Tasks.Task Import(string path)
    {
        if (imp == null) imp = new ImportDialog();
        var d = imp;
        path = path.Trim().Trim('"').TrimEnd('\\', '/');
        d.Src = path;
        if (lib.Sources == null || lib.Sources.Sources.Count == 0) { d.Message = L.T("anim_sources.txt に取り込み元が書かれていません", "anim_sources.txt lists no sources"); return; }
        if (!File.Exists(path) && !Directory.Exists(path)) { d.Message = L.T("ありません: ", "Not found: ") + path; return; }
        if (!Archive.CanRead(path)) { d.Message = L.T("読めるのは .zip・.unitypackage・フォルダです", "Only .zip, .unitypackage or a folder can be read"); return; }
        d.Busy = true;
        d.Message = L.T($"読んでいます: {Path.GetFileName(path)}(大きいものは数十秒かかります)", $"Reading: {Path.GetFileName(path)} (large files take tens of seconds)");
        var sources = lib.Sources;
        try
        {
            var (report, n) = await System.Threading.Tasks.Task.Run(() =>
            {
                var anims = Archive.ReadAnims(path);
                var r = sources.Import(anims, out var written);
                return (r, written);
            });
            Reload();
            d.Message = string.Join("\n", report);
            status = n > 0 ? L.T($"{n} 個のアニメーションを anim/ に取り込みました", $"Imported {n} animations into anim/") : L.T("取り込めるものはありませんでした", "Nothing to import");
        }
        catch (Exception e)
        {
            d.Message = L.T("読めません: ", "Cannot read: ") + e.Message;
            Debug.LogException(e);
        }
        d.Busy = false;
    }

    void ImportWindow(int id)
    {
        var d = imp;
        var src = lib.Sources;
        GUILayout.Space(4);
        GUILayout.Label(L.T("pose_aliases.txt に登録したアニメーションのうち、anim/ にまだないものを、", "Animations registered in pose_aliases.txt but not yet in anim/ are imported"), plain);
        GUILayout.Label(L.T("入手したファイル(zip・.unitypackage・フォルダ)から取り込みます。登録したものだけを取り込みます。", "from the file you got (zip, .unitypackage or folder). Only registered ones are imported."), plain);
        GUILayout.Space(6);
        d.Scroll = GUILayout.BeginScrollView(d.Scroll, GUILayout.Height(110));
        if (src == null || src.Sources.Count == 0) GUILayout.Label(L.T("(anim_sources.txt に取り込み元がありません)", "(no sources in anim_sources.txt)"), plain);
        else
            foreach (var s in src.Sources)
            {
                GUILayout.BeginHorizontal();
                var state = s.Targets.Count == 0 ? L.T("登録なし", "none registered") : s.Missing.Count == 0 ? L.T($"✓ {s.Targets.Count} 個とも取り込み済み", $"✓ all {s.Targets.Count} imported") : L.T($"未取り込み {s.Missing.Count} / 登録 {s.Targets.Count} 個", $"{s.Missing.Count} of {s.Targets.Count} not imported");
                GUILayout.Label($"{s.Name}:  {state}", plain);
                GUILayout.FlexibleSpace();
                if (s.Url != null && GUILayout.Button(L.T("入手先を開く", "Get it"), GUILayout.Width(110))) Application.OpenURL(s.Url);
                GUILayout.EndHorizontal();
            }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.Label(L.T("ファイル", "File"), GUILayout.Width(60));
        GUI.enabled = !d.Busy;
        d.Src = GUILayout.TextField(d.Src);
        if (GUILayout.Button(L.T("参照…", "Browse…"), GUILayout.Width(70)))
        {
            var dir = d.Src != "" && File.Exists(d.Src) ? Path.GetDirectoryName(d.Src) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var f = WinNative.OpenFile(L.T("取り込むファイル", "File to import"), "zip, unitypackage", "*.zip;*.unitypackage", dir);
            if (f != null) d.Src = f;
        }
        if (GUILayout.Button(L.T("取り込む", "Import"), GUILayout.Width(90)) && d.Src.Trim() != "") _ = Import(d.Src);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label(L.T("この窓に zip などをドロップしても取り込めます。入手した zip は展開しなくてかまいません。", "You can also drop the zip etc. on this window. No need to extract the zip."), plain);
        if (d.Message != "")
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.5f);
            GUILayout.Label(d.Message);
            GUI.color = old;
        }
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        bool noAsk = PlayerPrefs.GetInt(NoAskKey, 0) != 0;
        bool nv = GUILayout.Toggle(noAsk, L.T("起動したときにこの画面を出さない(上の「取り込み…」からいつでも開けます)", "Don't show this at startup (\"Import…\" at the top opens it any time)"));
        if (nv != noAsk) { PlayerPrefs.SetInt(NoAskKey, nv ? 1 : 0); PlayerPrefs.Save(); }
        GUILayout.FlexibleSpace();
        GUI.enabled = !d.Busy;
        if (GUILayout.Button(L.T("閉じる", "Close"), GUILayout.Width(90))) imp = null;
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    // テキスト形式のアニメーションか(先頭に "!u!74 " のある YAML)
    static bool IsTextAnim(string file)
    {
        try
        {
            var buf = new byte[300];
            int n;
            using (var s = File.OpenRead(file)) n = s.Read(buf, 0, buf.Length);
            var head = System.Text.Encoding.UTF8.GetString(buf, 0, n).TrimStart('\uFEFF');   // 先頭に BOM があるものも
            return head.StartsWith("%YAML") && head.Contains("!u!74 ");
        }
        catch (IOException) { return false; }
    }

    // ---- エイリアスの登録・編集 ----

    bool Under(string path, string dir) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    bool AtEnd => clip != null && time >= clip.Length - 0.0005f;

    static string SafeFile(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // 登録のダイアログを出す。アニメーションは anim/ にコピーして(もう anim/ にあればそのまま)登録する
    void OpenRegister()
    {
        if (clip == null && staticPose == null) { status = L.T("登録する姿勢がありません", "Nothing to register"); return; }
        var d = new Dialog();
        if (current?.Kind == "emote")
        {
            var label = current.Label.Trim();   // "46 ピース" → "ピース"
            d.Name = label.Contains(" ") ? label.Substring(label.IndexOf(' ') + 1).Trim() : label;
        }
        else if (clip != null)
        {
            d.Copy = clip.PackageFile != null || !Under(clip.Path, lib.SaveDir);
            d.Name = d.Copy ? Path.GetFileNameWithoutExtension(PoseLibrary.SaveName(clip.Path, clip.PackageFile ?? current?.PackageFile))
                            : Path.GetFileNameWithoutExtension(clip.Path);
            d.FileName = d.Name;
        }
        else
        {
            d.Copy = currentPosePath == null || !Under(currentPosePath, Path.Combine(lib.AgentDir, "poses"));
            d.Name = current?.Kind == "pose" ? current.Target : "pose";
            d.FileName = $"pose_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
        // 同じ名前のエイリアスがもうあれば、時刻を付けた名前を既定に(同じ名前でも中身が違えば登録はできる)
        if (clip != null && !AtEnd && lib.Aliases != null && lib.Aliases.Items.Any(x => x.Name == d.Name)) d.Name += "_" + AliasFile.When(time);
        d.UseTime = clip != null && !AtEnd;
        dlg = d;
    }

    void OpenEdit()
    {
        if (current?.AliasGroup == null) return;
        dlg = new Dialog { Edit = true, Name = current.Name, UseTime = false, Tags = new HashSet<string>(lib.Aliases.TagsOf(current.AliasKey)) };
    }

    // 選んだタグと、新しく書いたグループ
    static HashSet<string> TagsFrom(Dialog d)
    {
        var tags = new HashSet<string>(d.Tags);
        if (d.NewTag.Trim() != "") tags.Add(d.NewTag.Trim().Trim('[', ']'));
        return tags;
    }

    static string TagNames(IEnumerable<string> tags) => string.Join(L.T("・", ", "), tags.Select(g => g == "" ? L.T("ルート", "root") : g));

    void DoRegister(Dialog d)
    {
        var name = d.Name.Trim();
        if (name == "" || name.Contains("\"")) { d.Message = L.T("名前を入れてください(\" は使えません)", "Enter a name (\" cannot be used)"); return; }
        var tags = TagsFrom(d);
        if (tags.Count == 0) { d.Message = L.T("グループ(ルートも含めて)を 1 つ以上選んでください", "Choose at least one group (root counts)"); return; }
        var it = new AliasFile.Item { Name = name };
        Action write = null;   // コピーは、登録できると分かってから
        if (current?.Kind == "emote") { it.Kind = "emote"; it.Target = current.Target; }
        else if (clip != null)
        {
            it.Kind = "anim";
            if (d.Copy)
            {
                var text = clip.Text ?? File.ReadAllText(clip.Path);
                var file = SafeFile(d.FileName.Trim());
                if (file == "") { d.Message = L.T("ファイル名を入れてください", "Enter a file name"); return; }
                var dest = Path.Combine(lib.SaveDir, file + ".anim");
                if (File.Exists(dest) && File.ReadAllText(dest) != text) { d.Message = L.T("anim/ に同じ名前の別のファイルがあります: ", "A different file with the same name is in anim/: ") + Path.GetFileName(dest); return; }
                if (!File.Exists(dest)) write = () => { Directory.CreateDirectory(lib.SaveDir); File.WriteAllText(dest, text, new System.Text.UTF8Encoding(false)); };
                it.Target = "anim/" + Path.GetFileName(dest);
            }
            else it.Target = Relative(clip.Path);
        }
        else
        {
            it.Kind = "pose";
            var poses = Path.Combine(lib.AgentDir, "poses");
            if (!d.Copy) it.Target = Path.GetFileNameWithoutExtension(currentPosePath);
            else
            {
                var file = SafeFile(d.FileName.Trim());
                if (file == "") { d.Message = L.T("ファイル名を入れてください", "Enter a file name"); return; }
                var dest = Path.Combine(poses, file + ".json");
                if (File.Exists(dest)) { d.Message = L.T("poses/ に同じ名前のファイルがあります: ", "A file with the same name is in poses/: ") + Path.GetFileName(dest); return; }
                var src = currentPosePath;
                write = () => { Directory.CreateDirectory(poses); File.Copy(src, dest); };
                it.Target = Path.GetFileNameWithoutExtension(dest);
            }
        }
        if (it.Kind != "pose" && d.UseTime && clip != null) it.When = AliasFile.When(time);
        bool existed = lib.Aliases.TagsOf(it.Key).Count > 0;
        try
        {
            write?.Invoke();
            lib.Aliases.Put(it, tags);   // 同じ中身がもうあれば、タグを足すだけ
        }
        catch (Exception e) { d.Message = e.Message; return; }
        AfterAliasChange(it.Key, tags, (existed ? L.T("グループを足しました: ", "Added groups: ") : L.T("登録しました: ", "Registered: ")) + $"{name} ({TagNames(lib.Aliases.TagsOf(it.Key))})");
    }

    void DoChange(Dialog d)
    {
        var name = d.Name.Trim();
        if (name == "" || name.Contains("\"")) { d.Message = L.T("名前を入れてください(\" は使えません)", "Enter a name (\" cannot be used)"); return; }
        var tags = TagsFrom(d);
        if (tags.Count == 0) { d.Message = L.T("グループを全部外すときは「エイリアスから外す」を使ってください", "To remove it from all groups, use \"Remove alias\""); return; }
        var it = new AliasFile.Item { Name = name, Kind = current.Kind, Target = current.Target,
                                      When = d.UseTime && clip != null ? (AtEnd ? null : AliasFile.When(time)) : current.When };
        try { lib.Aliases.Put(it, tags, current.AliasKey); }
        catch (Exception e) { d.Message = e.Message; return; }
        AfterAliasChange(it.Key, tags, L.T("変更しました: ", "Changed: ") + $"{name} ({TagNames(lib.Aliases.TagsOf(it.Key))})");
    }

    void DoDelete(Dialog d)
    {
        var name = current.Name;
        try { lib.Aliases.Remove(current.AliasKey); }
        catch (Exception e) { d.Message = e.Message; return; }
        dlg = null;
        lib.Reload();
        aliasFileTime = AliasFilesTime();
        current = null;
        status = L.T($"エイリアスから外しました: {name}(ファイルはそのまま。未登録の一覧に出ます)", $"Removed the alias: {name} (the file is kept and listed as not registered)");
    }

    // 書き換えたあと: 読み直して、そのエイリアスを選んだ状態に(今のグループにあればそれ、なければ入れたグループのもの。時刻はそのまま)
    void AfterAliasChange(string key, ICollection<string> tags, string message)
    {
        var here = current?.AliasGroup;
        dlg = null;
        lib.Reload();
        aliasFileTime = AliasFilesTime();   // 自分で書き換えた分は、見張りで読み直さない
        var all = lib.Entries.Where(x => x.AliasKey == key).ToList();
        var e = all.FirstOrDefault(x => x.AliasGroup == here) ?? all.FirstOrDefault(x => tags.Contains(x.AliasGroup)) ?? all.FirstOrDefault();
        if (e != null)
        {
            float keep = time;
            Select(e);
            if (clip != null) time = keep;
        }
        status = message;
    }

    void DialogWindow(int id)
    {
        var d = dlg;
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label(L.T("名前", "Name"), GUILayout.Width(80));
        d.Name = GUILayout.TextField(d.Name);
        GUILayout.EndHorizontal();
        if (!d.Edit && d.Copy)
        {
            bool anim = clip != null;
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("ファイル名", "File name"), GUILayout.Width(80));
            d.FileName = GUILayout.TextField(d.FileName);
            GUILayout.Label(anim ? ".anim" : ".json", GUILayout.Width(44));
            GUILayout.EndHorizontal();
            GUILayout.Label(L.T($"{(anim ? "anim/" : "poses/")} にこの名前でコピーして登録します(エイリアスの名前とは別)。", $"Copied into {(anim ? "anim/" : "poses/")} with this name, then registered (separate from the alias name)."), plain);
        }
        GUILayout.Space(4);
        GUILayout.Label(L.T("グループ(タグ): 押して ✓ を付けたグループに入れる。いくつでも。", "Groups (tags): it goes into the groups you check (any number)."), plain);
        d.Scroll = GUILayout.BeginScrollView(d.Scroll, GUILayout.Height(96));
        GUILayout.BeginHorizontal();
        int n = 0;
        foreach (var g in new[] { "" }.Concat(lib.AliasGroups))
        {
            bool on = d.Tags.Contains(g);
            var old = GUI.backgroundColor;
            if (on) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            if (GUILayout.Button((on ? "✓ " : "　 ") + (g == "" ? L.T("ルート", "root") : g), GUILayout.Width(130)))   // 押すと入れる・外す
            {
                if (on) d.Tags.Remove(g);
                else d.Tags.Add(g);
            }
            GUI.backgroundColor = old;
            if (++n % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.Label(L.T("新しいグループ", "New group"), GUILayout.Width(110));
        d.NewTag = GUILayout.TextField(d.NewTag);
        GUILayout.EndHorizontal();
        if (clip != null && current?.Kind != "pose")
        {
            var t = AtEnd ? L.T("最後", "end") : $"{AliasFile.When(time)} " + L.T("秒", "s");
            d.UseTime = GUILayout.Toggle(d.UseTime, d.Edit ? L.T($"時刻を今の {t} にする", $"Set the time to the current {t}") : L.T($"時刻 {t} を付ける(付けなければ最後)", $"Add the time {t} (otherwise the end)"));
        }
        if (d.Message != "")
        {
            var old = GUI.color;
            GUI.color = new Color(1, 0.6f, 0.5f);
            GUILayout.Label(d.Message, plain);
            GUI.color = old;
        }
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (d.Edit && GUILayout.Button(d.ConfirmDelete ? L.T("本当に外す", "Really remove") : L.T("エイリアスから外す", "Remove alias"), GUILayout.Width(150)))
        {
            if (d.ConfirmDelete) DoDelete(d);
            else d.ConfirmDelete = true;
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(L.T("キャンセル", "Cancel"), GUILayout.Width(100))) dlg = null;
        if (GUILayout.Button(d.Edit ? L.T("変更", "Change") : L.T("登録", "Register"), GUILayout.Width(100)) && dlg == d)
        {
            if (d.Edit) DoChange(d);
            else DoRegister(d);
        }
        GUILayout.EndHorizontal();
    }

    bool OpenPoseFile(string path, PoseEntry entry = null)
    {
        if (!File.Exists(path)) { status = L.T("ファイルがありません: ", "File not found: ") + path; return false; }
        try
        {
            staticPose = lib.PoseFromJson(path);
            clip = null;
            current = entry;
            currentPosePath = path;
            poseFileTime = File.GetLastWriteTime(path);
            sourceLabel = (entry != null ? $"[{entry.Name}] " : "") + Path.GetFileName(path);
            status = L.T($"ポーズ: {Path.GetFileName(path)}(筋肉 {staticPose.Muscles.Count} 個)", $"Pose: {Path.GetFileName(path)} ({staticPose.Muscles.Count} muscles)");
            return true;
        }
        catch (Exception e)
        {
            status = L.T("ポーズを読めません: ", "Cannot read the pose: ") + e.Message;
            return false;
        }
    }

    void Select(PoseEntry e)
    {
        if (!string.IsNullOrEmpty(e.AliasGroup)) opened["G:" + e.AliasGroup] = true;   // 閉じたグループの中なら開く
        bool ok;
        try
        {
            if (e.Kind == "pkg") { SetClip(AnimClip.FromPackage(e.PackageFile, e.PkgAnim.PathName, e.PkgAnim.Text), e); ok = true; }
            else if (e.Kind == "anim" || e.Kind == "emote") ok = OpenClip(lib.ClipPathOf(e), e);
            else ok = OpenPoseFile(lib.PosePathOf(e), e);
        }
        catch (Exception ex)
        {
            status = ex.Message;
            ok = false;
        }
        // 読めないエイリアス(ファイルを消した、など)も選んだ状態にして、編集のダイアログで外せるようにする
        if (!ok && e.Kind == "anim" && e.AliasGroup != null && lib.Sources?.SourceOf(e.Target) is AnimSources.Source s
            && !File.Exists(lib.Sources.LocalPath(e.Target)))
        {
            // 取り込み元のあるアニメーションで、まだ取り込んでいないもの
            imp = new ImportDialog { Message = L.T($"「{e.Name}」はまだ取り込んでいません({s.Name})。入手したファイルを選んで取り込んでください。", $"\"{e.Name}\" is not imported yet ({s.Name}). Choose the file you got and import it.") };
            return;
        }
        if (!ok && e.AliasGroup != null)
        {
            var reason = status;
            current = e;
            clip = null;
            staticPose = null;
            currentPosePath = null;
            sourceLabel = $"[{e.Name}] " + L.T("(読めません)", "(cannot read)");
            OpenEdit();
            dlg.Message = reason + L.T("\n「エイリアスから外す」で外せます。", "\nYou can remove it with \"Remove alias\".");
        }
    }

    void OpenDropped(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (imp != null)
        {
            if (!imp.Busy) _ = Import(path);
            return;
        }
        if (Directory.Exists(path)) _ = LoadPackage(path);
        else if (ext == ".vrm") LoadVrm(path);
        else if (ext == ".anim") OpenClip(path);
        else if (ext == ".json") OpenPoseFile(path);
        else if (ext == ".unitypackage" || ext == ".zip") _ = LoadPackage(path);
        else status = L.T("開けない種類のファイルです: ", "Cannot open this kind of file: ") + Path.GetFileName(path) + L.T("(.vrm・.anim・.json・.unitypackage・.zip・フォルダ)", " (.vrm, .anim, .json, .unitypackage, .zip, folder)");
    }

    // ---- 毎フレーム ----

    void Update()
    {
        foreach (var f in WinNative.TakeDropped()) OpenDropped(f);
        Keys();
        Mouse();

        if (clip != null && playing)
        {
            time += Time.deltaTime * speed;
            if (time > clip.Length)
            {
                if (loop && clip.Length > 0) time %= clip.Length;
                else { time = clip.Length; playing = false; }
            }
        }
        // pose_aliases.txt・anim_sources.txt がほかで(メモ帳などで)書き換わったら一覧を読み直す
        if (Time.unscaledTime > nextAliasPoll && dlg == null && imp == null)
        {
            nextAliasPoll = Time.unscaledTime + 1f;
            var t = AliasFilesTime();
            if (aliasFileTime != default && t != aliasFileTime) { Reload(); status = L.T("pose_aliases.txt が書き換わったので、一覧を読み直しました", "pose_aliases.txt changed; the list was reloaded"); }
            aliasFileTime = t;
        }
        // 決まった姿勢のファイルが書き換わったら読み直す(pose_state.json を見張る)
        if (currentPosePath != null && Time.unscaledTime > nextPoll)
        {
            nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                var t = File.GetLastWriteTime(currentPosePath);
                if (t != poseFileTime) OpenPoseFile(currentPosePath, current);
            }
            catch (IOException) { }
        }
        ApplyPose();
        UpdateCamera();
    }

    void ApplyPose()
    {
        if (handler == null) return;
        var p = clip != null ? clip.Sample(time) : staticPose;
        if (p == null) return;
        var m = new float[HumanTrait.MuscleCount];
        foreach (var kv in p.Muscles) m[Muscles.Index[kv.Key]] = kv.Value;
        var rt = p.RootT;
        if (inPlace) rt.x = rt.z = 0;
        pose.bodyPosition = rt;
        pose.bodyRotation = p.RootQ;
        pose.muscles = m;
        handler.SetHumanPose(ref pose);
    }

    // 今の姿勢での、右手などの骨の位置・向き(VRM の根元 = 足元から。+z が前、+y が上、+x が右)を JSON に書く。
    // 持った物を任意ポーズの手に合わせる計算(hand_fit.py)が使う。
    void WriteHandInfo(string path)
    {
        var sb = new System.Text.StringBuilder();
        string F(float x) => x.ToString("0.#####", CultureInfo.InvariantCulture);
        string V(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
        string Q(Quaternion q) => $"[{F(q.x)}, {F(q.y)}, {F(q.z)}, {F(q.w)}]";
        if (vrm == null || handler == null)
        {
            File.WriteAllText(path, "{\"error\": \"VRM が読めていません\"}");
            return;
        }
        var animator = vrm.GetComponent<Animator>();
        var root = vrm.transform;
        handler.GetHumanPose(ref pose);
        sb.Append("{\n");
        sb.Append($" \"vrm\": \"{(vrmPath ?? "").Replace("\\", "\\\\")}\",\n");
        sb.Append($" \"source\": \"{sourceLabel.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\n");
        sb.Append($" \"time\": {F(time)},\n");
        sb.Append($" \"inplace\": {(inPlace ? "true" : "false")},\n");
        sb.Append($" \"humanScale\": {F(animator.humanScale)},\n");
        sb.Append($" \"bodyPosition\": {V(pose.bodyPosition)},\n");
        sb.Append($" \"bodyRotation\": {Q(pose.bodyRotation)},\n");
        sb.Append(" \"rest\": {");
        sb.Append(string.Join(", ", rest.Select(kv => $"\"{kv.Key}\": {V(kv.Value)}")));
        sb.Append("},\n \"restRot\": {");
        sb.Append(string.Join(", ", restRot.Select(kv => $"\"{kv.Key}\": {Q(kv.Value)}")));
        sb.Append("},\n \"bones\": {\n");
        var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightLittleProximal,
                            HumanBodyBones.RightThumbProximal,
                            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal };
        var lines = new List<string>();
        foreach (var b in bones)
        {
            var t = animator.GetBoneTransform(b);
            if (t == null) continue;
            var rot = Quaternion.Inverse(root.rotation) * t.rotation;
            lines.Add($"  \"{b}\": {{\"pos\": {V(root.InverseTransformPoint(t.position))}, \"rot\": {Q(rot)}}}");
        }
        sb.Append(string.Join(",\n", lines));
        sb.Append("\n }\n}\n");
        File.WriteAllText(path, sb.ToString());
        status = L.T("手の位置を書き出しました: ", "Wrote hand info: ") + path;
    }

    bool Typing => GUIUtility.keyboardControl != 0;

    void Keys()
    {
        if (Typing || dlg != null || imp != null) return;
        if (Input.GetKeyDown(KeyCode.Tab)) showUi = !showUi;
        if (Input.GetKeyDown(KeyCode.F)) ResetView("front");
        if (clip == null) return;
        float frame = 1f / clip.SampleRate;
        if (Input.GetKeyDown(KeyCode.Space)) playing = !playing;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) { playing = false; time = Mathf.Max(0, time - frame); }
        if (Input.GetKeyDown(KeyCode.RightArrow)) { playing = false; time = Mathf.Min(clip.Length, time + frame); }
        if (Input.GetKeyDown(KeyCode.Home)) time = 0;
        if (Input.GetKeyDown(KeyCode.End)) time = clip.Length;
    }

    bool OverUi(Vector3 mouse)
    {
        if (dlg != null || imp != null) return true;
        if (!showUi) return false;
        float y = Screen.height - mouse.y;
        return mouse.x < PanelW || y < TopH || y > Screen.height - BottomH;
    }

    void Mouse()
    {
        var m = Input.mousePosition;
        bool any = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && !OverUi(m))
        {
            dragging = true;
            lastMouse = m;
            GUIUtility.keyboardControl = 0;
        }
        if (!any) dragging = false;
        if (dragging)
        {
            var d = m - lastMouse;
            lastMouse = m;
            if (Input.GetMouseButton(0))
            {
                yaw += d.x * 0.3f;
                pitch = Mathf.Clamp(pitch - d.y * 0.3f, -89, 89);
            }
            else
            {
                float k = dist * 0.0015f;
                target -= cam.transform.right * d.x * k + cam.transform.up * d.y * k;
            }
        }
        if (!OverUi(m) && Input.mouseScrollDelta.y != 0)
            dist = Mathf.Clamp(dist * (1 - Input.mouseScrollDelta.y * 0.1f), 0.2f, 30f);
    }

    void ResetView(string view)
    {
        target = new Vector3(0, modelHeight * 0.5f, 0);
        float visible = showUi ? 1 - (TopH + BottomH) / Screen.height : 1;   // 上下の帯を除いた高さの割合
        dist = modelHeight * 0.6f / (Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * visible);
        pitch = 5;
        switch (view)
        {
            case "right": yaw = -90; break;
            case "left": yaw = 90; break;
            case "back": yaw = 0; break;
            case "top": yaw = 180; pitch = 89; break;
            default: yaw = 180; break;
        }
    }

    void UpdateCamera()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.position = target + rot * new Vector3(0, 0, -dist);
        cam.transform.rotation = rot;
        // 一覧と下の帯の分、モデルを見える所の真ん中に寄せる
        float l = showUi ? PanelW / Screen.width : 0, t = showUi ? TopH / Screen.height : 0, b = showUi ? BottomH / Screen.height : 0;
        cam.rect = new Rect(0, 0, 1, 1);
        cam.ResetProjectionMatrix();
        var p = cam.projectionMatrix;
        p.m02 = -l;            // 左にずらす
        p.m12 = t - b;         // 上下
        cam.projectionMatrix = p;
    }

    void DrawGrid(Camera c)
    {
        if (c != cam) return;
        lineMat.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);
        for (int i = -10; i <= 10; i++)
        {
            float f = i * 0.25f;
            GL.Color(i == 0 ? new Color(1, 1, 1, 0.35f) : new Color(1, 1, 1, 0.12f));
            GL.Vertex3(f, 0, -2.5f); GL.Vertex3(f, 0, 2.5f);
            GL.Vertex3(-2.5f, 0, f); GL.Vertex3(2.5f, 0, f);
        }
        // 正面(+Z)の印
        GL.Color(new Color(0.4f, 0.8f, 1f, 0.8f));
        GL.Vertex3(0, 0.001f, 0); GL.Vertex3(0, 0.001f, 0.6f);
        GL.Vertex3(-0.08f, 0.001f, 0.5f); GL.Vertex3(0, 0.001f, 0.6f);
        GL.Vertex3(0.08f, 0.001f, 0.5f); GL.Vertex3(0, 0.001f, 0.6f);
        GL.End();
        GL.PopMatrix();
    }

    // ---- 画面 ----

    void Style()
    {
        if (styled) return;
        styled = true;
        GUI.skin.font = font;
        foreach (var s in new[] { GUI.skin.label, GUI.skin.button, GUI.skin.toggle, GUI.skin.textField, GUI.skin.box })
        {
            s.fontSize = 14;
            s.clipping = TextClipping.Overflow;
        }
        plain = new GUIStyle(GUI.skin.label) { wordWrap = false };
    }

    void OnGUI()
    {
        Style();
        if (!showUi)
        {
            if (Arg("noui") == null) GUI.Label(new Rect(8, 4, 400, 24), L.T("Tab: 操作の表示", "Tab: show controls"));
            return;
        }
        float w = Screen.width, h = Screen.height;
        GUI.Box(new Rect(0, 0, PanelW, h - BottomH), GUIContent.none);
        GUI.Box(new Rect(PanelW, 0, w - PanelW, TopH), GUIContent.none);
        GUI.Box(new Rect(0, h - BottomH, w, BottomH), GUIContent.none);
        GUI.enabled = dlg == null && imp == null;
        ListPanel(new Rect(6, 6, PanelW - 12, h - BottomH - 12));
        TopBar(new Rect(PanelW + 8, 6, w - PanelW - 16, TopH - 10));
        BottomBar(new Rect(8, h - BottomH + 6, w - 16, BottomH - 10));
        GUI.enabled = true;
        if (dlg != null)
        {
            var title = dlg.Edit ? L.T("エイリアスの編集: ", "Edit alias: ") + current?.Name : L.T("エイリアスに登録", "Register as alias");
            GUI.ModalWindow(1, new Rect((w - 600) / 2, (h - 380) / 2, 600, 380), DialogWindow, title);
        }
        else if (imp != null)
            GUI.ModalWindow(2, new Rect((w - 720) / 2, (h - 400) / 2, 720, 400), ImportWindow, L.T("アニメーションの取り込み", "Import animations"));
    }

    // 見出し(押すと開け閉め)。開いていれば true
    bool Foldout(string key, string label, int count, bool defaultOpen, bool force)
    {
        if (!opened.TryGetValue(key, out var open)) open = defaultOpen;
        if (force) open = true;
        var style = new GUIStyle(GUI.skin.label) { wordWrap = false };
        if (GUILayout.Button($"{(open ? "▼" : "▶")} {label}  ({count})", style)) opened[key] = !open;
        return open;
    }

    void EntryButton(PoseEntry e, GUIStyle style)
    {
        var old = GUI.backgroundColor;
        if (e == current) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
        if (GUILayout.Button(e.Label, style)) Select(e);
        GUI.backgroundColor = old;
    }

    void ListPanel(Rect r)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        GUILayout.Label(L.T("絞り込み", "Filter"), GUILayout.Width(60));
        filter = GUILayout.TextField(filter);
        if (GUILayout.Button(L.T("読み直す", "Reload"), GUILayout.Width(70))) { Reload(); status = L.T("一覧を読み直しました", "Reloaded the list"); }
        GUILayout.EndHorizontal();
        if (GUILayout.Button(L.T("pose_aliases.txt をメモ帳で編集", "Edit pose_aliases.txt in Notepad"))) EditAliasFile();
        foreach (var e in lib.Errors)
        {
            var old = GUI.color;
            GUI.color = new Color(1, 0.6f, 0.5f);
            GUILayout.Label(e);
            GUI.color = old;
        }
        scroll = GUILayout.BeginScrollView(scroll);
        var left = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
        var indent = new GUIStyle(left) { margin = new RectOffset(18, 4, 2, 2) };
        bool filtering = filter != "";
        bool Match(PoseEntry e) => !filtering || e.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                               || (e.AliasGroup ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        // 見出しの順はエントリーの順(パッケージ・フォルダ → エイリアス → 未登録のアニメ → エモート → 未登録のポーズ → いまの任意ポーズ)
        foreach (var group in lib.Entries.Select(x => x.Group).Distinct().ToList())
        {
            var items = lib.Entries.Where(x => x.Group == group && Match(x)).ToList();
            if (group == "エイリアス")
            {
                // ルートはいつも開いている。グループ(1 段)は普段は閉じている
                GUILayout.Label(L.T("■ エイリアス", "■ Aliases"), plain);
                foreach (var e in items.Where(x => x.AliasGroup == "")) EntryButton(e, left);
                foreach (var g in lib.AliasGroups)
                {
                    var inG = items.Where(x => x.AliasGroup == g).ToList();
                    if (filtering && inG.Count == 0) continue;
                    if (Foldout("G:" + g, g, inG.Count, false, filtering))
                        foreach (var e in inG) EntryButton(e, indent);
                }
                continue;
            }
            if (items.Count == 0) continue;
            bool defaultOpen = group != "エモート";
            if (Foldout("S:" + group, L.Group(group), items.Count, defaultOpen, filtering))
                foreach (var e in items) EntryButton(e, left);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 一覧を読み直し、選んでいたものを選び直す
    void Reload()
    {
        var key = current != null ? (current.Group, current.Name, current.AliasGroup) : default;
        lib.Reload();
        if (current != null) current = lib.Entries.FirstOrDefault(x => (x.Group, x.Name, x.AliasGroup) == key) ?? current;
    }

    void TopBar(Rect r)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(L.T("VRM を開く…", "Open VRM…"), GUILayout.Width(110)))
        {
            var dir = vrmPath != null ? Path.GetDirectoryName(vrmPath) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VRoid");
            var f = WinNative.OpenFile(L.T("VRM を開く", "Open VRM"), "VRM", "*.vrm", dir);
            if (f != null) LoadVrm(f);
        }
        if (GUILayout.Button(L.T("アニメを開く…", "Open anim…"), GUILayout.Width(110)))
        {
            var dir = clip != null ? Path.GetDirectoryName(clip.Path) : Path.Combine(lib.ProjectDir, "Assets");
            var f = WinNative.OpenFile(L.T("アニメーションを開く", "Open animation"), L.T("アニメーション", "Animation"), "*.anim", dir);
            if (f != null) OpenClip(f);
        }
        int missing = lib.Sources?.MissingCount ?? 0;
        if (lib.Sources != null && lib.Sources.Sources.Count > 0 && GUILayout.Button(L.T("取り込み…", "Import…") + (missing > 0 ? $"({missing})" : ""), GUILayout.Width(missing > 0 ? 130 : 90)))
            imp = new ImportDialog();
        GUILayout.Space(16);
        foreach (var (label, view) in new[] { (L.T("正面", "Front"), "front"), (L.T("右", "Right"), "right"), (L.T("左", "Left"), "left"), (L.T("後", "Back"), "back"), (L.T("上", "Top"), "top") })
            if (GUILayout.Button(label, GUILayout.Width(L.Ja ? 44 : 50))) ResetView(view);
        GUILayout.Space(8);
        inPlace = GUILayout.Toggle(inPlace, L.T("その場で(左右・前後を止める)", "In place"));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(L.Ja ? "English" : "日本語", GUILayout.Width(70))) { L.Set(!L.Ja); Reload(); }
        GUILayout.EndHorizontal();
        GUILayout.Label($"{(vrmPath != null ? Path.GetFileName(vrmPath) : L.T("(VRM なし)", "(no VRM)"))}   ／   {sourceLabel}", plain);
        GUILayout.Label(L.T("左ドラッグ: 回す  右ドラッグ: 動かす  ホイール: 寄る  F: 正面  Space: 再生  ←→: 1 コマ  Tab: 隠す  (.vrm・.anim・.json・.unitypackage・.zip・フォルダはドロップで開く)", "L-drag: rotate  R-drag: pan  Wheel: zoom  F: front  Space: play  ←→: frame  Tab: hide  (drop .vrm/.anim/.json/.unitypackage/.zip/folder)"), plain);
        GUILayout.EndArea();
    }

    void BottomBar(Rect r)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        GUI.enabled = clip != null;
        if (GUILayout.Button(playing ? "❚❚" : "▶", GUILayout.Width(44))) playing = !playing;
        if (GUILayout.Button("◀1", GUILayout.Width(40)) && clip != null) { playing = false; time = Mathf.Max(0, time - 1f / clip.SampleRate); }
        if (GUILayout.Button("1▶", GUILayout.Width(40)) && clip != null) { playing = false; time = Mathf.Min(clip.Length, time + 1f / clip.SampleRate); }
        float len = clip != null ? clip.Length : 1;
        var nt = GUILayout.HorizontalSlider(time, 0, len, GUILayout.ExpandWidth(true), GUILayout.Height(22));
        if (clip != null && !Mathf.Approximately(nt, time)) { time = nt; playing = false; }
        GUILayout.Label(clip != null ? $"{time:0.000} / {clip.Length:0.000} " + L.T("秒", "s") + $"  ({time * clip.SampleRate:0} / {clip.Length * clip.SampleRate:0} f)" : L.T("(静止ポーズ)", "(still pose)"), GUILayout.Width(260));
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        loop = GUILayout.Toggle(loop, L.T("くり返す", "Loop"), GUILayout.Width(80));
        GUILayout.Label(L.T("速さ", "Speed"), GUILayout.Width(L.Ja ? 32 : 44));
        foreach (var s in new[] { 0.25f, 0.5f, 1f })
        {
            var old = GUI.backgroundColor;
            if (Mathf.Approximately(speed, s)) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            if (GUILayout.Button(s.ToString("0.##"), GUILayout.Width(44))) speed = s;
            GUI.backgroundColor = old;
        }
        GUILayout.Space(8);
        if (File.Exists(Path.Combine(lib.AgentDir, "pose_send.py")) && GUILayout.Button(L.T("pose_send のコマンドをコピー", "Copy pose_send command"), GUILayout.Width(200)))
        {
            var cmd = SendCommand();
            if (cmd != null) { GUIUtility.systemCopyBuffer = cmd; status = L.T("コピー: ", "Copied: ") + cmd; }
        }
        bool was = GUI.enabled;
        GUI.enabled = was && (clip != null || staticPose != null);
        if (GUILayout.Button(L.T("登録…", "Register…"), GUILayout.Width(80))) OpenRegister();
        GUI.enabled = was && current?.AliasGroup != null;
        if (GUILayout.Button(L.T("編集…", "Edit…"), GUILayout.Width(80))) OpenEdit();
        GUI.enabled = was;
        GUILayout.Space(8);
        GUILayout.Label(status, plain);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // 今の姿勢を VRChat に送る pose_send.py のコマンド
    string SendCommand()
    {
        var t = time.ToString("0.###", CultureInfo.InvariantCulture);
        if (clip != null)
        {
            if (current != null && current.Kind == "emote") return $"uv run pose_send.py from-emote {current.Target} --time {t}";
            if (clip.PackageFile != null) { status = L.T("パッケージの中のアニメーションです。先に「登録…」で anim/ に保存してください", "This animation is inside a package. Save it to anim/ with \"Register…\" first"); return null; }
            return $"uv run pose_send.py from-anim \"{clip.Path}\" --time {t}";
        }
        if (current != null && current.Kind == "pose") return $"uv run pose_send.py load {current.Target}";
        status = L.T("この姿勢は送るコマンドにできません", "This pose cannot be made into a send command");
        return null;
    }

    // pose_aliases.txt の所か Unity プロジェクトの中なら相対パス(/ 区切り)、そうでなければ絶対パス
    string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in new[] { lib.AgentDir, lib.ProjectDir })
        {
            var r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return full.Substring(r.Length).Replace('\\', '/');
        }
        return full;
    }
}
