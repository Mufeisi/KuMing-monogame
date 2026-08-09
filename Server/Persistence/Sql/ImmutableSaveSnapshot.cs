using System;

namespace Server.Persistence.Sql
{
    /// <summary>
    /// 后台保存的不可变所有权信封。Payload 只在构造时写入且不对外暴露；
    /// 快照工厂必须返回与游戏可变状态完全脱离的 DTO，交接后仅写线程可通过 Commit 读取。
    /// </summary>
    internal sealed class ImmutableSaveSnapshot<TSnapshot>
    {
        private readonly TSnapshot _payload;

        internal long Generation { get; }
        internal int CaptureThreadId { get; }

        internal ImmutableSaveSnapshot(long generation, int captureThreadId, TSnapshot payload)
        {
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
            Generation = generation;
            CaptureThreadId = captureThreadId;
            _payload = payload;
        }

        internal bool Commit(Func<TSnapshot, bool> commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            return commit(_payload);
        }
    }
}
