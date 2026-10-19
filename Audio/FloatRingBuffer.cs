using System;

namespace Surround71.Audio
{
    /// <summary>单生产者/单消费者浮点环形缓冲区（单位：float，交错）。</summary>
    public sealed class FloatRingBuffer
    {
        private readonly float[] _buffer;
        private readonly object _sync = new object();
        private int _write;
        private int _read;
        private int _count;
        private long _overflows;

        public FloatRingBuffer(int capacityFloats)
        {
            _buffer = new float[Math.Max(4096, capacityFloats)];
        }

        public int Capacity => _buffer.Length;
        public int Count { get { lock (_sync) return _count; } }
        public long Overflows => System.Threading.Interlocked.Read(ref _overflows);

        public void Write(float[] src, int offset, int count)
        {
            if (count <= 0) return;
            if (count > _buffer.Length)
            {
                offset += count - _buffer.Length;
                count = _buffer.Length;
            }

            lock (_sync)
            {
                int free = _buffer.Length - _count;
                if (count > free)
                {
                    int drop = count - free;
                    _read = (_read + drop) % _buffer.Length;
                    _count -= drop;
                    System.Threading.Interlocked.Add(ref _overflows, drop);
                }

                int first = Math.Min(count, _buffer.Length - _write);
                Array.Copy(src, offset, _buffer, _write, first);
                if (count > first)
                    Array.Copy(src, offset + first, _buffer, 0, count - first);

                _write = (_write + count) % _buffer.Length;
                _count += count;
            }
        }

        public int Read(float[] dst, int offset, int count)
        {
            int got;
            lock (_sync)
            {
                got = Math.Min(count, _count);
                if (got > 0)
                {
                    int first = Math.Min(got, _buffer.Length - _read);
                    Array.Copy(_buffer, _read, dst, offset, first);
                    if (got > first)
                        Array.Copy(_buffer, 0, dst, offset + first, got - first);

                    _read = (_read + got) % _buffer.Length;
                    _count -= got;
                }
            }

            if (got < count) Array.Clear(dst, offset + got, count - got);
            return got;
        }

        public void Clear()
        {
            lock (_sync) { _read = _write = 0; _count = 0; }
        }
    }
}
