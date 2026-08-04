namespace HardwareMonitorByYuinn.Business.TimeSeries;

internal sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _start;
    private int _count;

    public RingBuffer(int capacity) => _buffer = new T[capacity];

    public void Add(T item)
    {
        lock (_lock)
        {
            int index = (_start + _count) % _buffer.Length;
            _buffer[index] = item;
            if (_count < _buffer.Length)
                _count++;
            else
                _start = (_start + 1) % _buffer.Length;
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _buffer[(_start + i) % _buffer.Length];
            return result;
        }
    }
}
