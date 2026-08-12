using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using FairyGUI;
using XnaColor = Microsoft.Xna.Framework.Color;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileNearbyListTextName = "__codex_mobile_nearby_list_text";

        private enum MobileNearbyListMode
        {
            Players = 0,
            Monsters = 1,
        }

        private static GComponent _mobileNearbyRoot;
        private static GObject _mobileNearbyBtnHums;
        private static GObject _mobileNearbyBtnMob;
        private static GObject _mobileNearbyBtnRefresh;
        private static GComponent _mobileNearbyPage1;
        private static GRichTextField _mobileNearbyListText;

        private static EventCallback0 _mobileNearbyBtnHumsClick;
        private static EventCallback0 _mobileNearbyBtnMobClick;
        private static EventCallback0 _mobileNearbyBtnRefreshClick;

        private static DateTime _nextMobileNearbyBindAttemptUtc = DateTime.MinValue;
        private static DateTime _nextMobileNearbyRefreshUtc = DateTime.MinValue;
        private static bool _mobileNearbyDirty = true;
        private static MobileNearbyListMode _mobileNearbyMode = MobileNearbyListMode.Players;
        private const int MobileNearbyDesiredFontSize = 20; // 与移动端聊天框字号保持一致（见 FairyGuiHost.ApplyMobileChatFontSizes）

        private static void ResetMobileNearbyPanelBindings()
        {
            try
            {
                if (_mobileNearbyBtnHums != null && !_mobileNearbyBtnHums._disposed && _mobileNearbyBtnHumsClick != null)
                    _mobileNearbyBtnHums.onClick.Remove(_mobileNearbyBtnHumsClick);
            }
            catch
            {
            }

            try
            {
                if (_mobileNearbyBtnMob != null && !_mobileNearbyBtnMob._disposed && _mobileNearbyBtnMobClick != null)
                    _mobileNearbyBtnMob.onClick.Remove(_mobileNearbyBtnMobClick);
            }
            catch
            {
            }

            try
            {
                if (_mobileNearbyBtnRefresh != null && !_mobileNearbyBtnRefresh._disposed && _mobileNearbyBtnRefreshClick != null)
                    _mobileNearbyBtnRefresh.onClick.Remove(_mobileNearbyBtnRefreshClick);
            }
            catch
            {
            }

            _mobileNearbyRoot = null;
            _mobileNearbyBtnHums = null;
            _mobileNearbyBtnMob = null;
            _mobileNearbyBtnRefresh = null;
            _mobileNearbyPage1 = null;
            _mobileNearbyListText = null;

            _nextMobileNearbyBindAttemptUtc = DateTime.MinValue;
            _nextMobileNearbyRefreshUtc = DateTime.MinValue;
            _mobileNearbyDirty = true;
        }

        private static void MarkMobileNearbyDirty()
        {
            _mobileNearbyDirty = true;
            _nextMobileNearbyRefreshUtc = DateTime.MinValue;
        }

        private static void TryBindMobileNearbyPanelIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (_mobileNearbyRoot != null && !_mobileNearbyRoot._disposed &&
                _mobileNearbyPage1 != null && !_mobileNearbyPage1._disposed &&
                _mobileNearbyListText != null && !_mobileNearbyListText._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileNearbyBindAttemptUtc)
                return;

            _nextMobileNearbyBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            GComponent root = null;
            try { root = TryFindChildByNameRecursive(_mobileMainHud, "TaskAndRanksPanel") as GComponent; } catch { root = null; }
            if (root == null || root._disposed)
                return;

            GComponent page1 = null;
            try { page1 = TryFindChildByNameRecursive(root, "DA2EPage1") as GComponent; } catch { page1 = null; }
            if (page1 == null || page1._disposed)
                return;

            _mobileNearbyRoot = root;
            _mobileNearbyPage1 = page1;

            try { _mobileNearbyBtnHums = TryFindChildByNameRecursive(root, "DTabBtnHums"); } catch { _mobileNearbyBtnHums = null; }
            try { _mobileNearbyBtnMob = TryFindChildByNameRecursive(root, "DTabBtnMob"); } catch { _mobileNearbyBtnMob = null; }
            try { _mobileNearbyBtnRefresh = TryFindChildByNameRecursive(root, "RefreshBtn"); } catch { _mobileNearbyBtnRefresh = null; }

            _mobileNearbyBtnHumsClick ??= () =>
            {
                _mobileNearbyMode = MobileNearbyListMode.Players;
                MarkMobileNearbyDirty();
                TryRefreshMobileMainHudNearbyPanelIfDue(force: true);
            };

            _mobileNearbyBtnMobClick ??= () =>
            {
                _mobileNearbyMode = MobileNearbyListMode.Monsters;
                MarkMobileNearbyDirty();
                TryRefreshMobileMainHudNearbyPanelIfDue(force: true);
            };

            _mobileNearbyBtnRefreshClick ??= () =>
            {
                MarkMobileNearbyDirty();
                TryRefreshMobileMainHudNearbyPanelIfDue(force: true);
            };

            try
            {
                if (_mobileNearbyBtnHums != null && !_mobileNearbyBtnHums._disposed)
                {
                    _mobileNearbyBtnHums.touchable = true;
                    if (_mobileNearbyBtnHums is GButton b)
                    {
                        b.enabled = true;
                        b.grayed = false;
                        b.changeStateOnClick = false;
                    }
                    _mobileNearbyBtnHums.onClick.Remove(_mobileNearbyBtnHumsClick);
                    _mobileNearbyBtnHums.onClick.Add(_mobileNearbyBtnHumsClick);
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileNearbyBtnMob != null && !_mobileNearbyBtnMob._disposed)
                {
                    _mobileNearbyBtnMob.touchable = true;
                    if (_mobileNearbyBtnMob is GButton b)
                    {
                        b.enabled = true;
                        b.grayed = false;
                        b.changeStateOnClick = false;
                    }
                    _mobileNearbyBtnMob.onClick.Remove(_mobileNearbyBtnMobClick);
                    _mobileNearbyBtnMob.onClick.Add(_mobileNearbyBtnMobClick);
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileNearbyBtnRefresh != null && !_mobileNearbyBtnRefresh._disposed)
                {
                    _mobileNearbyBtnRefresh.touchable = true;
                    if (_mobileNearbyBtnRefresh is GButton b)
                    {
                        b.enabled = true;
                        b.grayed = false;
                        b.changeStateOnClick = false;
                    }
                    _mobileNearbyBtnRefresh.onClick.Remove(_mobileNearbyBtnRefreshClick);
                    _mobileNearbyBtnRefresh.onClick.Add(_mobileNearbyBtnRefreshClick);
                }
            }
            catch
            {
            }

            // 列表文本：不依赖 publish 结构，直接覆盖/叠加一个文本控件。
            GRichTextField tf = null;
            try { tf = page1.GetChild(MobileNearbyListTextName) as GRichTextField; } catch { tf = null; }
            if (tf == null || tf._disposed)
            {
                try
                {
                    tf = new GRichTextField
                    {
                        name = MobileNearbyListTextName,
                        touchable = false,
                        text = string.Empty,
                        align = AlignType.Left,
                        verticalAlign = VertAlignType.Top,
                        autoSize = AutoSizeType.None,
                        UBBEnabled = false,
                    };

                    TextFormat fmt = tf.textFormat;
                    fmt.size = MobileNearbyDesiredFontSize;
                    fmt.color = XnaColor.White;
                    fmt.lineSpacing = Math.Max(fmt.lineSpacing, 6);
                    tf.textFormat = fmt;

                    tf.stroke = Math.Max(tf.stroke, 1);
                    tf.strokeColor = new XnaColor(0, 0, 0, 200);

                    page1.AddChild(tf);
                }
                catch
                {
                    tf = null;
                }
            }

            _mobileNearbyListText = tf;

            try
            {
                if (_mobileNearbyListText != null && !_mobileNearbyListText._disposed)
                {
                    _mobileNearbyListText.SetPosition(0, 0);
                    _mobileNearbyListText.SetSize(page1.width, page1.height);
                    _mobileNearbyListText.AddRelation(page1, RelationType.Size);
                }
            }
            catch
            {
            }

            _mobileNearbyDirty = true;
        }

        private static List<(string Name, Point Location, int Dist)> CollectNearbyObjects(MobileNearbyListMode mode)
        {
            var list = new List<(string Name, Point Location, int Dist)>(32);

            UserObject user = GameScene.User;
            if (user == null)
                return list;

            Point origin;
            try { origin = user.CurrentLocation; } catch { origin = Point.Empty; }

            uint selfId = 0;
            try { selfId = user.ObjectID; } catch { selfId = 0; }

            List<MapObject> objects = MapControl.Objects;
            if (objects == null)
                return list;

            for (int i = 0; i < objects.Count; i++)
            {
                MapObject obj = objects[i];
                if (obj == null)
                    continue;

                if (selfId != 0 && obj.ObjectID == selfId)
                    continue;

                bool match = false;
                if (mode == MobileNearbyListMode.Players)
                    match = obj is PlayerObject;
                else if (mode == MobileNearbyListMode.Monsters)
                    match = obj is MonsterObject;

                if (!match)
                    continue;

                string name = obj.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                Point loc;
                try { loc = obj.CurrentLocation; } catch { loc = Point.Empty; }

                int dx = Math.Abs(loc.X - origin.X);
                int dy = Math.Abs(loc.Y - origin.Y);
                int dist = dx + dy;

                list.Add((name.Trim(), loc, dist));
            }

            list.Sort((a, b) =>
            {
                int d = a.Dist.CompareTo(b.Dist);
                if (d != 0) return d;
                int y = a.Location.Y.CompareTo(b.Location.Y);
                if (y != 0) return y;
                int x = a.Location.X.CompareTo(b.Location.X);
                if (x != 0) return x;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            return list;
        }

        private static void TryRefreshMobileMainHudNearbyPanelIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileNearbyRoot != null || _mobileNearbyPage1 != null || _mobileNearbyListText != null)
                    ResetMobileNearbyPanelBindings();
                return;
            }

            TryBindMobileNearbyPanelIfDue();

            if (_mobileNearbyRoot == null || _mobileNearbyRoot._disposed ||
                _mobileNearbyPage1 == null || _mobileNearbyPage1._disposed ||
                _mobileNearbyListText == null || _mobileNearbyListText._disposed)
                return;

            if (!force && !_mobileNearbyDirty && DateTime.UtcNow < _nextMobileNearbyRefreshUtc)
                return;

            _mobileNearbyDirty = false;
            _nextMobileNearbyRefreshUtc = DateTime.UtcNow.AddMilliseconds(600);

            List<(string Name, Point Location, int Dist)> items = CollectNearbyObjects(_mobileNearbyMode);

            // 限制数量，避免长文本影响性能
            int max = 30;
            if (items.Count > max)
                items.RemoveRange(max, items.Count - max);

            var sb = new StringBuilder(512);
            for (int i = 0; i < items.Count; i++)
            {
                (string name, Point loc, _) = items[i];
                sb.Append(name);
                sb.Append("  ");
                sb.Append('(');
                sb.Append(loc.X);
                sb.Append(',');
                sb.Append(loc.Y);
                sb.Append(')');
                if (i != items.Count - 1)
                    sb.AppendLine();
            }

            try
            {
                // 某些 publish 包/缩放逻辑可能会在运行中改写 TextFormat.size，这里强制锁定。
                TextFormat fmt = _mobileNearbyListText.textFormat;
                if (fmt.size != MobileNearbyDesiredFontSize)
                {
                    fmt.size = MobileNearbyDesiredFontSize;
                    _mobileNearbyListText.textFormat = fmt;
                }

                _mobileNearbyListText.text = sb.ToString();
            }
            catch
            {
            }
        }
    }
}
