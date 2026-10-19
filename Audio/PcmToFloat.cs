using System;
using NAudio.Wave;

namespace Surround71.Audio
{
    /// <summary>把采集端原始字节转换成"交错 N 声道"浮点（保留全部声道）。</summary>
    public sealed class PcmToFloat
    {
        private enum Kind { Float32, Pcm16, Pcm24, Pcm32 }

        private static readonly Guid SubtypeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

        private readonly Kind _kind;
        private readonly int _bytesPerSample;

        public WaveFormat Format { get; }
        public int Channels { get; }
        public int BytesPerFrame => Channels * _bytesPerSample;

        public PcmToFloat(WaveFormat format)
        {
            Format = format;
            Channels = Math.Max(1, format.Channels);

            if (IsFloat(format))
            {
                _kind = Kind.Float32;
                _bytesPerSample = 4;
            }
            else
            {
                _bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
                _kind = _bytesPerSample switch
                {
                    1 => Kind.Pcm16,
                    2 => Kind.Pcm16,
                    3 => Kind.Pcm24,
                    _ => Kind.Pcm32,
                };
                if (_bytesPerSample == 1) _bytesPerSample = 2;
            }
        }

        private static bool IsFloat(WaveFormat fmt)
        {
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat) return true;
            if (fmt is WaveFormatExtensible ext) return ext.SubFormat == SubtypeFloat;
            return false;
        }

        public int MaxFrames(int bytes)
        {
            int bpf = BytesPerFrame;
            return bpf <= 0 ? 0 : bytes / bpf;
        }

        /// <summary>转换。dest 长度需 &gt;= 帧数 * Channels。返回写入的帧数。</summary>
        public int Convert(byte[] data, int bytes, float[] dest)
        {
            int bpf = BytesPerFrame;
            if (bpf <= 0) return 0;
            int frames = bytes / bpf;

            for (int f = 0; f < frames; f++)
            {
                int baseOff = f * bpf;
                int o = f * Channels;
                for (int c = 0; c < Channels; c++)
                    dest[o + c] = ReadSample(data, baseOff + c * _bytesPerSample);
            }

            return frames;
        }

        private float ReadSample(byte[] d, int off)
        {
            switch (_kind)
            {
                case Kind.Float32:
                    return BitConverter.ToSingle(d, off);

                case Kind.Pcm16:
                    return BitConverter.ToInt16(d, off) / 32768f;

                case Kind.Pcm24:
                    {
                        int v = d[off] | (d[off + 1] << 8) | (d[off + 2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        return v / 8388608f;
                    }

                default:
                    return BitConverter.ToInt32(d, off) / 2147483648f;
            }
        }
    }
}
