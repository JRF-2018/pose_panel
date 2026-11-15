// 手の IK(panel.py の「☛ 手を自由に動かす」。2026-11-10 に作り直し)。
// 腕は 7 つの筋肉(上腕の上下・前後・ひねり、肘の曲げ、前腕のひねり、手首の上下・左右)で動く。
// そこで「手首の位置 3 + 手の向き 3 + 肘の回り具合 1」の 7 つを目標にすると、腕の形は 1 つに決まる(JRF・Gemini の案)。
// これを筋肉の値の空間で、Unity のヒューマノイドそのもの(SetHumanPose と骨の位置)を使って解く(ガウス・ニュートン法)。
// 関節の自然な範囲では縛らない(筋肉は ±4 まで)。解き始めは今の腕(前の状態からつながるように)。
// 届かないときは、向きを優先して、位置はできるだけ近くに。そのときの実際の値を新しい状態として返す(押し戻せばすぐ効くように)。
//   -ik CFG.json: {"side": "R"|"L",
//                  "state": {"pos": [x, y, z](手首。足元から m), "rot": [x, y, z, w](手の向き。足元の向きで), "swivel": 度} か null(今の腕から始める),
//                  "move": [右, 上, 前](m), "lrot": [x, y, z, w](手の骨の向きで右から掛ける), "swivel_delta": 度,
//                  "grip": true|false, "out": "結果.json"}
//   結果: {"muscles": {名前: 値}, "state": {...}, "reach_error": m, "rot_error": 度}
// 肘の回り具合: 肩と手首を結ぶ線の回りに、肘が「真後ろ」から何度回っているか(右腕で + は外へ。左腕は写したように)。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class PoseViewer
{
    static readonly string[] ArmMuscles = { "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out", "Forearm Stretch",
                                            "Forearm Twist In-Out", "Hand Down-Up", "Hand In-Out" };

    // 軽く握った指(Stretched は −1 で深く曲がる)
    static readonly Dictionary<string, float> GripFingers = new Dictionary<string, float>
    {
        ["Thumb.1 Stretched"] = -0.3f, ["Thumb.Spread"] = 0.2f, ["Thumb.2 Stretched"] = -0.4f, ["Thumb.3 Stretched"] = -0.4f,
        ["Index.1 Stretched"] = -0.45f, ["Index.Spread"] = 0f, ["Index.2 Stretched"] = -0.6f, ["Index.3 Stretched"] = -0.5f,
        ["Middle.1 Stretched"] = -0.55f, ["Middle.Spread"] = 0f, ["Middle.2 Stretched"] = -0.7f, ["Middle.3 Stretched"] = -0.55f,
        ["Ring.1 Stretched"] = -0.6f, ["Ring.Spread"] = 0f, ["Ring.2 Stretched"] = -0.75f, ["Ring.3 Stretched"] = -0.6f,
        ["Little.1 Stretched"] = -0.65f, ["Little.Spread"] = 0f, ["Little.2 Stretched"] = -0.8f, ["Little.3 Stretched"] = -0.6f,
    };

    // 肘の回り具合(度)。肩 s・肘 e・手首 w、基準は足元の真後ろ。右腕は外へ回すと +、左腕は写したように。
    static float Swivel(Transform root, Vector3 s, Vector3 e, Vector3 w, bool left, out float bend)
    {
        var a = (w - s).normalized;
        var refv = -root.forward;
        refv -= Vector3.Dot(refv, a) * a;
        if (refv.sqrMagnitude < 1e-6f) { refv = -root.up; refv -= Vector3.Dot(refv, a) * a; }
        var ep = (e - s) - Vector3.Dot(e - s, a) * a;
        bend = ep.magnitude / Mathf.Max(1e-4f, (e - s).magnitude);   // 0 = 腕がまっすぐ(回り具合が決まらない)
        if (ep.sqrMagnitude < 1e-10f) return 0f;
        float ang = Vector3.SignedAngle(refv, ep, a);
        // 右腕: 外(+x)へ回すほうを + に。左腕は逆向き(写したように)
        var outward = root.right * (left ? -1f : 1f);
        var test = Quaternion.AngleAxis(10f, a) * refv;
        if (Vector3.Dot(test - refv, outward) < 0) ang = -ang;
        return ang;
    }

    static Vector3 RotVec(Quaternion q)   // 回転を「軸 × 角度(ラジアン)」に
    {
        if (q.w < 0) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
        q.ToAngleAxis(out var deg, out var axis);
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return Vector3.zero;
        return axis * (deg * Mathf.Deg2Rad);
    }

    void RunIK(string cfgPath)
    {
        string outPath = null;
        string F(float x) => x.ToString("0.#####", CultureInfo.InvariantCulture);
        try
        {
            var cfg = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(cfgPath));
            outPath = (string)cfg["out"];
            if (vrm == null || handler == null) throw new Exception(L.T("VRM が読めていません", "No VRM loaded"));
            float[] Nums(Dictionary<string, object> d, string k, int n, float[] def)
            {
                if (d == null || !d.TryGetValue(k, out var o) || o == null) return def;
                var l = ((List<object>)o).Select(Convert.ToSingle).ToArray();
                return l.Length == n ? l : def;
            }
            float Num(Dictionary<string, object> d, string k, float def) =>
                d != null && d.TryGetValue(k, out var o) && o != null ? Convert.ToSingle(o) : def;
            bool left = cfg.TryGetValue("side", out var sd) && (string)sd == "L";
            string side = left ? "Left" : "Right";
            bool grip = cfg.TryGetValue("grip", out var g) && g is bool gb && gb;
            var stateIn = cfg.TryGetValue("state", out var so) ? so as Dictionary<string, object> : null;
            var move = Nums(cfg, "move", 3, new float[] { 0, 0, 0 });
            var lq = Nums(cfg, "lrot", 4, new float[] { 0, 0, 0, 1 });
            float dSwivel = Num(cfg, "swivel_delta", 0f);
            float away = Num(cfg, "swivel_away", 0f);
            float elbowDir = 0f;      // 肘を回す向き(回り具合の符号で)。止まったときも、回そうとした向き
            float elbowMoved = -1f;   // 度。+ で肘を体(胸)から離す、− で寄せる(向きはその場の形で決める)

            var animator = vrm.GetComponent<Animator>();
            var root = vrm.transform;
            // 動かす量はアバターの大きさに合わせる(humanScale: 人型の大きさ。ふつうの大人で 1 前後)
            float scale = cfg.TryGetValue("scale_move", out var smv) && smv is bool sm && sm ? animator.humanScale : 1f;
            for (int k = 0; k < 3; k++) move[k] *= scale;
            var ua = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var la = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hd = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

            handler.GetHumanPose(ref pose);
            var cur = (float[])pose.muscles.Clone();    // 今の腕(解き始め)
            var body = pose.bodyPosition;
            var bodyRot = pose.bodyRotation;
            var idx = ArmMuscles.Select(n => Muscles.Index[$"{side} {n}"]).ToArray();

            // 今の状態(足元の向きで)
            Vector3 P() => root.InverseTransformPoint(hd.position);
            Quaternion R() => Quaternion.Inverse(root.rotation) * hd.rotation;
            float S(out float bend) => Swivel(root, ua.position, la.position, hd.position, left, out bend);
            var pos = stateIn != null ? new Vector3(Nums(stateIn, "pos", 3, null)[0], Nums(stateIn, "pos", 3, null)[1], Nums(stateIn, "pos", 3, null)[2]) : P();
            Quaternion rot;
            if (stateIn != null) { var r = Nums(stateIn, "rot", 4, null); rot = new Quaternion(r[0], r[1], r[2], r[3]); }
            else rot = R();
            float swivel = stateIn != null ? Num(stateIn, "swivel", 0f) : S(out _);

            // 目標
            var tPos = pos + new Vector3(move[0], move[1], move[2]);
            var tRot = rot * new Quaternion(lq[0], lq[1], lq[2], lq[3]);
            if (Mathf.Abs(away) > 1e-3f)
            {
                // 肘を肩と手首を結ぶ線の回りに回して、胸から遠く(近く)なる向きへ。腕の形で外・内は変わるので、
                // 小さく回して確かめ、遠く(近く)なり続ける所まで(いちばん遠い・近い所を越えない)。2026-11-10、JRF
                var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                var sp = ua.position; var ax = (hd.position - sp).normalized; var el = la.position - sp;
                float Dist(float deg) => ((sp + Quaternion.AngleAxis(deg, ax) * el) - chest.position).magnitude;
                float want = Mathf.Sign(away);                       // + 離す、− 寄せる
                float dir = (Dist(2f) - Dist(-2f)) * want >= 0 ? 1f : -1f;   // AngleAxis の向き
                float ang = 0f, d0 = Dist(0f);
                while (ang < Mathf.Abs(away) - 1e-3f)
                {
                    float next = Mathf.Min(Mathf.Abs(away), ang + 2f);
                    float dn = Dist(dir * next);
                    if ((dn - d0) * want <= 1e-5f) break;            // それ以上は離れない(寄らない)
                    d0 = dn; ang = next;
                }
                if (ang < 1e-3f) status = want > 0 ? L.T("肘はこれ以上体から離れません", "The elbow cannot go farther from the body") : L.T("肘はこれ以上体に寄りません", "The elbow cannot come closer to the body");
                // AngleAxis の向きを Swivel の向きに直す
                float s0 = Swivel(root, sp, la.position, hd.position, left, out _);
                float s1 = Swivel(root, sp, sp + Quaternion.AngleAxis(5f, ax) * el, hd.position, left, out _);
                float plusSign = Mathf.DeltaAngle(s0, s1) >= 0 ? 1f : -1f;
                dSwivel += ang * dir * plusSign;
                elbowDir = dir * plusSign;
                elbowMoved = ang;
            }
            float tSw = swivel + dSwivel;

            float[] x = idx.Select(i => cur[i]).ToArray();
            void Apply(float[] v)
            {
                var m = (float[])cur.Clone();
                for (int k = 0; k < idx.Length; k++) m[idx[k]] = v[k];
                pose.muscles = m;
                pose.bodyPosition = body;
                pose.bodyRotation = bodyRot;
                handler.SetHumanPose(ref pose);
            }
            // 位置(m)・向き(ラジアン)・肘(ラジアン)の重み。届く所では位置を正確に(2026-11-10、JRF「絶対位置の追随が甘い」。前は 10)
            // 手の向き(絶対)はゆるめない(JRF: 伸ばしきっても保つ。無理な姿勢になってもよい)。1 度 ≈ 1 で、位置の 3 cm ほどに当たる
            float Wp = 30f, Wr = 60f, Ws = 6f;   // 肘: 10 度 ≈ 1(届く所では 7 つとも合う。ぶつかるのは届かないときだけ)
            // 肘は逆には反らせない(Forearm Stretch の上限は 1。元のポーズがそれを超えていれば元の値まで)
            int kStretch = Array.IndexOf(ArmMuscles, "Forearm Stretch");
            float[] lo = x.Select(_ => -4f).ToArray();
            float[] hi = x.Select(_ => 4f).ToArray();
            hi[kStretch] = Mathf.Max(1f, x[kStretch]);
            float[] Res(float[] v)
            {
                Apply(v);
                var dp = (P() - tPos) * Wp;
                var dr = RotVec(tRot * Quaternion.Inverse(R())) * Wr;
                float s = S(out var bend);
                float ds = Mathf.DeltaAngle(tSw, s) * Mathf.Deg2Rad * Ws * Mathf.Clamp01(bend * 3f);
                return new[] { dp.x, dp.y, dp.z, dr.x, dr.y, dr.z, ds };
            }
            float Norm2(float[] r) => r.Sum(t => t * t);

            float[] Solve(float[] x0, out float[] resOut)
            {
            var xc = (float[])x0.Clone();
            float lambda = 1e-3f;
            var rc = Res(xc);
            for (int it = 0; it < 150 && Norm2(rc) > 1e-10f; it++)
            {
                // 数値微分のヤコビ行列(7 × 7)
                int n = xc.Length;
                var J = new float[7, n];
                const float h = 0.004f;
                for (int k = 0; k < n; k++)
                {
                    var xp = (float[])xc.Clone(); xp[k] += h;
                    var rp = Res(xp);
                    for (int i = 0; i < 7; i++) J[i, k] = (rp[i] - rc[i]) / h;
                }
                // (JᵀJ + λ diag) dx = −Jᵀ r(レーベンバーグ・マーカート)
                var A = new double[n, n];
                var b = new double[n];
                for (int p = 0; p < n; p++)
                {
                    for (int q = 0; q < n; q++)
                    {
                        double sum = 0;
                        for (int i = 0; i < 7; i++) sum += J[i, p] * J[i, q];
                        A[p, q] = sum;
                    }
                    double bs = 0;
                    for (int i = 0; i < 7; i++) bs -= J[i, p] * rc[i];
                    b[p] = bs;
                }
                bool improved = false;
                for (int tries = 0; tries < 8 && !improved; tries++)
                {
                    var Al = (double[,])A.Clone();
                    for (int p = 0; p < n; p++) Al[p, p] += lambda * (1 + A[p, p]);
                    var dx = SolveLinear(Al, (double[])b.Clone());
                    if (dx == null) { lambda *= 10; continue; }
                    var xn = xc.Select((v, k) => Mathf.Clamp(v + (float)dx[k], lo[k], hi[k])).ToArray();
                    var rn = Res(xn);
                    if (Norm2(rn) < Norm2(rc)) { xc = xn; rc = rn; lambda = Mathf.Max(1e-6f, lambda * 0.3f); improved = true; }
                    else lambda *= 10;
                }
                if (!improved) break;
            }
            resOut = rc;
            return xc;
            }
            // 伸びきった腕から解き始めると肘の曲げ方が決まらず行き詰まるので、肘を曲げた腕からも解いて、よいほうを
            // (2026-11-10、上に伸ばしきってから下ろすと手が下がらなかった)
            var starts = new List<float[]> { x.Select((v, k) => Mathf.Clamp(v, lo[k], hi[k])).ToArray() };
            // 肘の向き(回り具合)の違う解に落ちないよう、上腕のひねりも変えた腕から(残差に回り具合も入っているので、合う解が選ばれる)
            int kTwist = Array.IndexOf(ArmMuscles, "Arm Twist In-Out");
            foreach (var bendTo in new[] { -0.2f, -0.6f })
            {
                if (x[kStretch] <= bendTo + 0.1f) continue;
                foreach (var tw in new[] { float.NaN, -1f, -0.5f, 0f, 0.5f, 1f })
                {
                    var sb0 = (float[])starts[0].Clone(); sb0[kStretch] = bendTo;
                    if (!float.IsNaN(tw)) sb0[kTwist] = tw;
                    starts.Add(sb0);
                }
            }
            float[] best = null; float bestN = float.MaxValue;
            // 2 段で解く: まず位置を主に(前の重み)、そこから手の向き・肘の回り具合を強くして詰める
            // (いきなり強い重みで解くと、肘を曲げきった形などで行き詰まる)
            // 最初から強い重みで解いたものも候補に(弱い重みの段で肘の回り具合が流され、近くの別の解に落ちることがあった。2026-11-10)
            foreach (var st in starts)
            {
                Wp = 30f; Wr = 60f; Ws = 6f;
                var xd = Solve(st, out var rd);
                float nd = Norm2(rd);
                if (nd < bestN - 1e-6f) { best = xd; bestN = nd; }
                Wp = 10f; Wr = 2f; Ws = 0.5f;
                var xa = Solve(st, out _);
                Wp = 30f; Wr = 60f; Ws = 6f;
                var xs = Solve(xa, out var rs);
                float nn = Norm2(rs);
                if (nn < bestN - 1e-6f) { best = xs; bestN = nn; }
            }
            x = best;
            Apply(x);

            // 結果の状態: 届かなかったぶんは、実際の値を新しい状態に(押し戻せばすぐ効く)
            var gotP = P();
            var gotR = R();
            float gotS = S(out var bendNow);
            float reach = (gotP - tPos).magnitude;
            float rotErr = Quaternion.Angle(gotR, tRot);
            // 届くかどうかは腕の長さで決める(解き方が外れただけなら目標を覚えたまま。ずれがたまらない)
            float armLen = (la.position - ua.position).magnitude + (hd.position - la.position).magnitude;
            float fromShoulder = (root.TransformPoint(tPos) - ua.position).magnitude;
            bool unreachable = fromShoulder > armLen * 0.99f;
            var nPos = unreachable && reach > 0.01f ? gotP : tPos;
            var nRot = tRot;   // 手の向きは目標を覚えたまま(回しきれなくても実際の値にはしない)
            // 伸ばしきって届かないときは肘の回り具合が決まらない(腕がまっすぐ)ので、前の値を覚えたまま(下ろしたとき元の肘の向きに)
            float nSw = !unreachable && bendNow > 0.15f && Mathf.Abs(Mathf.DeltaAngle(tSw, gotS)) > 5f ? gotS : tSw;

            var result = new Dictionary<string, float>();
            for (int k = 0; k < idx.Length; k++) result[$"{side} {ArmMuscles[k]}"] = x[k];
            if (grip)
                foreach (var kv in GripFingers) result[$"{side}Hand.{kv.Key}"] = kv.Value;
            // -shot のときに見えるよう、決まった姿勢にも入れる(Update で ApplyPose しても残る)
            if (staticPose != null)
                foreach (var kv in result) staticPose.Muscles[kv.Key] = kv.Value;
            ApplyPose();

            string V(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
            string Q(Quaternion q) => $"[{F(q.x)}, {F(q.y)}, {F(q.z)}, {F(q.w)}]";
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n \"muscles\": {");
            sb.Append(string.Join(", ", result.Select(kv => $"\"{kv.Key}\": {F(kv.Value)}")));
            sb.Append($"}},\n \"state\": {{\"pos\": {V(nPos)}, \"rot\": {Q(nRot)}, \"swivel\": {F(nSw)}}},");
            sb.Append($"\n \"scale\": {F(scale)},");
            if (elbowMoved >= 0) sb.Append($"\n \"elbow_moved\": {F(elbowMoved)},\n \"elbow_dir\": {F(elbowDir)},");
            sb.Append($"\n \"reach_error\": {F(reach)},\n \"rot_error\": {F(rotErr)},\n \"swivel_now\": {F(gotS)}\n}}\n");
            File.WriteAllText(outPath, sb.ToString());
            status = L.T($"IK: 届かなかった距離 {reach:0.000} m、向き {rotErr:0} 度", $"IK: missed by {reach:0.000} m, {rotErr:0} deg");
        }
        catch (Exception e)
        {
            if (outPath != null) File.WriteAllText(outPath, "{\"error\": \"" + e.Message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}");
            status = "IK: " + e.Message;
        }
    }

    static double[] SolveLinear(double[,] A, double[] b)
    {
        int n = b.Length;
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[piv, c])) piv = r;
            if (Math.Abs(A[piv, c]) < 1e-12) return null;
            if (piv != c)
            {
                for (int k = 0; k < n; k++) { var t = A[c, k]; A[c, k] = A[piv, k]; A[piv, k] = t; }
                var tb = b[c]; b[c] = b[piv]; b[piv] = tb;
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = A[r, c] / A[c, c];
                for (int k = c; k < n; k++) A[r, k] -= f * A[c, k];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int k = r + 1; k < n; k++) s -= A[r, k] * x[k];
            x[r] = s / A[r, r];
        }
        return x;
    }
}
