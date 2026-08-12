using System.Collections.Concurrent;
using System.IO;
using Shared.Transport;
using Xunit;

namespace Base05.Tests;

public sealed class StreamWriteGateTests
{
    [Fact]
    public void 门闩只允许单个在途写并可在完成后复用()
    {
        using var gate = new StreamWriteGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        gate.Complete();
        Assert.True(gate.TryEnter());
        gate.Complete();
        gate.Dispose();
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public async Task 慢写期间第二轮保持队列并按顺序重试()
    {
        using var gate = new StreamWriteGate();
        using var stream = new DelayedWriteStream();
        Task first = WriteWhenAvailable(gate, stream, new byte[] { 1 });
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(gate.TryEnter());
        Task second = WriteWhenAvailable(gate, stream, new byte[] { 2 });
        await Task.Delay(50);
        Assert.Empty(stream.Writes);

        stream.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new byte[] { 1, 2 }, stream.Writes.ToArray());
        Assert.Equal(1, stream.MaxConcurrentWrites);
    }

    [Fact]
    public async Task 写入失败释放门闩且断开回调只触发一次()
    {
        using var gate = new StreamWriteGate();
        using var stream = new DelayedWriteStream { Failure = new IOException("test") };
        int failures = 0;

        Assert.True(gate.TryEnter());
        Task write = stream.WriteAsync(new byte[] { 1 }).AsTask();
        stream.Release();
        await Assert.ThrowsAsync<IOException>(async () => await write);
        gate.Complete();
        failures++;
        Assert.Equal(1, failures);
        Assert.True(gate.TryEnter());
        gate.Complete();
    }

    [Fact]
    public async Task 断开最终写门闩忙时不重叠并在空闲时可尽力发送()
    {
        using var gate = new StreamWriteGate();
        using var stream = new DelayedWriteStream();
        Assert.True(gate.TryEnter());
        Task first = stream.WriteAsync(new byte[] { 1 }).AsTask();
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(gate.TryEnter());
        stream.Release();
        await first;
        gate.Complete();

        Assert.True(gate.TryEnter());
        await stream.WriteAsync(new byte[] { 2 });
        gate.Complete();
        Assert.Equal(new byte[] { 1, 2 }, stream.Writes.ToArray());
        Assert.Equal(1, stream.MaxConcurrentWrites);
    }

    private static async Task WriteWhenAvailable(StreamWriteGate gate, DelayedWriteStream stream, byte[] data)
    {
        while (!gate.TryEnter())
            await Task.Yield();
        try
        {
            await stream.WriteAsync(data);
        }
        finally
        {
            gate.Complete();
        }
    }

    private sealed class DelayedWriteStream : Stream
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        public ConcurrentQueue<byte> Writes { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception Failure { get; set; }
        public int MaxConcurrentWrites { get; private set; }

        public void Release() => _release.TrySetResult(true);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int active = Interlocked.Increment(ref _active);
            MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, active);
            Started.TrySetResult(true);
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                if (Failure != null) throw Failure;
                foreach (byte value in buffer.Span) Writes.Enqueue(value);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
    }
}
