using System;
using System.Text;
using FairyGUI;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private const string MobileItemTipsPackageName = "UILoadingRes";
        private const string MobileItemTipsComponentName = "Tips";

        private static GComponent _mobileItemTips;
        private static ulong _mobileItemTipsUniqueId;
        private static bool _mobileItemTipsStageHookInstalled;

        private static readonly EventCallback1 MobileItemTipsStageClickCapture = OnStageClickCaptureForMobileItemTips;

        private static void EnsureMobileItemTipsStageHookInstalled()
        {
            if (_stage == null || !_initialized)
                return;

            if (_mobileItemTipsStageHookInstalled)
                return;

            try
            {
                _stage.onClick.AddCapture(MobileItemTipsStageClickCapture);
                _mobileItemTipsStageHookInstalled = true;
            }
            catch
            {
                _mobileItemTipsStageHookInstalled = false;
            }
        }

        private static void OnStageClickCaptureForMobileItemTips(EventContext context)
        {
            GComponent tips = _mobileItemTips;
            if (tips == null || tips._disposed || !tips.visible)
                return;

            try
            {
                float x = 0;
                float y = 0;
                try
                {
                    var pos = _stage != null ? _stage.touchPosition : default;
                    x = pos.X;
                    y = pos.Y;
                }
                catch
                {
                    x = 0;
                    y = 0;
                }

                if (x >= tips.x && x <= tips.x + tips.width && y >= tips.y && y <= tips.y + tips.height)
                    return;
            }
            catch
            {
            }

            HideMobileItemTips();
        }

        private static GComponent EnsureMobileItemTipsCreated()
        {
            if (_mobileItemTips != null && !_mobileItemTips._disposed)
                return _mobileItemTips;

            _mobileItemTips = null;
            _mobileItemTipsUniqueId = 0;

            try
            {
                EnsureMobileItemTipsStageHookInstalled();

                GObject created = UIPackage.CreateObject(MobileItemTipsPackageName, MobileItemTipsComponentName);
                if (created is not GComponent comp || comp._disposed)
                    return null;

                comp.visible = false;
                comp.touchable = false;
                comp.sortingOrder = 9999;

                try { if (comp.GetChild("DressBtn") is GObject o) o.visible = false; } catch { }
                try { if (comp.GetChild("SplitBtn") is GObject o) o.visible = false; } catch { }
                try { if (comp.GetChild("UseBtn1") is GObject o) o.visible = false; } catch { }
                try { if (comp.GetChild("UseBtn2") is GObject o) o.visible = false; } catch { }

                try { GRoot.inst.AddChild(comp); } catch { }

                _mobileItemTips = comp;
                return comp;
            }
            catch
            {
                _mobileItemTips = null;
                return null;
            }
        }

        private static void HideMobileItemTips()
        {
            try
            {
                if (_mobileItemTips != null && !_mobileItemTips._disposed)
                    _mobileItemTips.visible = false;
            }
            catch
            {
            }

            _mobileItemTipsUniqueId = 0;
        }

        private static void ShowMobileItemTips(UserItem item)
        {
            if (item == null || item.Info == null)
            {
                HideMobileItemTips();
                return;
            }

            GComponent tips = EnsureMobileItemTipsCreated();
            if (tips == null || tips._disposed)
                return;

            try
            {
                if (_mobileItemTipsUniqueId == item.UniqueID && tips.visible)
                    return;
            }
            catch
            {
            }

            _mobileItemTipsUniqueId = item.UniqueID;

            try
            {
                if (tips.GetChild("ItemName") is GRichTextField name && name != null && !name._disposed)
                    name.text = item.FriendlyName ?? (item.Info?.FriendlyName ?? string.Empty);
            }
            catch
            {
            }

            try
            {
                if (tips.GetChild("TipsMsg") is GRichTextField msg && msg != null && !msg._disposed)
                    msg.text = BuildMobileItemTipsText(item);
            }
            catch
            {
            }

            try
            {
                float x = 0;
                float y = 0;
                try
                {
                    var pos = _stage != null ? _stage.touchPosition : default;
                    x = pos.X;
                    y = pos.Y;
                }
                catch
                {
                    x = 0;
                    y = 0;
                }

                // 默认出现在点击点右下角，并做屏幕内约束
                float xx = x + 12;
                float yy = y + 12;

                float maxX = Math.Max(0, GRoot.inst.width - tips.width);
                float maxY = Math.Max(0, GRoot.inst.height - tips.height);

                if (xx > maxX) xx = Math.Max(0, x - tips.width - 12);
                if (yy > maxY) yy = Math.Max(0, y - tips.height - 12);

                tips.x = (int)Math.Clamp(xx, 0, maxX);
                tips.y = (int)Math.Clamp(yy, 0, maxY);
            }
            catch
            {
            }

            try
            {
                tips.visible = true;

                try
                {
                    if (tips.parent != null && tips.parent is GComponent parent && !parent._disposed)
                        parent.SetChildIndex(tips, parent.numChildren - 1);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private sealed class MobileLongPressTipBinding
        {
            public GObject Target;
            public Func<UserItem> ResolveItem;

            public float StartX;
            public float StartY;
            public bool Armed;
            public bool LongPressTriggered;
            public bool SuppressClick;

            public readonly TimerCallback TimerCallback;
            public readonly EventCallback1 TouchBegin;
            public readonly EventCallback1 TouchMove;
            public readonly EventCallback1 TouchEnd;
            public readonly EventCallback1 ClickCapture;

            public MobileLongPressTipBinding(GObject target, Func<UserItem> resolveItem)
            {
                Target = target;
                ResolveItem = resolveItem;

                TimerCallback = OnTimer;
                TouchBegin = OnTouchBegin;
                TouchMove = OnTouchMove;
                TouchEnd = OnTouchEnd;
                ClickCapture = OnClickCapture;
            }

            public void Attach()
            {
                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }

                try { Target.onTouchBegin.Add(TouchBegin); } catch { }
                try { Target.onTouchMove.Add(TouchMove); } catch { }
                try { Target.onTouchEnd.Add(TouchEnd); } catch { }
                try { Target.onClick.AddCapture(ClickCapture); } catch { }
            }

            public void Detach()
            {
                try { Timers.inst.Remove(TimerCallback); } catch { }

                if (Target == null || Target._disposed)
                    return;

                try { Target.onTouchBegin.Remove(TouchBegin); } catch { }
                try { Target.onTouchMove.Remove(TouchMove); } catch { }
                try { Target.onTouchEnd.Remove(TouchEnd); } catch { }
                try { Target.onClick.RemoveCapture(ClickCapture); } catch { }
            }

            private void Arm(EventContext context)
            {
                if (Target == null || Target._disposed)
                    return;

                Armed = true;
                LongPressTriggered = false;
                SuppressClick = false;

                try
                {
                    InputEvent evt = context?.inputEvent;
                    StartX = evt != null ? evt.x : 0F;
                    StartY = evt != null ? evt.y : 0F;
                }
                catch
                {
                    StartX = 0F;
                    StartY = 0F;
                }

                float thresholdSec = 0.45f;
                try
                {
                    thresholdSec = Math.Clamp(Settings.MobileTouchLongPressThresholdMs / 1000f, 0.25f, 1.2f);
                }
                catch
                {
                    thresholdSec = 0.45f;
                }

                try { Timers.inst.Remove(TimerCallback); } catch { }
                try { Timers.inst.Add(thresholdSec, 1, TimerCallback); } catch { }
            }

            private void CancelArmed()
            {
                Armed = false;
                try { Timers.inst.Remove(TimerCallback); } catch { }
            }

            private void OnTouchBegin(EventContext context)
            {
                Arm(context);
            }

            private void OnTouchMove(EventContext context)
            {
                if (!Armed)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                float dx = x - StartX;
                float dy = y - StartY;
                float distSq = dx * dx + dy * dy;

                float tolerance = 32F;
                try { tolerance = Math.Max(8F, Settings.MobileTouchTapMoveTolerancePixels); } catch { tolerance = 32F; }
                if (distSq > tolerance * tolerance)
                    CancelArmed();
            }

            private void OnTouchEnd(EventContext context)
            {
                if (Armed)
                {
                    CancelArmed();
                    return;
                }

                if (!SuppressClick)
                    return;

                float x = 0F;
                float y = 0F;
                try
                {
                    InputEvent evt = context?.inputEvent;
                    x = evt != null ? evt.x : 0F;
                    y = evt != null ? evt.y : 0F;
                }
                catch
                {
                    x = 0F;
                    y = 0F;
                }

                bool inside = true;
                try
                {
                    var rect = Target.LocalToGlobal(new System.Drawing.RectangleF(0, 0, Target.width, Target.height));
                    inside = x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
                }
                catch
                {
                    inside = true;
                }

                // 抬起点不在目标上：不会触发 click，避免 SuppressClick 残留影响下一次点按
                if (!inside)
                {
                    SuppressClick = false;
                    LongPressTriggered = false;
                }
            }

            private void OnTimer(object param)
            {
                if (!Armed)
                    return;

                Armed = false;
                LongPressTriggered = true;
                SuppressClick = true;

                if (Target == null || Target._disposed)
                    return;

                UserItem item = null;
                try
                {
                    item = ResolveItem != null ? ResolveItem() : null;
                }
                catch
                {
                    item = null;
                }

                if (item == null || item.Info == null)
                {
                    HideMobileItemTips();
                    return;
                }

                ShowMobileItemTips(item);
            }

            private void OnClickCapture(EventContext context)
            {
                if (!SuppressClick)
                    return;

                try { context?.StopPropagation(); } catch { }
                try { context?.PreventDefault(); } catch { }

                SuppressClick = false;
                LongPressTriggered = false;
            }
        }

        private static MobileLongPressTipBinding BindMobileLongPressItemTips(GObject target, Func<UserItem> resolveItem)
        {
            if (target == null || target._disposed || resolveItem == null)
                return null;

            var binding = new MobileLongPressTipBinding(target, resolveItem);
            binding.Attach();
            return binding;
        }

        private static void UnbindMobileLongPressItemTips(MobileLongPressTipBinding binding)
        {
            if (binding == null)
                return;

            try
            {
                binding.Detach();
            }
            catch
            {
            }
        }

        private static string BuildMobileItemTipsText(UserItem item)
        {
            if (item == null || item.Info == null)
                return string.Empty;

            try
            {
                var sb = new StringBuilder(256);

                sb.Append("Type: ").Append(item.Info.Type).AppendLine();

                if (item.MaxDura > 0)
                {
                    sb.Append("Dura: ").Append(item.CurrentDura).Append('/').Append(item.MaxDura).AppendLine();
                }

                if (item.Weight > 0)
                {
                    sb.Append("Weight: ").Append(item.Weight).AppendLine();
                }

                try
                {
                    var stats = item.Info.Stats;
                    if (stats != null)
                    {
                        AppendStatRange(sb, stats, Stat.MinAC, Stat.MaxAC, "AC");
                        AppendStatRange(sb, stats, Stat.MinMAC, Stat.MaxMAC, "MAC");
                        AppendStatRange(sb, stats, Stat.MinDC, Stat.MaxDC, "DC");
                        AppendStatRange(sb, stats, Stat.MinMC, Stat.MaxMC, "MC");
                        AppendStatRange(sb, stats, Stat.MinSC, Stat.MaxSC, "SC");

                        AppendStatValue(sb, stats, Stat.Accuracy, "Accuracy");
                        AppendStatValue(sb, stats, Stat.Agility, "Agility");
                        AppendStatValue(sb, stats, Stat.AttackSpeed, "AttackSpeed");
                        AppendStatValue(sb, stats, Stat.Luck, "Luck");
                        AppendStatValue(sb, stats, Stat.HP, "HP");
                        AppendStatValue(sb, stats, Stat.MP, "MP");
                    }
                }
                catch
                {
                }

                try
                {
                    var added = item.AddedStats;
                    if (added != null && added.Values != null && added.Values.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Added:");

                        AppendStatRange(sb, added, Stat.MinAC, Stat.MaxAC, "AC");
                        AppendStatRange(sb, added, Stat.MinMAC, Stat.MaxMAC, "MAC");
                        AppendStatRange(sb, added, Stat.MinDC, Stat.MaxDC, "DC");
                        AppendStatRange(sb, added, Stat.MinMC, Stat.MaxMC, "MC");
                        AppendStatRange(sb, added, Stat.MinSC, Stat.MaxSC, "SC");

                        AppendStatValue(sb, added, Stat.Accuracy, "Accuracy");
                        AppendStatValue(sb, added, Stat.Agility, "Agility");
                        AppendStatValue(sb, added, Stat.AttackSpeed, "AttackSpeed");
                        AppendStatValue(sb, added, Stat.Luck, "Luck");
                        AppendStatValue(sb, added, Stat.HP, "HP");
                        AppendStatValue(sb, added, Stat.MP, "MP");
                    }
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(item.Info.ToolTip))
                {
                    sb.AppendLine();
                    sb.Append(item.Info.ToolTip);
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendStatValue(StringBuilder sb, Stats stats, Stat stat, string label)
        {
            if (sb == null || stats == null)
                return;

            int value = 0;
            try { value = stats[stat]; } catch { value = 0; }
            if (value == 0)
                return;

            sb.Append(label).Append(": ").Append(value).AppendLine();
        }

        private static void AppendStatRange(StringBuilder sb, Stats stats, Stat minStat, Stat maxStat, string label)
        {
            if (sb == null || stats == null)
                return;

            int min = 0;
            int max = 0;
            try { min = stats[minStat]; } catch { min = 0; }
            try { max = stats[maxStat]; } catch { max = 0; }

            if (min == 0 && max == 0)
                return;

            if (min == max)
                sb.Append(label).Append(": ").Append(min).AppendLine();
            else
                sb.Append(label).Append(": ").Append(min).Append('-').Append(max).AppendLine();
        }
    }
}
