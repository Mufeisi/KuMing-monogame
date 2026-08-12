using System;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static string _pendingMobileChatPrefillText;
        private static DateTime _pendingMobileChatPrefillExpiresUtc = DateTime.MinValue;

        public static void BeginMobileChatWhisperTo(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return;

            string cleaned = target.Trim();
            if (cleaned.Length == 0)
                return;

            int spaceIndex = cleaned.IndexOf(' ');
            if (spaceIndex >= 0)
                cleaned = cleaned.Substring(0, spaceIndex);

            if (cleaned.Length == 0)
                return;

            string prefill = "/" + cleaned + " ";

            _pendingMobileChatPrefillText = prefill;
            _pendingMobileChatPrefillExpiresUtc = DateTime.UtcNow.AddSeconds(20);

            TryApplyPendingMobileChatPrefillIfDue();
        }

        private static void TryApplyPendingMobileChatPrefillIfDue()
        {
            string text = _pendingMobileChatPrefillText;
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (_pendingMobileChatPrefillExpiresUtc != DateTime.MinValue && DateTime.UtcNow > _pendingMobileChatPrefillExpiresUtc)
            {
                _pendingMobileChatPrefillText = null;
                _pendingMobileChatPrefillExpiresUtc = DateTime.MinValue;
                return;
            }

            if (_mobileChatInput == null || _mobileChatInput._disposed)
                return;

            try
            {
                _mobileChatInput.text = text;
                try
                {
                    _mobileChatInput.caretPosition = (_mobileChatInput.text ?? string.Empty).Length;
                }
                catch
                {
                }

                _mobileChatInput.RequestFocus();
            }
            catch
            {
                return;
            }

            _pendingMobileChatPrefillText = null;
            _pendingMobileChatPrefillExpiresUtc = DateTime.MinValue;
        }
    }
}
