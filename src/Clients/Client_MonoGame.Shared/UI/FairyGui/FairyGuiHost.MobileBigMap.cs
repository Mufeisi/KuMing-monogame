using System;
using System.Collections.Generic;
using FairyGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.MirGraphics;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileBigMapImageConfigKey = "MobileBigMap.Image";

        private static readonly string[] DefaultBigMapImageKeywords = { "bigmap", "big_map", "dbigmap", "map", "地图", "大地图", "image", "img" };

        private static GLoader _mobileBigMapImage;
        private static string _mobileBigMapImageResolveInfo;
        private static EventCallback0 _mobileBigMapClickCallback;
        private static DateTime _nextMobileBigMapBindAttemptUtc = DateTime.MinValue;
        private static bool _mobileBigMapDirty;
        private static ushort _mobileBigMapLastIndex;

        private static readonly Dictionary<int, NTexture> BigMapTextureCache = new Dictionary<int, NTexture>(64);

        private static GComponent _mobileBigMapOverlay;
        private static GGraph _mobileBigMapPlayerMarker;
        private static GGraph _mobileBigMapDestinationMarker;
        private static readonly List<GGraph> _mobileBigMapPathDots = new List<GGraph>(256);
        private static int _mobileBigMapPathLastHash;
        private static DateTime _nextMobileBigMapOverlayRefreshUtc = DateTime.MinValue;

        public static void MarkMobileBigMapDirty()
        {
            _mobileBigMapDirty = true;
            TryRefreshMobileBigMapIfDue(force: false);
        }

        private static void ResetMobileBigMapBindings()
        {
            try
            {
                if (_mobileBigMapImage != null && !_mobileBigMapImage._disposed && _mobileBigMapClickCallback != null)
                    _mobileBigMapImage.onClick.Remove(_mobileBigMapClickCallback);
            }
            catch
            {
            }

            try
            {
                if (_mobileBigMapOverlay != null && !_mobileBigMapOverlay._disposed)
                    _mobileBigMapOverlay.Dispose();
            }
            catch
            {
            }

            _mobileBigMapImage = null;
            _mobileBigMapImageResolveInfo = null;
            _mobileBigMapClickCallback = null;
            _nextMobileBigMapBindAttemptUtc = DateTime.MinValue;
            _mobileBigMapDirty = false;
            _mobileBigMapLastIndex = 0;

            _mobileBigMapOverlay = null;
            _mobileBigMapPlayerMarker = null;
            _mobileBigMapDestinationMarker = null;
            _mobileBigMapPathDots.Clear();
            _mobileBigMapPathLastHash = 0;
            _nextMobileBigMapOverlayRefreshUtc = DateTime.MinValue;
        }

        private static void TryEnsureMobileBigMapOverlay(GComponent window, GLoader mapImage)
        {
            if (window == null || window._disposed)
                return;

            if (mapImage == null || mapImage._disposed)
                return;

            try
            {
                if (_mobileBigMapOverlay == null || _mobileBigMapOverlay._disposed || _mobileBigMapOverlay.parent != window)
                {
                    _mobileBigMapOverlay?.Dispose();
                    _mobileBigMapOverlay = new GComponent
                    {
                        name = "__codex_mobile_bigmap_overlay",
                        opaque = false,
                        touchable = false,
                    };
                    window.AddChild(_mobileBigMapOverlay);
                }
            }
            catch
            {
                return;
            }

            try
            {
                _mobileBigMapOverlay.touchable = false;
                _mobileBigMapOverlay.opaque = false;
            }
            catch
            {
            }

            TryUpdateMobileBigMapOverlayLayout(window, mapImage);

            if (_mobileBigMapPlayerMarker == null || _mobileBigMapPlayerMarker._disposed)
            {
                try
                {
                    _mobileBigMapPlayerMarker = new GGraph { name = "__codex_mobile_bigmap_player", touchable = false };
                    _mobileBigMapPlayerMarker.DrawEllipse(14, 14, new Color(40, 220, 90, 220));
                    _mobileBigMapOverlay.AddChild(_mobileBigMapPlayerMarker);
                }
                catch
                {
                    _mobileBigMapPlayerMarker = null;
                }
            }

            if (_mobileBigMapDestinationMarker == null || _mobileBigMapDestinationMarker._disposed)
            {
                try
                {
                    _mobileBigMapDestinationMarker = new GGraph { name = "__codex_mobile_bigmap_dest", touchable = false };
                    _mobileBigMapDestinationMarker.DrawEllipse(16, 16, new Color(235, 80, 60, 220));
                    _mobileBigMapOverlay.AddChild(_mobileBigMapDestinationMarker);
                }
                catch
                {
                    _mobileBigMapDestinationMarker = null;
                }
            }

            try
            {
                // 确保 Overlay 在地图图像之上（避免被遮挡）。
                window.SetChildIndex(_mobileBigMapOverlay, window.numChildren - 1);
            }
            catch
            {
            }
        }

        private static void TryUpdateMobileBigMapOverlayLayout(GComponent window, GLoader mapImage)
        {
            if (_mobileBigMapOverlay == null || _mobileBigMapOverlay._disposed)
                return;

            if (window == null || window._disposed || mapImage == null || mapImage._disposed)
                return;

            try
            {
                // Overlay 与“实际贴图内容区域”保持同位置/同尺寸（避免 fill/align 缩放导致坐标偏移）。
                // 注意：mapImage 不一定是 window 的直接子节点，必须换算到 window 坐标系。
                if (!TryGetLoaderContentRectInComponent(window, mapImage, out float x, out float y, out float w, out float h))
                {
                    _mobileBigMapOverlay.SetPosition(mapImage.x, mapImage.y);
                    _mobileBigMapOverlay.SetSize(mapImage.width, mapImage.height);
                    return;
                }

                _mobileBigMapOverlay.SetPosition(x, y);
                _mobileBigMapOverlay.SetSize(w, h);
            }
            catch
            {
            }
        }

        private static bool TryGetLoaderContentRectInComponent(GComponent component, GLoader loader, out float x, out float y, out float w, out float h)
        {
            x = 0F;
            y = 0F;
            w = 0F;
            h = 0F;

            if (component == null || component._disposed)
                return false;

            if (loader == null || loader._disposed)
                return false;

            try
            {
                Image img = loader.image;
                if (img == null)
                    return false;

                float contentW = Math.Max(1F, img.width);
                float contentH = Math.Max(1F, img.height);
                if (contentW <= 1F || contentH <= 1F)
                    return false;

                System.Drawing.RectangleF globalRect = loader.LocalToGlobal(new System.Drawing.RectangleF(img.x, img.y, contentW, contentH));
                System.Drawing.RectangleF localRect = component.GlobalToLocal(globalRect);

                x = localRect.X;
                y = localRect.Y;
                w = localRect.Width;
                h = localRect.Height;

                if (w < 0F)
                {
                    x += w;
                    w = -w;
                }

                if (h < 0F)
                {
                    y += h;
                    h = -h;
                }

                w = Math.Max(1F, w);
                h = Math.Max(1F, h);
                return w > 1F && h > 1F;
            }
            catch
            {
                x = 0F;
                y = 0F;
                w = 0F;
                h = 0F;
                return false;
            }
        }

        private static void TryRefreshMobileBigMapOverlayIfDue(GComponent window, GLoader mapImage, bool force)
        {
            if (_mobileBigMapOverlay == null || _mobileBigMapOverlay._disposed)
                return;

            if (!force && DateTime.UtcNow < _nextMobileBigMapOverlayRefreshUtc)
                return;

            _nextMobileBigMapOverlayRefreshUtc = DateTime.UtcNow.AddMilliseconds(120);

            MapControl map = GameScene.Scene?.MapControl;
            if (map == null)
                return;

            int mapWidth = map.Width;
            int mapHeight = map.Height;
            if (mapWidth <= 1 || mapHeight <= 1)
                return;

            TryUpdateMobileBigMapOverlayLayout(window, mapImage);

            float w = Math.Max(1F, _mobileBigMapOverlay.width);
            float h = Math.Max(1F, _mobileBigMapOverlay.height);

            // 当前位置
            try
            {
                var user = MapControl.User;
                System.Drawing.Point loc = user != null ? user.CurrentLocation : System.Drawing.Point.Empty;
                float px = (loc.X / (float)(mapWidth - 1)) * w;
                float py = (loc.Y / (float)(mapHeight - 1)) * h;
                if (_mobileBigMapPlayerMarker != null && !_mobileBigMapPlayerMarker._disposed)
                {
                    _mobileBigMapPlayerMarker.visible = true;
                    _mobileBigMapPlayerMarker.SetPosition(px - _mobileBigMapPlayerMarker.width / 2F, py - _mobileBigMapPlayerMarker.height / 2F);
                }
            }
            catch
            {
            }

            // 目的地 + 路线（来自 MapControl 的移动端寻路路径）
            System.Drawing.Point? destination = null;
            IReadOnlyList<System.Drawing.Point> steps = null;
            int stepIndex = 0;
            try
            {
                destination = map.MobileTapPathDestination;
                steps = map.MobileTapPathSteps;
                stepIndex = map.MobileTapPathStepIndex;
            }
            catch
            {
                destination = null;
                steps = null;
                stepIndex = 0;
            }

            // 目的地标记
            try
            {
                if (_mobileBigMapDestinationMarker != null && !_mobileBigMapDestinationMarker._disposed)
                {
                    if (destination.HasValue)
                    {
                        System.Drawing.Point dest = destination.Value;
                        float dx = (dest.X / (float)(mapWidth - 1)) * w;
                        float dy = (dest.Y / (float)(mapHeight - 1)) * h;
                        _mobileBigMapDestinationMarker.visible = true;
                        _mobileBigMapDestinationMarker.SetPosition(dx - _mobileBigMapDestinationMarker.width / 2F, dy - _mobileBigMapDestinationMarker.height / 2F);
                    }
                    else
                    {
                        _mobileBigMapDestinationMarker.visible = false;
                    }
                }
            }
            catch
            {
            }

            int pathHash = 0;
            try
            {
                if (destination.HasValue)
                {
                    System.Drawing.Point dest = destination.Value;
                    pathHash = (dest.X * 397) ^ dest.Y;
                }

                if (steps != null)
                    pathHash = (pathHash * 397) ^ steps.Count;

                pathHash = (pathHash * 397) ^ stepIndex;
            }
            catch
            {
                pathHash = 0;
            }

            if (!force && pathHash == _mobileBigMapPathLastHash)
                return;

            _mobileBigMapPathLastHash = pathHash;

            // 路线点（采样显示，避免一次生成太多对象）
            int dotNeeded = 0;
            if (steps != null && stepIndex >= 0 && stepIndex < steps.Count)
            {
                int remaining = steps.Count - stepIndex;
                int maxDots = 220;
                int stride = Math.Max(1, remaining / maxDots);
                dotNeeded = (remaining + stride - 1) / stride;

                int dotIndex = 0;
                for (int i = stepIndex; i < steps.Count; i += stride)
                {
                    System.Drawing.Point p = steps[i];
                    float sx = (p.X / (float)(mapWidth - 1)) * w;
                    float sy = (p.Y / (float)(mapHeight - 1)) * h;

                    GGraph dot = null;
                    if (dotIndex < _mobileBigMapPathDots.Count)
                    {
                        dot = _mobileBigMapPathDots[dotIndex];
                    }
                    else
                    {
                        try
                        {
                            dot = new GGraph { name = "__codex_mobile_bigmap_dot_" + dotIndex, touchable = false };
                            dot.DrawEllipse(6, 6, new Color(80, 170, 255, 190));
                            _mobileBigMapOverlay.AddChild(dot);
                            _mobileBigMapPathDots.Add(dot);
                        }
                        catch
                        {
                            dot = null;
                        }
                    }

                    if (dot != null && !dot._disposed)
                    {
                        dot.visible = true;
                        dot.SetPosition(sx - dot.width / 2F, sy - dot.height / 2F);
                    }

                    dotIndex++;
                    if (dotIndex >= dotNeeded)
                        break;
                }
            }

            // 隐藏多余点
            for (int i = dotNeeded; i < _mobileBigMapPathDots.Count; i++)
            {
                try
                {
                    GGraph dot = _mobileBigMapPathDots[i];
                    if (dot != null && !dot._disposed)
                        dot.visible = false;
                }
                catch
                {
                }
            }
        }

        private static void TryHandleMobileBigMapClick()
        {
            if (_mobileBigMapImage == null || _mobileBigMapImage._disposed)
                return;

            try
            {
                MapControl map = GameScene.Scene?.MapControl;
                if (map == null)
                    return;

                int mapWidth = map.Width;
                int mapHeight = map.Height;
                if (mapWidth <= 0 || mapHeight <= 0)
                    return;

                var global = Stage.inst.touchPosition;
                var local = _mobileBigMapImage.GlobalToLocal(global);

                // 注意：GLoader 可能 fill/align 导致贴图内容区域在容器内有偏移/缩放，必须按 contentRect 换算。
                float contentX = 0F;
                float contentY = 0F;
                float contentW = 0F;
                float contentH = 0F;
                try
                {
                    Image img = _mobileBigMapImage.image;
                    if (img != null)
                    {
                        contentX = img.x;
                        contentY = img.y;
                        contentW = Math.Max(1F, img.width);
                        contentH = Math.Max(1F, img.height);
                    }
                }
                catch
                {
                    contentX = 0F;
                    contentY = 0F;
                    contentW = 0F;
                    contentH = 0F;
                }

                if (contentW <= 1F || contentH <= 1F)
                    return;

                // 点击落在内容区域之外（边框/留白）则忽略
                if (local.X < contentX || local.X > contentX + contentW || local.Y < contentY || local.Y > contentY + contentH)
                    return;

                float u = (local.X - contentX) / contentW;
                float v = (local.Y - contentY) / contentH;

                if (float.IsNaN(u) || float.IsNaN(v) || float.IsInfinity(u) || float.IsInfinity(v))
                    return;

                u = Math.Clamp(u, 0F, 1F);
                v = Math.Clamp(v, 0F, 1F);

                int x = (int)Math.Round(u * (mapWidth - 1));
                int y = (int)Math.Round(v * (mapHeight - 1));

                x = Math.Clamp(x, 0, Math.Max(0, mapWidth - 1));
                y = Math.Clamp(y, 0, Math.Max(0, mapHeight - 1));

                // 移动端：主场景“点地面移动”已禁用（避免与双摇杆走跑冲突），但允许通过【大地图】选择目的地进行自动寻路。
                map.SetMobileTapMoveDestination(new System.Drawing.Point(x, y));
            }
            catch
            {
            }
        }

        private static int ScoreMobileBigMapImageCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 600, maxAreaScore: 200);
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static void TryBindMobileBigMapWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            if (_mobileBigMapImage != null && !_mobileBigMapImage._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileBigMapBindAttemptUtc)
                return;

            _nextMobileBigMapBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            string overrideSpec = string.Empty;
            try
            {
                InIReader reader = TryCreateConfigReader();
                if (reader != null)
                    overrideSpec = reader.ReadString(FairyGuiConfigSectionName, MobileBigMapImageConfigKey, string.Empty, writeWhenNull: false);
            }
            catch
            {
                overrideSpec = string.Empty;
            }

            overrideSpec = overrideSpec?.Trim() ?? string.Empty;

            GLoader resolvedLoader = null;
            string[] overrideKeywords = null;

            if (!string.IsNullOrWhiteSpace(overrideSpec))
            {
                if (TryResolveMobileMainHudObjectBySpec(window, overrideSpec, out GObject resolved, out string[] keywords))
                {
                    if (resolved is GLoader loader && !loader._disposed)
                    {
                        resolvedLoader = loader;
                        _mobileBigMapImageResolveInfo = DescribeObject(window, loader) + " (override)";
                    }
                    else if (keywords != null && keywords.Length > 0)
                    {
                        overrideKeywords = keywords;
                    }
                }
                else
                {
                    overrideKeywords = SplitKeywords(overrideSpec);
                }
            }

            if (resolvedLoader == null || resolvedLoader._disposed)
            {
                string[] keywordsUsed = overrideKeywords != null && overrideKeywords.Length > 0 ? overrideKeywords : DefaultBigMapImageKeywords;
                int minScore = overrideKeywords != null && overrideKeywords.Length > 0 ? 20 : 35;

                List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(window, obj => obj is GLoader, keywordsUsed, ScoreMobileBigMapImageCandidate);
                resolvedLoader = SelectMobileChatCandidate<GLoader>(candidates, minScore);
                if (resolvedLoader != null && !resolvedLoader._disposed)
                    _mobileBigMapImageResolveInfo = DescribeObject(window, resolvedLoader) + (overrideKeywords != null && overrideKeywords.Length > 0 ? " (keywords)" : " (auto)");
            }

            // 最终兜底：取窗口内面积最大的 Loader
            if (resolvedLoader == null || resolvedLoader._disposed)
            {
                GLoader best = null;
                float bestArea = 0F;

                foreach (GObject obj in Enumerate(window))
                {
                    if (obj == null || obj._disposed)
                        continue;

                    if (obj is not GLoader loader || loader._disposed)
                        continue;

                    float area = Math.Max(0F, loader.width) * Math.Max(0F, loader.height);
                    if (area <= bestArea)
                        continue;

                    bestArea = area;
                    best = loader;
                }

                resolvedLoader = best;
                if (resolvedLoader != null && !resolvedLoader._disposed)
                    _mobileBigMapImageResolveInfo = DescribeObject(window, resolvedLoader) + " (largest)";
            }

            if (resolvedLoader == null || resolvedLoader._disposed)
            {
                CMain.SaveError("FairyGUI: 大地图窗口未找到地图图片容器（BigMap）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                MobileBigMapImageConfigKey + "=idx:... 指定地图图片的 GLoader。");
                return;
            }

            _mobileBigMapImage = resolvedLoader;
            _mobileBigMapDirty = true;

            try
            {
                _mobileBigMapImage.showErrorSign = false;
                _mobileBigMapImage.url = string.Empty;
                _mobileBigMapImage.touchable = true;

                if (_mobileBigMapClickCallback == null)
                    _mobileBigMapClickCallback = TryHandleMobileBigMapClick;

                try
                {
                    _mobileBigMapImage.onClick.Remove(_mobileBigMapClickCallback);
                }
                catch
                {
                }

                _mobileBigMapImage.onClick.Add(_mobileBigMapClickCallback);
            }
            catch
            {
            }

            if (Settings.LogErrors)
                CMain.SaveLog("FairyGUI: 大地图窗口绑定完成：Image=" + (_mobileBigMapImageResolveInfo ?? "(null)"));
        }

        private static NTexture GetOrCreateMiniMapTexture(ushort mapIndex)
        {
            if (mapIndex == 0)
                return null;

            int key = mapIndex;
            if (BigMapTextureCache.TryGetValue(key, out NTexture cached) && cached != null)
            {
                Texture2D native = cached.nativeTexture;
                if (native != null && !native.IsDisposed)
                    return cached;
            }

            Texture2D texture = null;
            try
            {
                texture = Libraries.MiniMap.GetTexture(mapIndex);
            }
            catch
            {
                texture = null;
            }

            if (texture == null || texture.IsDisposed)
                return null;

            NTexture created = new NTexture(texture);
            BigMapTextureCache[key] = created;
            return created;
        }

        private static void TryRefreshMobileBigMapIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("BigMap", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileBigMapImage != null)
                    ResetMobileBigMapBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileBigMapWindowIfDue("BigMap", window, resolveInfo: null);

            if (_mobileBigMapImage == null || _mobileBigMapImage._disposed)
            {
                ResetMobileBigMapBindings();
                return;
            }

            // 路线/定位 Overlay 刷新（与贴图刷新解耦：即使贴图不变，也要更新当前位置/路线）。
            TryEnsureMobileBigMapOverlay(window, _mobileBigMapImage);
            TryRefreshMobileBigMapOverlayIfDue(window, _mobileBigMapImage, force: false);

            ushort index = 0;
            try
            {
                MapControl control = GameScene.Scene?.MapControl;
                if (control != null)
                    index = ResolvePreferredBigMapIndex(control);
                else
                    index = 0;
            }
            catch
            {
                index = 0;
            }

            if (!force && !_mobileBigMapDirty && index == _mobileBigMapLastIndex)
                return;

            _mobileBigMapDirty = false;
            _mobileBigMapLastIndex = index;

            try
            {
                if (index == 0)
                {
                    _mobileBigMapImage.texture = null;
                    return;
                }

                Libraries.MiniMap.Touch(index);
                NTexture texture = GetOrCreateMiniMapTexture(index);
                if (texture != null)
                {
                    _mobileBigMapImage.texture = texture;
                }
                else
                {
                    // 贴图可能是异步加载：本次拿不到时保持 dirty，稍后重试。
                    _mobileBigMapDirty = true;
                }
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新大地图窗口失败：" + ex.Message);
                _nextMobileBigMapBindAttemptUtc = DateTime.MinValue;
                _mobileBigMapDirty = true;
            }
        }

        /// <summary>
        /// 移动端大地图优先取“尺寸更大”的那张图（通常为 map / bigmap）。
        /// 兼容某些服端配置 MiniMap/BigMap 索引对调或其中一个为 0 的情况。
        /// </summary>
        private static ushort ResolvePreferredBigMapIndex(MapControl map)
        {
            if (map == null)
                return 0;

            ushort mini = 0;
            ushort big = 0;
            try
            {
                mini = map.MiniMap;
                big = map.BigMap;
            }
            catch
            {
                mini = 0;
                big = 0;
            }

            if (big == 0)
                return mini;
            if (mini == 0 || mini == big)
                return big;

            try
            {
                System.Drawing.Size miniSize = Libraries.MiniMap.GetSize(mini);
                System.Drawing.Size bigSize = Libraries.MiniMap.GetSize(big);

                if (bigSize.IsEmpty || bigSize.Width <= 0 || bigSize.Height <= 0)
                    return mini;
                if (miniSize.IsEmpty || miniSize.Width <= 0 || miniSize.Height <= 0)
                    return big;

                long miniArea = (long)miniSize.Width * miniSize.Height;
                long bigArea = (long)bigSize.Width * bigSize.Height;

                return bigArea >= miniArea ? big : mini;
            }
            catch
            {
                return big;
            }
        }
    }
}
