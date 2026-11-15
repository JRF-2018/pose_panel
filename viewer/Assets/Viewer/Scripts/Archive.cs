// 配布されている形のまま(zip・.unitypackage・フォルダ)から、テキスト形式の人型アニメーション(.anim)を読む。
//   .unitypackage: UnityPackage.ReadAnims
//   .zip: 中の .anim と、中の .unitypackage の .anim(Booth の配布物はたいてい zip の中に .unitypackage がある)
//   フォルダ: 下の .anim(Unity に展開したアセットなど)
// zip は自前で読む(中のファイル名が Shift_JIS のものが多く、Unity の実行時のライブラリでは扱いにくいため)。
// 圧縮は「なし」と Deflate だけ(ふつうの zip はこれ)。ZIP64・暗号化には対応しない。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

public static class Archive
{
    public static bool CanRead(string path)
    {
        if (Directory.Exists(path)) return true;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".zip" || ext == ".unitypackage";
    }

    // 中の .anim をすべて(PathName の順)
    public static List<UnityPackage.Anim> ReadAnims(string path)
    {
        if (Directory.Exists(path)) return ReadFolder(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".unitypackage") return UnityPackage.ReadAnims(path);
        if (ext == ".zip") return ReadZip(path);
        throw new Exception($"読めない種類のファイルです: {Path.GetFileName(path)}(.zip・.unitypackage・フォルダ)");
    }

    static List<UnityPackage.Anim> ReadFolder(string dir)
    {
        var root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var list = new List<UnityPackage.Anim>();
        foreach (var f in Directory.GetFiles(dir, "*.anim", SearchOption.AllDirectories))
        {
            var buf = File.ReadAllBytes(f);
            if (!UnityPackage.LooksLikeAnim(buf, buf.Length)) continue;
            // Assets の下なら Assets/... の形に(.unitypackage の中と同じパスになる)
            var rel = Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/');
            var full = Path.GetFullPath(f).Replace('\\', '/');
            int i = full.LastIndexOf("/Assets/", StringComparison.Ordinal);
            list.Add(UnityPackage.Anim.FromBytes(i >= 0 ? full.Substring(i + 1) : rel, buf, buf.Length));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.PathName, b.PathName));
        return list;
    }

    static List<UnityPackage.Anim> ReadZip(string path)
    {
        var list = new List<UnityPackage.Anim>();
        using (var zip = new ZipReader(path))
            foreach (var e in zip.Entries)
            {
                var lower = e.Name.ToLowerInvariant();
                if (lower.EndsWith(".unitypackage"))
                {
                    using (var s = zip.Open(e))
                        foreach (var a in UnityPackage.ReadAnims(s))
                        {
                            a.Source = Path.GetFileName(e.Name);
                            list.Add(a);
                        }
                }
                else if (lower.EndsWith(".anim") && e.Size < int.MaxValue)
                {
                    var buf = new byte[e.Size];
                    using (var s = zip.Open(e)) UnityPackage.ReadFull(s, buf, buf.Length);
                    if (UnityPackage.LooksLikeAnim(buf, buf.Length)) list.Add(UnityPackage.Anim.FromBytes(e.Name, buf, buf.Length));
                }
            }
        list.Sort((a, b) => string.CompareOrdinal(a.PathName, b.PathName));
        return list;
    }

    public class ZipEntry
    {
        public string Name;
        public int Method;          // 0: なし、8: Deflate
        public long CompressedSize, Size, Offset;
    }

    public class ZipReader : IDisposable
    {
        readonly FileStream file;
        public readonly List<ZipEntry> Entries = new List<ZipEntry>();

        public ZipReader(string path)
        {
            file = File.OpenRead(path);
            // 終わりの印(End of central directory)を後ろから探す
            int tail = (int)Math.Min(file.Length, 22 + 65535);
            var buf = new byte[tail];
            file.Seek(file.Length - tail, SeekOrigin.Begin);
            UnityPackage.ReadFull(file, buf, tail);
            int eocd = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (U32(buf, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new Exception("zip として読めません");
            int count = U16(buf, eocd + 10);
            long cdSize = U32(buf, eocd + 12), cdOffset = U32(buf, eocd + 16);
            if (cdOffset == 0xFFFFFFFF || count == 0xFFFF) throw new Exception("ZIP64 の zip には対応していません");
            var cd = new byte[cdSize];
            file.Seek(cdOffset, SeekOrigin.Begin);
            UnityPackage.ReadFull(file, cd, cd.Length);
            int p = 0;
            for (int n = 0; n < count && p + 46 <= cd.Length; n++)
            {
                if (U32(cd, p) != 0x02014b50) throw new Exception("zip の目次が壊れています");
                int flags = U16(cd, p + 8);
                int nameLen = U16(cd, p + 28), extraLen = U16(cd, p + 30), commentLen = U16(cd, p + 32);
                var e = new ZipEntry
                {
                    Method = U16(cd, p + 10),
                    CompressedSize = U32(cd, p + 20),
                    Size = U32(cd, p + 24),
                    Offset = U32(cd, p + 42),
                    Name = DecodeName(cd, p + 46, nameLen, (flags & 0x800) != 0),
                };
                if ((flags & 1) == 0 && !e.Name.EndsWith("/")) Entries.Add(e);   // 暗号化したものとフォルダは飛ばす
                p += 46 + nameLen + extraLen + commentLen;
            }
        }

        public Stream Open(ZipEntry e)
        {
            var head = new byte[30];
            file.Seek(e.Offset, SeekOrigin.Begin);
            UnityPackage.ReadFull(file, head, 30);
            if (U32(head, 0) != 0x04034b50) throw new Exception($"zip の中が壊れています: {e.Name}");
            file.Seek(e.Offset + 30 + U16(head, 26) + U16(head, 28), SeekOrigin.Begin);
            var data = new byte[e.CompressedSize];
            UnityPackage.ReadFull(file, data, data.Length);
            var ms = new MemoryStream(data);
            if (e.Method == 0) return ms;
            if (e.Method == 8) return new DeflateStream(ms, CompressionMode.Decompress);
            throw new Exception($"この圧縮方式には対応していません({e.Method}): {e.Name}");
        }

        public void Dispose() => file.Dispose();

        static int U16(byte[] b, int i) => b[i] | b[i + 1] << 8;
        static long U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);
    }

    // zip の中のファイル名: UTF-8 の印があれば UTF-8。なければ、UTF-8 として正しければ UTF-8、
    // そうでなければ Shift_JIS(日本の zip に多い。I18N.CJK.dll がないときは 1 バイトずつの文字に)
    static string DecodeName(byte[] b, int start, int len, bool utf8)
    {
        if (utf8 || IsAscii(b, start, len)) return Encoding.UTF8.GetString(b, start, len);
        try { return new UTF8Encoding(false, true).GetString(b, start, len); }
        catch (ArgumentException) { }
        try { return Encoding.GetEncoding(932).GetString(b, start, len); }
        catch (Exception) { return Encoding.GetEncoding(28591).GetString(b, start, len); }
    }

    static bool IsAscii(byte[] b, int start, int len)
    {
        for (int i = start; i < start + len; i++) if (b[i] >= 0x80) return false;
        return true;
    }
}
