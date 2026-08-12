using System;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static GComponent _mobilePreLoginUpdateOverlay;
        private static GTextField _mobilePreLoginUpdateTitle;
        private static GTextField _mobilePreLoginUpdateStatus;
        private static GGraph _mobilePreLoginUpdateProgressBack;
        private static GGraph _mobilePreLoginUpdateProgressFill;
        private static float _mobilePreLoginUpdateLastProgress01 = -2F;
        private static int _mobilePreLoginUpdateLastLayoutWidth;
        private static int _mobilePreLoginUpdateLastLayoutHeight;

        public static bool TryShowMobilePreLoginUpdateOverlay()
        {
            if (!_initialized || _stage == null || _uiManager == null)
                return false;

            try
            {
                EnsureMobileOverlaySafeAreaLayout(force: false);

                if (_mobileOverlaySafeAreaRoot == null || _mobileOverlaySafeAreaRoot.displayObject == null || _mobileOverlaySafeAreaRoot.displayObject.isDisposed)
                    return false;

                if (_mobilePreLoginUpdateOverlay == null || _mobilePreLoginUpdateOverlay.displayObject == null || _mobilePreLoginUpdateOverlay.displayObject.isDisposed)
                {
                    _mobilePreLoginUpdateOverlay = new GComponent
                    {
                        name = "MobilePreLoginUpdateOverlay",
                        opaque = false,
                        touchable = false,
                    };
                    _mobileOverlaySafeAreaRoot.AddChild(_mobilePreLoginUpdateOverlay);
                    _mobilePreLoginUpdateOverlay.SetPosition(0, 0);
                    _mobilePreLoginUpdateOverlay.AddRelation(_mobileOverlaySafeAreaRoot, RelationType.Size);

                    BuildMobilePreLoginUpdateOverlayUi();
                    LayoutMobilePreLoginUpdateOverlay(force: true);
                }

                _mobilePreLoginUpdateOverlay.visible = true;
                LayoutMobilePreLoginUpdateOverlay(force: false);
                return true;
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: TryShowMobilePreLoginUpdateOverlay 异常：" + ex);
                return false;
            }
        }

        public static void HideMobilePreLoginUpdateOverlay()
        {
            try
            {
                if (_mobilePreLoginUpdateOverlay == null)
                    return;

                if (_mobilePreLoginUpdateOverlay.displayObject == null || _mobilePreLoginUpdateOverlay.displayObject.isDisposed)
                {
                    _mobilePreLoginUpdateOverlay = null;
                    _mobilePreLoginUpdateTitle = null;
                    _mobilePreLoginUpdateStatus = null;
                    _mobilePreLoginUpdateProgressBack = null;
                    _mobilePreLoginUpdateProgressFill = null;
                    _mobilePreLoginUpdateLastProgress01 = -2F;
                    _mobilePreLoginUpdateLastLayoutWidth = 0;
                    _mobilePreLoginUpdateLastLayoutHeight = 0;
                    return;
                }

                _mobilePreLoginUpdateOverlay.visible = false;
            }
            catch
            {
            }
        }

        public static void SetMobilePreLoginUpdateStatus(string statusText)
        {
            if (!TryShowMobilePreLoginUpdateOverlay())
                return;

            if (_mobilePreLoginUpdateStatus == null || _mobilePreLoginUpdateStatus.displayObject == null || _mobilePreLoginUpdateStatus.displayObject.isDisposed)
                return;

            _mobilePreLoginUpdateStatus.text = statusText ?? string.Empty;
        }

        public static void SetMobilePreLoginUpdateProgress(float progress01)
        {
            if (!TryShowMobilePreLoginUpdateOverlay())
                return;

            if (_mobilePreLoginUpdateProgressBack == null || _mobilePreLoginUpdateProgressFill == null)
                return;

            if (_mobilePreLoginUpdateProgressBack.displayObject == null || _mobilePreLoginUpdateProgressBack.displayObject.isDisposed)
                return;

            float normalized = progress01;
            if (normalized < 0F || float.IsNaN(normalized) || float.IsInfinity(normalized))
                normalized = -1F;
            if (normalized > 1F)
                normalized = 1F;

            if (Math.Abs(normalized - _mobilePreLoginUpdateLastProgress01) < 0.0001F)
                return;

            _mobilePreLoginUpdateLastProgress01 = normalized;

            if (normalized < 0F)
            {
                _mobilePreLoginUpdateProgressBack.visible = false;
                _mobilePreLoginUpdateProgressFill.visible = false;
                return;
            }

            _mobilePreLoginUpdateProgressBack.visible = true;
            _mobilePreLoginUpdateProgressFill.visible = true;

            float backWidth = _mobilePreLoginUpdateProgressBack.width;
            int fillWidth = (int)Math.Round(backWidth * normalized);
            if (fillWidth < 0) fillWidth = 0;
            int backWidthInt = (int)Math.Round(backWidth);
            if (fillWidth > backWidthInt) fillWidth = backWidthInt;

            _mobilePreLoginUpdateProgressFill.SetSize(fillWidth, _mobilePreLoginUpdateProgressBack.height);
        }

        private static void BuildMobilePreLoginUpdateOverlayUi()
        {
            if (_mobilePreLoginUpdateOverlay == null)
                return;

            _mobilePreLoginUpdateOverlay.RemoveChildren();

            var background = new GGraph
            {
                name = "bg",
                touchable = false,
            };
            _mobilePreLoginUpdateOverlay.AddChild(background);
            background.AddRelation(_mobilePreLoginUpdateOverlay, RelationType.Size);
            background.DrawRect(100, 100, 0, Color.Transparent, new Color(0, 0, 0, 200));

            _mobilePreLoginUpdateTitle = new GTextField
            {
                name = "title",
                touchable = false,
                text = "正在加载资源",
            };
            _mobilePreLoginUpdateOverlay.AddChild(_mobilePreLoginUpdateTitle);
            _mobilePreLoginUpdateTitle.autoSize = AutoSizeType.None;
            _mobilePreLoginUpdateTitle.singleLine = true;

            {
                TextFormat tf = _mobilePreLoginUpdateTitle.textFormat;
                tf.size = 26;
                tf.bold = true;
                tf.color = new Color(255, 255, 255, 255);
                _mobilePreLoginUpdateTitle.textFormat = tf;
            }
            _mobilePreLoginUpdateTitle.align = AlignType.Center;
            _mobilePreLoginUpdateTitle.verticalAlign = VertAlignType.Middle;

            _mobilePreLoginUpdateStatus = new GTextField
            {
                name = "status",
                touchable = false,
                text = string.Empty,
            };
            _mobilePreLoginUpdateOverlay.AddChild(_mobilePreLoginUpdateStatus);
            _mobilePreLoginUpdateStatus.autoSize = AutoSizeType.None;
            _mobilePreLoginUpdateStatus.singleLine = false;

            {
                TextFormat tf = _mobilePreLoginUpdateStatus.textFormat;
                tf.size = 20;
                tf.color = new Color(235, 235, 235, 255);
                _mobilePreLoginUpdateStatus.textFormat = tf;
            }
            _mobilePreLoginUpdateStatus.align = AlignType.Center;
            _mobilePreLoginUpdateStatus.verticalAlign = VertAlignType.Top;

            _mobilePreLoginUpdateProgressBack = new GGraph
            {
                name = "progressBack",
                touchable = false,
            };
            _mobilePreLoginUpdateOverlay.AddChild(_mobilePreLoginUpdateProgressBack);
            _mobilePreLoginUpdateProgressBack.DrawRect(300, 16, 0, Color.Transparent, new Color(255, 255, 255, 40));

            _mobilePreLoginUpdateProgressFill = new GGraph
            {
                name = "progressFill",
                touchable = false,
            };
            _mobilePreLoginUpdateOverlay.AddChild(_mobilePreLoginUpdateProgressFill);
            _mobilePreLoginUpdateProgressFill.DrawRect(10, 16, 0, Color.Transparent, new Color(120, 220, 120, 210));

            _mobilePreLoginUpdateProgressBack.visible = false;
            _mobilePreLoginUpdateProgressFill.visible = false;
            _mobilePreLoginUpdateLastProgress01 = -2F;
        }

        private static void LayoutMobilePreLoginUpdateOverlay(bool force)
        {
            if (_mobilePreLoginUpdateOverlay == null || _mobilePreLoginUpdateTitle == null || _mobilePreLoginUpdateStatus == null)
                return;

            if (_mobilePreLoginUpdateOverlay.displayObject == null || _mobilePreLoginUpdateOverlay.displayObject.isDisposed)
                return;

            int width = (int)Math.Round(_mobilePreLoginUpdateOverlay.width);
            int height = (int)Math.Round(_mobilePreLoginUpdateOverlay.height);
            if (width <= 0 || height <= 0)
            {
                try
                {
                    width = Math.Max(width, (int)Math.Round(_mobileOverlaySafeAreaRoot?.width ?? 0F));
                    height = Math.Max(height, (int)Math.Round(_mobileOverlaySafeAreaRoot?.height ?? 0F));
                }
                catch
                {
                }

                if (width <= 0 || height <= 0)
                {
                    try
                    {
                        width = Math.Max(width, (int)Math.Round(_stage?.width ?? 0F));
                        height = Math.Max(height, (int)Math.Round(_stage?.height ?? 0F));
                    }
                    catch
                    {
                    }
                }
            }
            if (width <= 0 || height <= 0)
                return;

            if (!force && width == _mobilePreLoginUpdateLastLayoutWidth && height == _mobilePreLoginUpdateLastLayoutHeight)
                return;

            _mobilePreLoginUpdateLastLayoutWidth = width;
            _mobilePreLoginUpdateLastLayoutHeight = height;

            const int margin = 24;
            const int titleHeight = 40;
            const int progressHeight = 16;
            const int gapTitleToStatus = 10;
            const int gapStatusToProgress = 16;

            int statusHeight = Math.Max(110, Math.Min(200, height / 3));
            int contentWidth = Math.Min(720, Math.Max(260, width - margin * 2));

            int contentHeight = titleHeight + gapTitleToStatus + statusHeight + gapStatusToProgress + progressHeight;
            int startX = Math.Max(margin, (width - contentWidth) / 2);
            int startY = Math.Max(margin, (height - contentHeight) / 2);

            _mobilePreLoginUpdateTitle.SetPosition(startX, startY);
            _mobilePreLoginUpdateTitle.SetSize(contentWidth, titleHeight);

            _mobilePreLoginUpdateStatus.SetPosition(startX, _mobilePreLoginUpdateTitle.y + titleHeight + gapTitleToStatus);
            _mobilePreLoginUpdateStatus.SetSize(contentWidth, statusHeight);

            if (_mobilePreLoginUpdateProgressBack != null && _mobilePreLoginUpdateProgressFill != null)
            {
                _mobilePreLoginUpdateProgressBack.SetPosition(startX, _mobilePreLoginUpdateStatus.y + statusHeight + gapStatusToProgress);
                _mobilePreLoginUpdateProgressBack.SetSize(contentWidth, progressHeight);

                _mobilePreLoginUpdateProgressFill.SetPosition(startX, _mobilePreLoginUpdateProgressBack.y);
                _mobilePreLoginUpdateProgressFill.SetSize(0, progressHeight);

                // 触发布局后重新应用一次进度宽度
                float current = _mobilePreLoginUpdateLastProgress01;
                _mobilePreLoginUpdateLastProgress01 = -2F;
                SetMobilePreLoginUpdateProgress(current);
            }
        }
    }
}
