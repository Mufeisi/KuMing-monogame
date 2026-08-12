namespace Launcher.PlayerShell;

internal sealed class ReadOnlySliceStream : Stream
{
    private readonly Stream _stream;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _leaveOpen;
    private long _position;

    public ReadOnlySliceStream(Stream stream, long start, long length, bool leaveOpen)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("底层流必须支持读取和定位", nameof(stream));
        if (start < 0 || length < 0 || start > stream.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        _stream = stream;
        _start = start;
        _length = length;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, _length - _position);
        if (count <= 0) return 0;
        _stream.Position = _start + _position;
        int read = _stream.Read(buffer[..count]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (next < 0 || next > _length) throw new IOException("定位越出载荷边界");
        _position = next;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen) _stream.Dispose();
        base.Dispose(disposing);
    }
}
