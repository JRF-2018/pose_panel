// anim_sources.txt: pose_aliases.txt の anim/<フォルダ>/... のアニメーションを、どこから取り込むかの表。
// 配布されているアセット(Booth の zip など)の中身はこちらで配れないので、使う人に入手してもらい、
// その zip・.unitypackage・フォルダから、エイリアスに登録したものだけを anim/ に取り込む。
//
// 1 行に 1 つ:  フォルダ  パッケージの中のパスの頭  名前  [入手先]
//   フォルダ: anim/ の下のフォルダ名(エイリアスの対象が anim/<フォルダ>/... のものが、この取り込み元のもの)
//   パスの頭: .unitypackage の中の元のパス(Assets/...)の頭。* は / 以外の何文字でも(版の数字など)。| で区切って複数。
//   エイリアスの anim/<フォルダ>/<残り> は、パスの頭に合うアニメーションのうち、頭のあとが <残り> で終わるもの
//   (間のフォルダは問わない。版が上がってフォルダの並びが変わっても、ファイル名が同じなら取り込める)。
// 区切りは空白、空白を含むものは "..." で、引用符の外の # から後はコメント(pose_aliases.txt と同じ)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class AnimSources
{
    public class Source
    {
        public string Folder, Pattern, Name, Url;
        public Regex Re;
        public List<string> Targets = new List<string>();   // エイリアスの対象(anim/<Folder>/...。重ねない)
        public List<string> Missing = new List<string>();   // そのうち anim/ にないもの
    }

    public readonly List<Source> Sources = new List<Source>();
    public readonly List<string> Errors = new List<string>();
    public readonly string AgentDir;
    public string FilePath => Path.Combine(AgentDir, "anim_sources.txt");

    public AnimSources(string agentDir, AliasFile aliases)
    {
        AgentDir = agentDir;
        if (!File.Exists(FilePath)) return;
        var lines = File.ReadAllText(FilePath, Encoding.UTF8).TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            var w = PoseLibrary.SplitWords(lines[n]);
            if (w.Count == 0) continue;
            if (w.Count < 3) { Errors.Add($"anim_sources.txt:{n + 1}: 「フォルダ パスの頭 名前 [入手先]」の形ではありません"); continue; }
            var s = new Source { Folder = w[0].Trim('/'), Pattern = w[1], Name = w[2], Url = w.Count > 3 ? w[3] : null };
            var alts = s.Pattern.Split('|').Select(p => Regex.Escape(p.Trim().TrimEnd('/') + "/").Replace(@"\*", "[^/]*"));
            s.Re = new Regex("^(?:" + string.Join("|", alts) + ")");
            Sources.Add(s);
        }
        if (aliases == null) return;
        foreach (var it in aliases.Items.Where(x => x.Kind == "anim"))
        {
            var src = SourceOf(it.Target);
            if (src == null || src.Targets.Contains(it.Target)) continue;
            src.Targets.Add(it.Target);
            if (!File.Exists(LocalPath(it.Target))) src.Missing.Add(it.Target);
        }
    }

    public int MissingCount => Sources.Sum(s => s.Missing.Count);

    // 一つも取り込んでいない取り込み元があるか(起動したときに取り込みの画面を出すか)
    public bool NothingImported => Sources.Any(s => s.Targets.Count > 0 && s.Missing.Count == s.Targets.Count);

    public Source SourceOf(string target)
    {
        var t = target.Replace('\\', '/');
        return Sources.FirstOrDefault(s => t.StartsWith("anim/" + s.Folder + "/", StringComparison.OrdinalIgnoreCase));
    }

    public string LocalPath(string target) => Path.Combine(AgentDir, target.Replace('/', Path.DirectorySeparatorChar));

    // 読んだアニメーションから、足りないものを anim/ に書き出す。結果を 1 行ずつ返す。
    public List<string> Import(List<UnityPackage.Anim> anims, out int written)
    {
        written = 0;
        var report = new List<string>();
        foreach (var s in Sources)
        {
            if (s.Missing.Count == 0) continue;
            // パスの頭に合うもの: 頭のあとの残り → アニメーション
            var cand = new List<(string rest, UnityPackage.Anim anim)>();
            foreach (var a in anims)
            {
                var m = s.Re.Match(a.PathName);
                if (m.Success) cand.Add((a.PathName.Substring(m.Length), a));
            }
            if (cand.Count == 0) continue;
            int ok = 0;
            var notFound = new List<string>();
            foreach (var target in s.Missing.ToList())
            {
                var rest = target.Substring(("anim/" + s.Folder + "/").Length);
                var hits = cand.Where(c => c.rest == rest || c.rest.EndsWith("/" + rest)).ToList();
                if (hits.Count == 0) { notFound.Add(rest); continue; }
                var pick = hits.FirstOrDefault(c => c.rest == rest).anim;
                if (pick == null)
                {
                    // 別のフォルダに同じ名前がいくつかあるときは、中身がすべて同じなら取り込む
                    var first = hits[0].anim.Bytes;
                    if (hits.Skip(1).Any(h => !h.anim.Bytes.SequenceEqual(first)))
                    {
                        report.Add($"{s.Name}: {rest} は中身の違う同じ名前のファイルがあり、決められません");
                        continue;
                    }
                    pick = hits[0].anim;
                }
                var dest = LocalPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, pick.Bytes);
                s.Missing.Remove(target);
                ok++;
            }
            written += ok;
            report.Add($"{s.Name}: {ok} 個を取り込みました" + (notFound.Count > 0 ? $"(見つからないもの {notFound.Count} 個: {string.Join(", ", notFound.Take(5))}{(notFound.Count > 5 ? " …" : "")})" : ""));
        }
        if (report.Count == 0) report.Add("このファイルには、取り込み元(anim_sources.txt)に合うアニメーションがありませんでした");
        return report;
    }
}
