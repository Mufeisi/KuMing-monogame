using System;
using System.Collections.Generic;
using FairyGUI;
using MonoShare.MirGraphics;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static readonly string[] DefaultMobileMiniMapImageKeywords = { "小地图", "minimap", "mini_map", "mini-map", "map", "DMinMapUI", "MiniMap", "image", "img" };

        private static GObject _mobileMainHudMiniMapTarget;
        private static string _mobileMainHudMiniMapResolveInfo;
        private static GObject _mobileMainHudMiniMapBackground;
        private static bool _mobileMainHudMiniMapBackgroundInitVisible;
        private static bool _mobileMainHudMiniMapBackgroundCaptured;
        private static DateTime _nextMobileMainHudMiniMapBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileMainHudMiniMapTextureAttemptUtc = DateTime.MinValue;
        private static bool _mobileMainHudMiniMapDirty;
        private static ushort _mobileMainHudMiniMapLastIndex;
        private static ushort _mobileMainHudMiniMapLastLoggedIndex;
        private static int _mobileMainHudMiniMapLastViewHash;
        private static NTexture _mobileMainHudMiniMapViewTexture;
        private static string _mobileMainHudMiniMapDefaultUrl;

        private static void ResetMobileMainHudMiniMapBindings()
        {
            _mobileMainHudMiniMapTarget = null;
            _mobileMainHudMiniMapResolveInfo = null;
            _mobileMainHudMiniMapBackground = null;
            _mobileMainHudMiniMapBackgroundInitVisible = true;
            _mobileMainHudMiniMapBackgroundCaptured = false;
            _nextMobileMainHudMiniMapBindAttemptUtc = DateTime.MinValue;
            _nextMobileMainHudMiniMapTextureAttemptUtc = DateTime.MinValue;
            _mobileMainHudMiniMapDirty = false;
            _mobileMainHudMiniMapLastIndex = 0;
            _mobileMainHudMiniMapLastLoggedIndex = 0;
            _mobileMainHudMiniMapLastViewHash = 0;
            _mobileMainHudMiniMapDefaultUrl = null;
            try
            {
                _mobileMainHudMiniMapViewTexture?.Dispose();
            }
            catch
            {
            }
            _mobileMainHudMiniMapViewTexture = null;
        }

        public static void MarkMobileMainHudMiniMapDirty()
        {
            _mobileMainHudMiniMapDirty = true;
            TryRefreshMobileMainHudMiniMapIfDue(force: false);
        }

        private static bool TrySetMobileMiniMapTexture(GObject target, NTexture texture)
        {
            if (target == null || target._disposed)
                return false;

            try
            {
                if (target is GLoader loader)
                {
                    loader.showErrorSign = false;

                    if (texture != null)
                    {
                        try
                        {
                            // 移动端小地图：根据父容器自动拉伸填满（允许非等比拉伸），避免右侧留黑/露底图。
                            try
                            {
                                // 某些 publish 会把 Loader 设为 autoSize=true，导致即使 fill/Relation 正确也不会真正铺满父容器。
                                loader.autoSize = false;
                            }
                            catch
                            {
                            }

                            loader.fill = FillType.ScaleFree;
                            loader.align = AlignType.Center;
                            loader.verticalAlign = VertAlignType.Middle;
                        }
                        catch
                        {
                        }

                        // 兜底：确保 loader 跟随父容器尺寸（避免出现右侧未填满的黑边）。
                        try
                        {
                            if (loader.parent is GComponent parent && parent != null && !parent._disposed)
                                loader.SetSize(parent.width, parent.height, true);
                        }
                        catch
                        {
                        }

                        // 记录默认底图 url（用于地图没有 minimap 时恢复），并在有 minimap 时清空底图避免覆盖/露边。
                        if (_mobileMainHudMiniMapDefaultUrl == null)
                        {
                            string url = null;
                            try
                            {
                                url = loader.url;
                            }
                            catch
                            {
                                url = null;
                            }

                            if (!string.IsNullOrWhiteSpace(url))
                                _mobileMainHudMiniMapDefaultUrl = url;
                        }

                        loader.url = string.Empty;
                        loader.texture = texture;
                    }
                    else
                    {
                        loader.texture = null;
                        if (!string.IsNullOrWhiteSpace(_mobileMainHudMiniMapDefaultUrl))
                            loader.url = _mobileMainHudMiniMapDefaultUrl;
                    }

                    return true;
                }

                if (target is GImage image)
                {
                    image.texture = texture;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static int ScoreMobileMiniMapImageCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 160, startsWithWeight: 80, containsWeight: 35);
            score += ScoreRect(obj, preferLower: false, areaDivisor: 700, maxAreaScore: 180);

            if (obj is GLoader || obj is GImage)
                score += 10;
            if (obj.packageItem?.exported == true)
                score += 5;

            // 位置偏好：右上角（小地图通常在右上）
            try
            {
                var rect = obj.LocalToGlobal(new System.Drawing.RectangleF(0, 0, obj.width, obj.height));
                float screenW = Math.Max(1, Settings.ScreenWidth);
                float screenH = Math.Max(1, Settings.ScreenHeight);
                bool inTopHalf = rect.Top <= screenH * 0.55F;
                bool inRightHalf = rect.Right >= screenW * 0.45F;
                if (inTopHalf && inRightHalf)
                    score += 25;
            }
            catch
            {
            }

            return score;
        }

        private static void TryBindMobileMainHudMiniMapIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileMainHudMiniMapTarget != null && !_mobileMainHudMiniMapTarget._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileMainHudMiniMapBindAttemptUtc)
                return;

            _nextMobileMainHudMiniMapBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            EnsureMobileMiniMapLocatorInitialized();

            GObject selected = null;
            string resolveInfo = null;

            GObject overrideRoot = _mobileMiniMapRoot;
            string[] keywords = _mobileMiniMapKeywords != null && _mobileMiniMapKeywords.Length > 0 ? _mobileMiniMapKeywords : DefaultMiniMapKeywords;
            if (keywords == null || keywords.Length == 0)
                keywords = DefaultMobileMiniMapImageKeywords;

            // 1) 如果配置覆盖解析到具体对象，优先使用
            if (overrideRoot != null && !overrideRoot._disposed)
            {
                if (overrideRoot is GLoader || overrideRoot is GImage)
                {
                    selected = overrideRoot;
                    resolveInfo = DescribeObject(_mobileMainHud, overrideRoot) + " (override)";
                }
                else if (overrideRoot is GComponent component)
                {
                    List<(int Score, GObject Target)> candidates =
                        CollectMobileChatCandidates(component, obj => obj is GLoader || obj is GImage, keywords, ScoreMobileMiniMapImageCandidate);
                    selected = SelectMobileChatCandidate<GObject>(candidates, minScore: 30);
                    if (selected != null && !selected._disposed)
                        resolveInfo = DescribeObject(_mobileMainHud, selected) + " (override-child)";
                }
            }

            // 2) 默认：DMinMapUI 内找图像位
            if (selected == null || selected._disposed)
            {
                if (TryFindChildByNameRecursive(_mobileMainHud, "DMinMapUI") is GObject miniRoot && miniRoot != null && !miniRoot._disposed)
                {
                    if (miniRoot is GLoader || miniRoot is GImage)
                    {
                        selected = miniRoot;
                        resolveInfo = DescribeObject(_mobileMainHud, miniRoot) + " (DMinMapUI)";
                    }
                    else if (miniRoot is GComponent miniComponent)
                    {
                        // 记录默认底图/遮罩（地图有 minimap 时需要隐藏，避免“右侧被默认图覆盖/截断”）
                        if (_mobileMainHudMiniMapBackground == null || _mobileMainHudMiniMapBackground._disposed)
                        {
                            try
                            {
                                if (TryFindChildByNameRecursive(miniComponent, "MinisMapBg") is GObject bg && bg != null && !bg._disposed)
                                {
                                    _mobileMainHudMiniMapBackground = bg;
                                    if (!_mobileMainHudMiniMapBackgroundCaptured)
                                    {
                                        _mobileMainHudMiniMapBackgroundCaptured = true;
                                        _mobileMainHudMiniMapBackgroundInitVisible = bg.visible;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        // 优先命中：DMinMapUI/DMap（小地图贴图应绘制到 DMap 图片框上）
                        try
                        {
                            if (TryFindChildByNameRecursive(miniComponent, "DMap") is GObject dmap && dmap != null && !dmap._disposed)
                            {
                                if (dmap is GLoader || dmap is GImage)
                                {
                                    selected = dmap;
                                    resolveInfo = DescribeObject(_mobileMainHud, dmap) + " (DMinMapUI/DMap)";
                                }
                                else if (dmap is GComponent dmapComponent)
                                {
                                    // DMap 可能是组件：优先找 item（DMinMapUI/DMap/item 是 Loader）
                                    GObject best = null;
                                    try
                                    {
                                        if (TryFindChildByNameRecursive(dmapComponent, "item") is GObject item && item != null && !item._disposed &&
                                            (item is GLoader || item is GImage))
                                        {
                                            best = item;
                                        }
                                    }
                                    catch
                                    {
                                        best = null;
                                    }

                                    if (best == null || best._disposed)
                                        best = TryFindLargestLoaderOrImage(dmapComponent);
                                    if (best != null && !best._disposed)
                                    {
                                        selected = best;
                                        resolveInfo = DescribeObject(_mobileMainHud, selected) + " (DMinMapUI/DMap-largest)";
                                    }

                                    if (selected == null || selected._disposed)
                                    {
                                        List<(int Score, GObject Target)> dmapCandidates =
                                            CollectMobileChatCandidates(dmapComponent, obj => obj is GLoader || obj is GImage, keywords, ScoreMobileMiniMapImageCandidate);
                                        selected = SelectMobileChatCandidate<GObject>(dmapCandidates, minScore: 10);
                                        if (selected != null && !selected._disposed)
                                            resolveInfo = DescribeObject(_mobileMainHud, selected) + " (DMinMapUI/DMap-child)";
                                    }

                                    // 兜底：DMap 内部可能没有任何可命中的关键字，直接选“面积最大”的 Loader/Image。
                                    if (selected == null || selected._disposed)
                                    {
                                        GObject fallbackBest = TryFindLargestLoaderOrImage(dmapComponent);
                                        if (fallbackBest != null && !fallbackBest._disposed)
                                        {
                                            selected = fallbackBest;
                                            resolveInfo = DescribeObject(_mobileMainHud, selected) + " (DMinMapUI/DMap-largest)";
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }

                        if (selected == null || selected._disposed)
                        {
                            List<(int Score, GObject Target)> candidates =
                                CollectMobileChatCandidates(miniComponent, obj => obj is GLoader || obj is GImage, keywords, ScoreMobileMiniMapImageCandidate);
                            selected = SelectMobileChatCandidate<GObject>(candidates, minScore: 20);
                            if (selected != null && !selected._disposed)
                                resolveInfo = DescribeObject(_mobileMainHud, selected) + " (DMinMapUI-child)";
                        }
                    }
                }
            }

            // 3) 兜底：全局关键字匹配
            if (selected == null || selected._disposed)
            {
                List<(int Score, GObject Target)> candidates =
                    CollectMobileChatCandidates(_mobileMainHud, obj => obj is GLoader || obj is GImage, keywords, ScoreMobileMiniMapImageCandidate);
                selected = SelectMobileChatCandidate<GObject>(candidates, minScore: 45);
                if (selected != null && !selected._disposed)
                    resolveInfo = DescribeObject(_mobileMainHud, selected) + " (auto)";
            }

            if (selected == null || selected._disposed)
            {
                if (Settings.DebugMode)
                {
                    CMain.SaveError("FairyGUI: 主界面未找到小地图容器（MiniMap）。可在 Mir2Config.ini 设置 [" + FairyGuiConfigSectionName + "] " +
                                    MobileMainHudMiniMapConfigKey + "=idx:... 指定小地图节点（或 path:/name:/item:/url:/title）。");
                }
                return;
            }

            _mobileMainHudMiniMapTarget = selected;
            _mobileMainHudMiniMapResolveInfo = resolveInfo;
            _mobileMainHudMiniMapDirty = true;

            try
            {
                _mobileMainHudMiniMapTarget.touchable = true;
            }
            catch
            {
            }

            // 避免贴图大小不匹配导致“右侧被默认图覆盖/截断”：让 Loader 跟随父容器大小
            try
            {
                if (_mobileMainHudMiniMapTarget is GLoader loader && loader.parent is GComponent parent && parent != null && !parent._disposed)
                {
                    loader.AddRelation(parent, RelationType.Size);
                    loader.SetSize(parent.width, parent.height, true);
                }
            }
            catch
            {
            }

            if (Settings.LogErrors)
                CMain.SaveLog("FairyGUI: 主界面小地图绑定完成：" + (_mobileMainHudMiniMapResolveInfo ?? DescribeObject(_mobileMainHud, selected)));
        }

        private static GObject TryFindLargestLoaderOrImage(GComponent root)
        {
            if (root == null || root._disposed)
                return null;

            GObject bestLoaderWithUrl = null;
            float bestLoaderWithUrlArea = 0F;
            GObject bestLoader = null;
            float bestLoaderArea = 0F;
            GObject bestImage = null;
            float bestImageArea = 0F;

            try
            {
                foreach (GObject obj in Enumerate(root))
                {
                    if (obj == null || obj._disposed || ReferenceEquals(obj, root))
                        continue;

                    bool isLoader = obj is GLoader;
                    bool isImage = obj is GImage;
                    if (!isLoader && !isImage)
                        continue;

                    float w = Math.Max(0F, obj.width);
                    float h = Math.Max(0F, obj.height);
                    float area = w * h;

                    if (isLoader)
                    {
                        try
                        {
                            // 优先选择“带默认 url 底图”的 Loader：这样设置 minimap 时能把默认图替换掉，避免覆盖/露边。
                            if (obj is GLoader loader && !string.IsNullOrWhiteSpace(loader.url))
                            {
                                if (area > bestLoaderWithUrlArea)
                                {
                                    bestLoaderWithUrlArea = area;
                                    bestLoaderWithUrl = obj;
                                }
                            }
                        }
                        catch
                        {
                        }

                        if (area > bestLoaderArea)
                        {
                            bestLoaderArea = area;
                            bestLoader = obj;
                        }
                        continue;
                    }

                    if (isImage && area > bestImageArea)
                    {
                        bestImageArea = area;
                        bestImage = obj;
                    }
                }
            }
            catch
            {
                bestLoaderWithUrl = null;
                bestLoaderWithUrlArea = 0F;
                bestLoader = null;
                bestImage = null;
            }

            if (bestLoaderWithUrl != null && !bestLoaderWithUrl._disposed)
                return bestLoaderWithUrl;

            if (bestLoader != null && !bestLoader._disposed)
                return bestLoader;

            return bestImage != null && !bestImage._disposed ? bestImage : null;
        }

        private static void TryRefreshMobileMainHudMiniMapIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileMainHudMiniMapTarget != null)
                    ResetMobileMainHudMiniMapBindings();
                return;
            }

            TryBindMobileMainHudMiniMapIfDue();

            if (_mobileMainHudMiniMapTarget == null || _mobileMainHudMiniMapTarget._disposed)
            {
                ResetMobileMainHudMiniMapBindings();
                return;
            }

            ushort index = 0;
            try
            {
                MapControl control = GameScene.Scene?.MapControl;
                if (control != null)
                    index = ResolvePreferredMiniMapIndex(control);
                else
                    index = 0;
            }
            catch
            {
                index = 0;
            }

            MapControl map = null;
            try
            {
                map = GameScene.Scene?.MapControl;
            }
            catch
            {
                map = null;
            }

            bool hasViewRegion = TryComputeMobileMiniMapViewRegion(index, map, _mobileMainHudMiniMapTarget, out System.Drawing.RectangleF viewRegion, out int viewHash);
            bool viewChanged = hasViewRegion && viewHash != 0 && viewHash != _mobileMainHudMiniMapLastViewHash;

            // 某些 publish gear/controller 会在后续帧把 Loader.url/FillType 还原，导致“默认图覆盖/被截断”。
            bool needsEnforce = false;
            try
            {
                if (index != 0 && _mobileMainHudMiniMapTarget is GLoader loader)
                {
                    needsEnforce |= !string.IsNullOrWhiteSpace(loader.url);
                    needsEnforce |= loader.fill != FillType.ScaleFree;
                }
            }
            catch
            {
                needsEnforce = false;
            }

            // 贴图通常是异步加载：在短时间内反复 GetTexture 可能导致卡顿，这里做一次节流重试。
            if (!force && index == _mobileMainHudMiniMapLastIndex && DateTime.UtcNow < _nextMobileMainHudMiniMapTextureAttemptUtc)
                return;

            if (!force && !viewChanged && !_mobileMainHudMiniMapDirty && index == _mobileMainHudMiniMapLastIndex && !needsEnforce)
                return;

            _mobileMainHudMiniMapDirty = false;
            _mobileMainHudMiniMapLastIndex = index;

            try
            {
                    if (index == 0)
                    {
                        try
                        {
                            _mobileMainHudMiniMapViewTexture?.Dispose();
                    }
                    catch
                    {
                    }
                        _mobileMainHudMiniMapViewTexture = null;
                        _mobileMainHudMiniMapLastViewHash = 0;
                        TrySetMobileMiniMapTexture(_mobileMainHudMiniMapTarget, null);
                        TrySetMobileMiniMapBackgroundVisible(showBackground: true);
                        return;
                    }

                    Libraries.MiniMap.Touch(index);
                    NTexture texture = GetOrCreateMiniMapTexture(index);
                    if (texture != null)
                    {
                        TrySetMobileMiniMapBackgroundVisible(showBackground: false);
                        _nextMobileMainHudMiniMapTextureAttemptUtc = DateTime.MinValue;
                        NTexture finalTexture = texture;

                    if (hasViewRegion && viewRegion.Width > 1F && viewRegion.Height > 1F)
                    {
                        bool viewTextureInvalid = false;
                        try
                        {
                            var native = _mobileMainHudMiniMapViewTexture?.nativeTexture;
                            viewTextureInvalid = native == null || native.IsDisposed;
                        }
                        catch
                        {
                            viewTextureInvalid = true;
                        }

                        if (viewChanged || _mobileMainHudMiniMapViewTexture == null || viewTextureInvalid)
                        {
                            try
                            {
                                _mobileMainHudMiniMapViewTexture?.Dispose();
                            }
                            catch
                            {
                            }

                            try
                            {
                                _mobileMainHudMiniMapViewTexture = new NTexture(texture, viewRegion, rotated: false);
                            }
                            catch
                            {
                                _mobileMainHudMiniMapViewTexture = null;
                            }
                        }

                        if (_mobileMainHudMiniMapViewTexture != null)
                            finalTexture = _mobileMainHudMiniMapViewTexture;
                    }
                    else
                    {
                        try
                        {
                            _mobileMainHudMiniMapViewTexture?.Dispose();
                        }
                        catch
                        {
                        }
                        _mobileMainHudMiniMapViewTexture = null;
                        viewHash = 0;
                    }

                    _mobileMainHudMiniMapLastViewHash = viewHash;
                    TrySetMobileMiniMapTexture(_mobileMainHudMiniMapTarget, finalTexture);

                    if (Settings.LogErrors && index != 0 && _mobileMainHudMiniMapLastLoggedIndex != index)
                    {
                        _mobileMainHudMiniMapLastLoggedIndex = index;
                        try
                        {
                            string viewInfo = hasViewRegion
                                ? $"({viewRegion.X:0},{viewRegion.Y:0},{viewRegion.Width:0},{viewRegion.Height:0})"
                                : "full";
                            string sizeInfo = string.Empty;
                            try
                            {
                                if (_mobileMainHudMiniMapTarget is GLoader l && l != null && !l._disposed)
                                {
                                    float pw = 0F, ph = 0F;
                                    try
                                    {
                                        pw = l.parent != null && !l.parent._disposed ? l.parent.width : 0F;
                                        ph = l.parent != null && !l.parent._disposed ? l.parent.height : 0F;
                                    }
                                    catch
                                    {
                                        pw = 0F;
                                        ph = 0F;
                                    }

                                    sizeInfo = $" Size=({l.width:0.##},{l.height:0.##}) Parent=({pw:0.##},{ph:0.##}) Fill={l.fill} AutoSize={l.autoSize}";
                                }
                            }
                            catch
                            {
                                sizeInfo = string.Empty;
                            }

                            CMain.SaveLog($"FairyGUI: 小地图贴图已更新：Index={index} View={viewInfo} Target={_mobileMainHudMiniMapResolveInfo ?? DescribeObject(_mobileMainHud, _mobileMainHudMiniMapTarget)}{sizeInfo}");
                        }
                        catch
                        {
                        }
                    }
                    }
                    else
                    {
                        TrySetMobileMiniMapBackgroundVisible(showBackground: true);
                        // 还没就绪：保持 dirty，稍后重试（避免第一次进地图时小地图一直空白）。
                        _mobileMainHudMiniMapDirty = true;
                        _mobileMainHudMiniMapDirty = true;
                        try
                        {
                            _mobileMainHudMiniMapViewTexture?.Dispose();
                        }
                        catch
                        {
                        }
                        _mobileMainHudMiniMapViewTexture = null;
                        _mobileMainHudMiniMapLastViewHash = 0;
                        TrySetMobileMiniMapTexture(_mobileMainHudMiniMapTarget, null);
                        _nextMobileMainHudMiniMapTextureAttemptUtc = DateTime.UtcNow.AddMilliseconds(450);
                    }
                }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: 刷新主界面小地图失败：" + ex.Message);
                _nextMobileMainHudMiniMapBindAttemptUtc = DateTime.MinValue;
                _mobileMainHudMiniMapDirty = true;
            }
        }

        private static bool TryComputeMobileMiniMapViewRegion(ushort mapIndex, MapControl map, GObject target, out System.Drawing.RectangleF viewRegion, out int viewHash)
        {
            viewRegion = default;
            viewHash = 0;

            if (mapIndex == 0 || map == null || target == null || target._disposed)
                return false;

            try
            {
                var user = MapControl.User;
                if (user == null)
                    return false;

                int mapWidth = map.Width;
                int mapHeight = map.Height;
                if (mapWidth <= 0 || mapHeight <= 0)
                    return false;

                System.Drawing.Size miniMapSize = Libraries.MiniMap.GetSize(mapIndex);
                if (miniMapSize.IsEmpty || miniMapSize.Width <= 0 || miniMapSize.Height <= 0)
                    return false;

                float scaleX = miniMapSize.Width / (float)mapWidth;
                float scaleY = miniMapSize.Height / (float)mapHeight;

                int viewW = (int)Math.Round(Math.Max(32F, target.width));
                int viewH = (int)Math.Round(Math.Max(32F, target.height));
                viewW = Math.Clamp(viewW, 32, miniMapSize.Width);
                viewH = Math.Clamp(viewH, 32, miniMapSize.Height);

                System.Drawing.Point loc = user.CurrentLocation;

                int x = (int)Math.Round(scaleX * loc.X) - viewW / 2;
                int y = (int)Math.Round(scaleY * loc.Y) - viewH / 2;

                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x + viewW > miniMapSize.Width) x = miniMapSize.Width - viewW;
                if (y + viewH > miniMapSize.Height) y = miniMapSize.Height - viewH;

                viewRegion = new System.Drawing.RectangleF(x, y, viewW, viewH);

                int hash = mapIndex;
                hash = (hash * 397) ^ x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ viewW;
                hash = (hash * 397) ^ viewH;

                viewHash = hash;
                return true;
            }
            catch
            {
                viewRegion = default;
                viewHash = 0;
                return false;
            }
        }

        private static void TrySetMobileMiniMapBackgroundVisible(bool showBackground)
        {
            if (_mobileMainHudMiniMapBackground == null || _mobileMainHudMiniMapBackground._disposed)
                return;

            try
            {
                _mobileMainHudMiniMapBackground.visible = showBackground ? _mobileMainHudMiniMapBackgroundInitVisible : false;
                _mobileMainHudMiniMapBackground.touchable = false;
            }
            catch
            {
            }
        }

        /// <summary>
        /// 有些服端/资源配置可能会把 MiniMap/BigMap 的索引对调或返回其中一个为 0。
        /// 移动端 HUD 的小地图优先取“尺寸更小”的那张图（通常为 minimap）。
        /// </summary>
        private static ushort ResolvePreferredMiniMapIndex(MapControl map)
        {
            if (map == null)
                return 0;

            ushort mini = 0;
            ushort big = 0;
            try
            {
                mini = map.ActiveMiniMap;
                big = map.ActiveBigMap;
            }
            catch
            {
                mini = 0;
                big = 0;
            }

            if (mini == 0)
                return big;
            if (big == 0 || big == mini)
                return mini;

            try
            {
                System.Drawing.Size miniSize = Libraries.MiniMap.GetSize(mini);
                System.Drawing.Size bigSize = Libraries.MiniMap.GetSize(big);

                if (miniSize.IsEmpty || miniSize.Width <= 0 || miniSize.Height <= 0)
                    return big;
                if (bigSize.IsEmpty || bigSize.Width <= 0 || bigSize.Height <= 0)
                    return mini;

                long miniArea = (long)miniSize.Width * miniSize.Height;
                long bigArea = (long)bigSize.Width * bigSize.Height;

                return miniArea <= bigArea ? mini : big;
            }
            catch
            {
                return mini;
            }
        }
    }
}
