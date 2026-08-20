using System;
using System.Collections.Generic;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        internal enum MobileCenterToastKind
        {
            Normal = 0,
            Item = 1,
            Gold = 2,
            Experience = 3,
        }

        private sealed class MobileCenterToastEntry
        {
            public long StartAtMs;
            public string Message;
            public MobileCenterToastKind Kind;
            public GTextField Field;
        }

        private const string MobileCenterToastRootName = "__codex_mobile_center_toast_root";
        private static readonly List<MobileCenterToastEntry> _mobileCenterToasts = new List<MobileCenterToastEntry>(12);
        private static GComponent _mobileCenterToastRoot;

        internal static void PushMobileCenterToast(string message, MobileCenterToastKind kind = MobileCenterToastKind.Normal)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (string.IsNullOrWhiteSpace(message))
                return;

            if (_stage == null || !_initialized)
                return;

            EnsureMobileCenterToastRoot();

            if (_mobileCenterToastRoot == null || _mobileCenterToastRoot._disposed)
                return;

            string cleaned = message.Trim();
            if (cleaned.Length == 0)
                return;

            long now = 0;
            try { now = CMain.Time; } catch { now = 0; }

            try
            {
                if (_mobileCenterToasts.Count >= 8)
                    RemoveMobileCenterToastAt(0);
            }
            catch
            {
            }

            GTextField field;
            try
            {
                field = new GTextField
                {
                    touchable = false,
                    text = cleaned,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle,
                    autoSize = AutoSizeType.Both,
                    singleLine = true,
                };
            }
            catch
            {
                return;
            }

            try
            {
                TextFormat tf = field.textFormat;
                tf.size = Math.Clamp(Math.Max(tf.size, 26), 1, 192);
                tf.color = kind switch
                {
                    MobileCenterToastKind.Gold => new Color(255, 220, 60),
                    MobileCenterToastKind.Experience => new Color(120, 220, 255),
                    _ => new Color(255, 255, 255),
                };
                tf.bold = true;
                field.textFormat = tf;
                field.stroke = 2;
                field.strokeColor = new Color(0, 0, 0, 200);
            }
            catch
            {
            }

            try
            {
                _mobileCenterToastRoot.AddChild(field);
            }
            catch
            {
                try { field.Dispose(); } catch { }
                return;
            }

            var entry = new MobileCenterToastEntry
            {
                StartAtMs = now,
                Message = cleaned,
                Kind = kind,
                Field = field,
            };

            _mobileCenterToasts.Add(entry);
        }

        private static void EnsureMobileCenterToastRoot()
        {
            if (_mobileCenterToastRoot != null && !_mobileCenterToastRoot._disposed && _mobileCenterToastRoot.parent != null)
                return;

            try
            {
                _mobileCenterToastRoot?.Dispose();
            }
            catch
            {
            }

            _mobileCenterToastRoot = null;

            try
            {
                _mobileCenterToastRoot = new GComponent
                {
                    name = MobileCenterToastRootName,
                    touchable = false,
                    opaque = false,
                };

                GComponent parent = _mobileOverlaySafeAreaRoot;
                if (parent == null || parent._disposed)
                    parent = _uiManager?.OverlayLayer ?? GRoot.inst;

                parent.AddChild(_mobileCenterToastRoot);
                _mobileCenterToastRoot.sortingOrder = 9000;
                _mobileCenterToastRoot.SetSize(parent.width, parent.height);
            }
            catch
            {
                try { _mobileCenterToastRoot?.Dispose(); } catch { }
                _mobileCenterToastRoot = null;
            }
        }

        private static void TryRefreshMobileCenterToastIfDue()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return;

            if (_stage == null || !_initialized)
                return;

            if (_mobileCenterToasts.Count == 0)
                return;

            EnsureMobileCenterToastRoot();
            if (_mobileCenterToastRoot == null || _mobileCenterToastRoot._disposed)
                return;

            try
            {
                if (_mobileCenterToastRoot.parent != null && !_mobileCenterToastRoot.parent._disposed)
                {
                    _mobileCenterToastRoot.SetPosition(0f, 0f);
                    _mobileCenterToastRoot.SetSize(_mobileCenterToastRoot.parent.width, _mobileCenterToastRoot.parent.height);
                }
            }
            catch
            {
            }

            long now = 0;
            try { now = CMain.Time; } catch { now = 0; }

            const long holdMs = 900;
            const long scrollMs = 1400;
            const float scrollDistance = 52f;
            const float stackSpacing = 34f;

            for (int i = _mobileCenterToasts.Count - 1; i >= 0; i--)
            {
                MobileCenterToastEntry entry = _mobileCenterToasts[i];
                if (entry == null || entry.Field == null || entry.Field._disposed)
                {
                    _mobileCenterToasts.RemoveAt(i);
                    continue;
                }

                long age = now - entry.StartAtMs;
                if (age >= holdMs + scrollMs)
                {
                    RemoveMobileCenterToastAt(i);
                }
            }

            if (_mobileCenterToasts.Count == 0)
                return;

            float rootW = Math.Max(1f, _mobileCenterToastRoot.width);
            float rootH = Math.Max(1f, _mobileCenterToastRoot.height);

            float centerX = rootW / 2f;
            float centerY = rootH * 0.45f;

            int count = _mobileCenterToasts.Count;
            for (int i = 0; i < count; i++)
            {
                MobileCenterToastEntry entry = _mobileCenterToasts[i];
                if (entry == null || entry.Field == null || entry.Field._disposed)
                    continue;

                long age = now - entry.StartAtMs;
                float progress = 0f;
                if (age > holdMs && scrollMs > 0)
                    progress = Math.Clamp((age - holdMs) / (float)scrollMs, 0f, 1f);

                float alpha = 1f - progress;

                int indexFromBottom = (count - 1) - i;
                float stackOffsetY = -indexFromBottom * stackSpacing;
                float scrollOffsetY = -scrollDistance * progress;
                float y = centerY + stackOffsetY + scrollOffsetY;

                try
                {
                    entry.Field.alpha = alpha;
                }
                catch
                {
                }

                try
                {
                    float x = centerX - entry.Field.width / 2f;
                    entry.Field.SetPosition(x, y);
                }
                catch
                {
                }
            }
        }

        private static void RemoveMobileCenterToastAt(int index)
        {
            if (index < 0 || index >= _mobileCenterToasts.Count)
                return;

            MobileCenterToastEntry entry = _mobileCenterToasts[index];
            _mobileCenterToasts.RemoveAt(index);

            try
            {
                if (entry?.Field != null && !entry.Field._disposed)
                    entry.Field.Dispose();
            }
            catch
            {
            }
        }
    }
}
