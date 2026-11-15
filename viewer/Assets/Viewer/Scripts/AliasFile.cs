// pose_aliases.txt を読み書きする。書き方は pose_aliases.txt の先頭のコメントと pose_aliases.py を参照(読み方を合わせてある)。
//
// ファイルの中は [グループ名] の行で区切る(1 段。最初の [..] より前はルート "")。
// 名前・種類・対象・時刻がすべて同じ行は、グループが違っても同じエイリアス(= グループは「タグ」)。
// 名前が同じでも中身が違えば別のエイリアス(同じグループの中にあってもよい)。
// 書き換えるときは、触らない行(コメント・空行・ほかのエイリアス)はそのまま残す。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class AliasFile
{
    public class Item
    {
        public string Name, Kind, Target, When;   // When: "1.6"・"f30"・null(最後)
        public string Group = "";                  // この行のグループ(ルートは "")
        public int Line;                           // 何行目か(0 から)
        public string Key => $"{Name}\n{Kind}\n{Target}\n{When}";   // エイリアスの同一性(グループは含まない)
    }

    static readonly Regex GroupRe = new Regex(@"^\s*\[(.+?)\]\s*(#.*)?$");
    public readonly string Path;
    public List<string> Lines = new List<string>();
    public readonly List<Item> Items = new List<Item>();        // 行ごと(同じグループの同じ行の 2 度目は除く)
    public readonly List<string> Groups = new List<string>();   // ファイルに出てくる順(中が空のものも)
    public readonly List<string> Errors = new List<string>();
    readonly List<int> duplicates = new List<int>();            // 同じグループの同じ行の 2 度目(書き換えるときに消す)

    public AliasFile(string path)
    {
        Path = path;
        if (File.Exists(path))
            Lines = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿').Replace("\r\n", "\n").Split('\n').ToList();
        if (Lines.Count > 0 && Lines[Lines.Count - 1] == "") Lines.RemoveAt(Lines.Count - 1);
        Parse();
    }

    void Parse()
    {
        Items.Clear();
        Groups.Clear();
        Errors.Clear();
        duplicates.Clear();
        string group = "";
        for (int n = 0; n < Lines.Count; n++)
        {
            var m = GroupRe.Match(Lines[n]);
            if (m.Success)
            {
                group = m.Groups[1].Value.Trim();
                if (!Groups.Contains(group)) Groups.Add(group);
                continue;
            }
            var w = PoseLibrary.SplitWords(Lines[n]);
            if (w.Count == 0) continue;
            if ((w.Count != 3 && w.Count != 4) || (w[1] != "emote" && w[1] != "anim" && w[1] != "pose"))
            {
                Errors.Add($"pose_aliases.txt:{n + 1}: " + L.T("「エイリアス名 種類 対象 [時刻]」の形ではありません", "not in the form \"name kind target [time]\""));
                continue;
            }
            var it = new Item { Name = w[0], Kind = w[1], Target = w[2], When = w.Count == 4 && w[3] != "end" ? w[3] : null, Group = group, Line = n };
            if (Items.Any(x => x.Key == it.Key && x.Group == group)) { duplicates.Add(n); continue; }
            Items.Add(it);
        }
    }

    // そのエイリアス(中身が同じ行)が入っているグループ
    public List<string> TagsOf(string key) => Items.Where(x => x.Key == key).Select(x => x.Group).Distinct().ToList();

    static string Quote(string s) =>
        s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c == '#' || c == '\'') ? $"\"{s}\"" : s;

    public static string Format(Item it) =>
        $"{Quote(it.Name)}  {it.Kind}  {Quote(it.Target)}" + (it.When != null ? $"  {it.When}" : "");

    public static string When(float? time) =>
        time.HasValue ? time.Value.ToString("0.###", CultureInfo.InvariantCulture) : null;

    // エイリアスを tags のグループに置く。
    // oldKey が null(登録): 同じ中身がもうあれば、足りないタグを足すだけ(ほかのタグはそのまま)。
    // oldKey があり(編集): oldKey の行を新しい中身に書き換え、tags にないグループの行は消し、足りないグループには足す。
    public void Put(Item content, ICollection<string> tags, string oldKey = null)
    {
        if (tags.Count == 0) throw new Exception(L.T("グループ(ルートも含めて)を 1 つ以上選んでください", "Choose at least one group (root counts)"));
        if (oldKey != null)
        {
            foreach (var x in Items.Where(x => x.Key == oldKey).OrderByDescending(x => x.Line).ToList())
            {
                if (tags.Contains(x.Group)) Lines[x.Line] = Format(content);
                else Lines.RemoveAt(x.Line);
            }
            Parse();
            for (int i = duplicates.Count - 1; i >= 0; i--) Lines.RemoveAt(duplicates[i]);   // 書き換えで重なった行
            Parse();
        }
        foreach (var g in tags)
            if (!Items.Any(x => x.Key == content.Key && x.Group == g))
                Insert(new Item { Name = content.Name, Kind = content.Kind, Target = content.Target, When = content.When, Group = g });
        DropEmptyGroups();
        Save();
    }

    // そのエイリアスを、すべてのグループから消す
    public void Remove(string key)
    {
        foreach (var x in Items.Where(x => x.Key == key).OrderByDescending(x => x.Line).ToList())
            Lines.RemoveAt(x.Line);
        Parse();
        DropEmptyGroups();
        Save();
    }

    // グループの最後のエイリアスの次の行に入れる。グループがなければファイルの最後に作る
    void Insert(Item it)
    {
        var line = Format(it);
        int at;
        var inGroup = Items.Where(x => x.Group == it.Group).ToList();
        if (inGroup.Count > 0) at = inGroup.Max(x => x.Line) + 1;
        else if (it.Group == "")
        {
            int first = Lines.FindIndex(l => GroupRe.IsMatch(l));
            at = first < 0 ? Lines.Count : first;
            while (at > 0 && Lines[at - 1].Trim() == "" && first >= 0) at--;   // 最初のグループの前の空行の前に
        }
        else
        {
            int header = Header(it.Group);
            if (header < 0)
            {
                if (Lines.Count > 0 && Lines[Lines.Count - 1].Trim() != "") Lines.Add("");
                Lines.Add($"[{it.Group}]");
                at = Lines.Count;
            }
            else at = header + 1;
        }
        Lines.Insert(at, line);
        if (it.Group == "" && at + 1 < Lines.Count && GroupRe.IsMatch(Lines[at + 1])) Lines.Insert(at + 1, "");   // 見出しの前を 1 行空ける
        Parse();
    }

    int Header(string group) =>
        Lines.FindIndex(l => GroupRe.Match(l) is var m && m.Success && m.Groups[1].Value.Trim() == group);

    // エイリアスのなくなったグループの見出しの行(と、その前の空行)を消す
    void DropEmptyGroups()
    {
        foreach (var g in Groups.ToList())
        {
            if (Items.Any(x => x.Group == g)) continue;
            int header = Header(g);
            if (header < 0) continue;
            Lines.RemoveAt(header);
            if (header > 0 && Lines[header - 1].Trim() == "" && (header == Lines.Count || Lines[header].Trim() == "" || GroupRe.IsMatch(Lines[header])))
                Lines.RemoveAt(header - 1);
            Parse();
        }
    }

    void Save() => File.WriteAllText(Path, string.Join("\n", Lines) + "\n", new UTF8Encoding(false));
}
