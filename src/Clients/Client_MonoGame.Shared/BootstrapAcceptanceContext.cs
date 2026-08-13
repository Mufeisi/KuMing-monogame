using System;
using System.Collections.Generic;
using System.Threading;
using Shared.Security;

namespace MonoShare
{
    internal static class BootstrapAcceptanceContext
    {
        private static readonly AsyncLocal<IReadOnlyDictionary<string, BootstrapManifestTrustedKey>> CurrentValue = new AsyncLocal<IReadOnlyDictionary<string, BootstrapManifestTrustedKey>>();
        public static IReadOnlyDictionary<string, BootstrapManifestTrustedKey> TrustedKeys => CurrentValue.Value;

        public static IDisposable UseTrustedKeys(IReadOnlyDictionary<string, BootstrapManifestTrustedKey> trustedKeys)
        {
            if (trustedKeys == null || trustedKeys.Count == 0) throw new ArgumentException("验收可信密钥不能为空。", nameof(trustedKeys));
            IReadOnlyDictionary<string, BootstrapManifestTrustedKey> previous = CurrentValue.Value;
            CurrentValue.Value = trustedKeys;
            return new Scope(() => CurrentValue.Value = previous);
        }

        private sealed class Scope : IDisposable
        {
            private Action _dispose;
            public Scope(Action dispose) { _dispose = dispose; }
            public void Dispose() { Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
        }
    }
}
