// 画面の言語。T(日本語, 英語) で今の言語の文字を返す。
// 決め方: 起動オプション -lang ja|en(パネルが渡す)→ 前に選んだもの(PlayerPrefs の lang)→ Windows の言語。
using UnityEngine;

public static class L
{
    public static bool Ja = true;

    public static string T(string ja, string en) => Ja ? ja : en;

    public static void Init(string arg)
    {
        var lang = arg ?? PlayerPrefs.GetString("lang", "");
        Ja = lang == "ja" || (lang != "en" && Application.systemLanguage == SystemLanguage.Japanese);
    }

    public static void Set(bool ja)
    {
        Ja = ja;
        PlayerPrefs.SetString("lang", ja ? "ja" : "en");
        PlayerPrefs.Save();
    }

    // 一覧の見出し(PoseLibrary は日本語のまま持つ。見せるときに直す)
    public static string Group(string g)
    {
        if (Ja) return g;
        switch (g)
        {
            case "エイリアス": return "Aliases";
            case "エモート": return "Emotes";
            case "保存したアニメ(未登録)": return "Saved animations (not registered)";
            case "保存したポーズ(未登録)": return "Saved poses (not registered)";
            case "いまの任意ポーズ": return "Current poses";
        }
        if (g.StartsWith("パッケージ: ")) return "Package: " + g.Substring(7);
        if (g.StartsWith("フォルダ: ")) return "Folder: " + g.Substring(6);
        return g;
    }
}
