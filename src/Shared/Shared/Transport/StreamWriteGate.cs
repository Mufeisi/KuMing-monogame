using System;
using System.Threading;

namespace Shared.Transport;

/// <summary>单个连接同向写入的轻量门闩；业务队列由调用方保留。</summary>
public sealed class StreamWriteGate : IDisposable
{
    private int _state;

    public bool TryEnter() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    public void Complete() => Interlocked.CompareExchange(ref _state, 0, 1);

    public void Dispose() => Interlocked.Exchange(ref _state, 2);
}
