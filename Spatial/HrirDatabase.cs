using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Surround71.Spatial
{
    /// <summary>一个方向上的双耳 HRIR。</summary>
    public sealed class HrirPair
    {
        public float[] Left;
        public float[] Right;
    }

    /// <summary>
    /// MIT KEMAR 紧凑 HRIR 数据库（368 个方向，44.1kHz / 128 抽头，内嵌于程序集）。
    ///
    /// 文件命名：H&lt;仰角&gt;e&lt;方位角&gt;a.wav
    /// 声道：ch0 = 左耳，ch1 = 右耳
    /// 方位角：0° = 正前方，90° = 正右侧，180° = 正后方（左半侧用镜像 + 交换左右耳）
    /// 仰角：-40° ~ +90°
    /// </summary>
    public sealed class HrirDatabase
    {
        public const int NativeRate = 44100;
        public const int NativeTaps = 128;

        private readonly Dictionary<long, HrirPair> _map = new Dictionary<long, HrirPair>();
        private readonly Dictionary<int, List<int>> _azimuths = new Dictionary<int, List<int>>();
        private readonly List<int> _elevations = new List<int>();

        public int Taps { get; private set; }
        public int PointCount => _map.Count;
        public int MinElevation => _elevations.Count > 0 ? _elevations[0] : 0;
        public int MaxElevation => _elevations.Count > 0 ? _elevations[_elevations.Count - 1] : 0;
        public IReadOnlyList<int> Elevations => _elevations;

        private static readonly System.Collections.Generic.Dictionary<long, HrirDatabase> _cache
            = new System.Collections.Generic.Dictionary<long, HrirDatabase>();

        /// <summary>按采样率缓存数据库，避免反复启动管线时重复解析 368 个文件。</summary>
        public static HrirDatabase Get(int targetRate, int taps)
        {
            long key = ((long)targetRate << 8) | (uint)(taps & 0xFF);
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var db) && db.PointCount > 0) return db;
                db = Load(targetRate, taps);
                _cache[key] = db;
                return db;
            }
        }

        public static HrirDatabase Load(int targetRate, int taps)
        {            var db = new HrirDatabase { Taps = taps };
            var asm = Assembly.GetExecutingAssembly();
            double ratio = (double)targetRate / NativeRate;
            int resampledLen = (int)Math.Round(NativeTaps * ratio);
            bool needResample = Math.Abs(ratio - 1.0) > 1e-9;

            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                if (res.IndexOf(".hrir.", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!TryParseName(res, out int elev, out int azim)) continue;

                var bytes = ReadAll(asm, res);
                if (bytes == null) continue;
                if (!TryParseWav(bytes, out float[] l, out float[] r)) continue;

                if (needResample)
                {
                    l = Resampler.ResampleLanczos(l, ratio, resampledLen);
                    r = Resampler.ResampleLanczos(r, ratio, resampledLen);
                }

                var pair = new HrirPair { Left = Fit(l, taps), Right = Fit(r, taps) };
                db._map[Key(elev, azim)] = pair;

                if (!db._azimuths.TryGetValue(elev, out var list))
                {
                    list = new List<int>();
                    db._azimuths[elev] = list;
                }
                list.Add(azim);
            }

            foreach (var kv in db._azimuths) kv.Value.Sort();
            db._elevations.AddRange(db._azimuths.Keys);
            db._elevations.Sort();
            return db;
        }

        /// <summary>按方向取 HRIR（方位角 0=正前，正=右；仰角正=上）。</summary>
        public HrirPair Get(float elevationDeg, float azimuthDeg)
        {
            float az = Normalize(azimuthDeg);
            bool mirror = az < 0f;
            if (mirror) az = -az;

            float[] dataLeftEar = Interpolate(elevationDeg, az, 0);   // 实测 ch0
            float[] dataRightEar = Interpolate(elevationDeg, az, 1);  // 实测 ch1

            // 源在左侧时，用右半侧测量点镜像：左右耳互换
            return mirror
                ? new HrirPair { Left = dataRightEar, Right = dataLeftEar }
                : new HrirPair { Left = dataLeftEar, Right = dataRightEar };
        }

        /// <summary>ear: 0 = 数据 ch0(左耳)，1 = 数据 ch1(右耳)。返回 Taps 长度的 HRIR。</summary>
        private float[] Interpolate(float elevationDeg, float azimuthDeg, int ear)
        {
            if (_elevations.Count == 0) return new float[Taps];

            float elev = Math.Max(MinElevation, Math.Min(MaxElevation, elevationDeg));

            int e0 = _elevations[0], e1 = _elevations[_elevations.Count - 1];
            for (int i = 0; i < _elevations.Count - 1; i++)
            {
                if (elev >= _elevations[i] && elev <= _elevations[i + 1])
                {
                    e0 = _elevations[i];
                    e1 = _elevations[i + 1];
                    break;
                }
            }

            float[] a0 = AlongAzimuth(e0, azimuthDeg, ear);
            if (e0 == e1) return a0;
            float[] a1 = AlongAzimuth(e1, azimuthDeg, ear);

            float t = (elev - e0) / (e1 - e0);
            var outp = new float[Taps];
            for (int i = 0; i < Taps; i++)
                outp[i] = a0[i] * (1f - t) + a1[i] * t;
            return outp;
        }

        private float[] AlongAzimuth(int elevation, float azimuth, int ear)
        {
            var list = _azimuths[elevation];

            int lo = list[0], hi = list[list.Count - 1];
            if (azimuth <= lo) return Copy(elevation, lo, ear);
            if (azimuth >= hi) return Copy(elevation, hi, ear);

            for (int i = 0; i < list.Count - 1; i++)
            {
                if (azimuth >= list[i] && azimuth <= list[i + 1])
                {
                    float a0 = list[i], a1 = list[i + 1];
                    float t = (azimuth - a0) / Math.Max(1f, a1 - a0);
                    var p0 = _map[Key(elevation, (int)a0)];
                    var p1 = _map[Key(elevation, (int)a1)];
                    var src0 = ear == 0 ? p0.Left : p0.Right;
                    var src1 = ear == 0 ? p1.Left : p1.Right;

                    var outp = new float[Taps];
                    for (int k = 0; k < Taps; k++)
                        outp[k] = src0[k] * (1f - t) + src1[k] * t;
                    return outp;
                }
            }

            return Copy(elevation, lo, ear);
        }

        private float[] Copy(int elevation, int azimuth, int ear)
        {
            var p = _map[Key(elevation, azimuth)];
            var src = ear == 0 ? p.Left : p.Right;
            var outp = new float[Taps];
            Array.Copy(src, outp, Math.Min(Taps, src.Length));
            return outp;
        }

        private static float Normalize(float az)
        {
            az %= 360f;
            if (az > 180f) az -= 360f;
            if (az <= -180f) az += 360f;
            return az;
        }

        private static float[] Fit(float[] x, int taps)
        {
            var y = new float[taps];
            int n = Math.Min(taps, x.Length);
            Array.Copy(x, 0, y, 0, n);
            if (x.Length > taps) Resampler.FadeOut(y, 8);
            return y;
        }

        private static long Key(int elev, int azim) => ((long)(elev + 1000) << 32) | (uint)(azim + 1000);

        private static bool TryParseName(string resourceName, out int elev, out int azim)
        {
            elev = 0; azim = 0;

            int lastDot = resourceName.LastIndexOf('.');
            if (lastDot <= 0) return false;
            int prevDot = resourceName.LastIndexOf('.', lastDot - 1);
            if (prevDot < 0) return false;

            string stem = resourceName.Substring(prevDot + 1, lastDot - prevDot - 1); // 例如 H-10e005a
            if (stem.Length < 5) return false;
            if (stem[0] != 'H' && stem[0] != 'h') return false;

            string body = stem.Substring(1);
            int e = body.IndexOf('e');
            if (e <= 0) return false;

            if (!int.TryParse(body.Substring(0, e), out elev)) return false;

            string azPart = body.Substring(e + 1);
            if (azPart.Length < 3) return false;
            return int.TryParse(azPart.Substring(0, 3), out azim);
        }

        private static byte[] ReadAll(Assembly asm, string name)
        {
            try
            {
                using var s = asm.GetManifestResourceStream(name);
                if (s == null) return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        private static bool TryParseWav(byte[] b, out float[] left, out float[] right)
        {
            left = null; right = null;
            if (b == null || b.Length < 44) return false;

            int channels = BitConverter.ToInt16(b, 22);
            int bits = BitConverter.ToInt16(b, 34);
            if (channels != 2 || bits != 16) return false;

            int pos = 12, dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (id == "data")
                {
                    dataPos = pos + 8;
                    dataLen = Math.Min(size, b.Length - dataPos);
                    break;
                }
                pos += 8 + size + (size & 1);
            }

            if (dataPos < 0) return false;

            int frames = dataLen / 4;
            if (frames <= 0) return false;

            left = new float[frames];
            right = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                left[i] = BitConverter.ToInt16(b, dataPos + i * 4) / 32768f;
                right[i] = BitConverter.ToInt16(b, dataPos + i * 4 + 2) / 32768f;
            }
            return true;
        }
    }
}
