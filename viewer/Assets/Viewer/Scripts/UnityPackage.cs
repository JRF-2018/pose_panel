// .unitypackage(gzip で固めた tar)から、テキスト形式のアニメーション(.anim)だけを取り出す。
// 中身は <guid>/pathname(元のパス "Assets/...")と <guid>/asset(ファイルの中身)の組。
// 順番は決まっていないので、アニメーションらしい asset(先頭に "!u!74 " がある YAML)だけを覚えておき、最後に組み合わせる。
// 中身は圧縮して持ち、使うときに戻す(エモート集などは .anim が合計数百 MB になるため)。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class UnityPackage
{
    public class Anim
    {
        public string PathName;   // Assets/... の元のパス(zip の中にじかにある .anim は、zip の中のパス)
        public string Source;     // zip の中の .unitypackage から読んだときは、その名前(表示用。なければ null)
        byte[] packed;            // .anim の中身(UTF-8 を Deflate で圧縮したもの)
        public int Size;          // 元の大きさ(バイト)

        public static Anim FromBytes(string pathName, byte[] buf, int size)
        {
            var a = new Anim { PathName = pathName, Size = size };
            using (var ms = new MemoryStream())
            {
                using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true)) d.Write(buf, 0, size);
                a.packed = ms.ToArray();
            }
            return a;
        }

        public byte[] Bytes
        {
            get
            {
                var buf = new byte[Size];
                using (var d = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress)) ReadFull(d, buf, Size);
                return buf;
            }
        }

        public string Text => Encoding.UTF8.GetString(Bytes);
    }

    public static List<Anim> ReadAnims(string path)
    {
        using (var file = File.OpenRead(path)) return ReadAnims(file);
    }

    // .unitypackage の中身(gzip で固めた tar)のストリームから
    public static List<Anim> ReadAnims(Stream packageStream)
    {
        var names = new Dictionary<string, string>();
        var assets = new Dictionary<string, Anim>();
        using (var gz = new GZipStream(packageStream, CompressionMode.Decompress, true))
        {
            var header = new byte[512];
            string longName = null;
            while (ReadFull(gz, header, 512))
            {
                if (IsZero(header)) break;
                var name = longName ?? CString(header, 0, 100);
                longName = null;
                var prefix = CString(header, 345, 155);
                if (prefix.Length > 0 && Encoding.ASCII.GetString(header, 257, 5) == "ustar") name = prefix + "/" + name;
                var sizeText = CString(header, 124, 12).Trim();
                long size = sizeText.Length > 0 ? Convert.ToInt64(sizeText, 8) : 0;
                char type = (char)header[156];
                long padded = (size + 511) / 512 * 512;

                if (type == 'L')   // GNU の長い名前: 次の項目の名前
                {
                    var buf = new byte[padded];
                    ReadFull(gz, buf, (int)padded);
                    longName = CString(buf, 0, (int)size);
                    continue;
                }
                if (name.StartsWith("./")) name = name.Substring(2);
                int slash = name.IndexOf('/');
                string guid = slash > 0 ? name.Substring(0, slash) : name;
                string part = slash > 0 ? name.Substring(slash + 1) : "";
                if ((type == '0' || type == '\0') && (part == "pathname" || part == "asset") && size < int.MaxValue)
                {
                    var buf = new byte[padded];
                    ReadFull(gz, buf, (int)padded);
                    if (part == "pathname")
                        names[guid] = Encoding.UTF8.GetString(buf, 0, (int)size).Split('\n')[0].Trim();
                    else if (LooksLikeAnim(buf, (int)size))
                        assets[guid] = Anim.FromBytes(null, buf, (int)size);
                }
                else Skip(gz, padded);
            }
        }
        var list = new List<Anim>();
        foreach (var kv in assets)
            if (names.TryGetValue(kv.Key, out var p) && p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            {
                kv.Value.PathName = p;
                list.Add(kv.Value);
            }
        list.Sort((a, b) => string.CompareOrdinal(a.PathName, b.PathName));
        return list;
    }

    public static bool LooksLikeAnim(byte[] buf, int size)
    {
        var head = Encoding.UTF8.GetString(buf, 0, Math.Min(size, 300)).TrimStart('\uFEFF');   // 先頭に BOM があるものも
        return head.StartsWith("%YAML") && head.Contains("!u!74 ");
    }

    public static bool ReadFull(Stream s, byte[] buf, int n)
    {
        int got = 0;
        while (got < n)
        {
            int r = s.Read(buf, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    static void Skip(Stream s, long n)
    {
        var buf = new byte[81920];
        while (n > 0)
        {
            int r = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
            if (r <= 0) return;
            n -= r;
        }
    }

    static bool IsZero(byte[] b)
    {
        foreach (var x in b) if (x != 0) return false;
        return true;
    }

    static string CString(byte[] b, int start, int len)
    {
        int end = start;
        while (end < start + len && end < b.Length && b[end] != 0) end++;
        return Encoding.UTF8.GetString(b, start, end - start);
    }
}
