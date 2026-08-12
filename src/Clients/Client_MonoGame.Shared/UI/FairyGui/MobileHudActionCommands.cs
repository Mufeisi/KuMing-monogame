using System;
using System.Collections.Generic;
using System.Drawing;
using FairyGUI;
using MonoShare.MirControls;
using MonoShare.MirNetwork;
using MonoShare.MirObjects;
using MonoShare.MirScenes;
using C = ClientPackets;

namespace MonoShare
{
    internal static class MobileHudActionCommands
    {
        private const int MobileNearbyPickupRange = 8; // 保持中等范围，便于移动端就近拾取（适当放宽避免点不到脚边物品）
        private const int MobileNearbyPickupMaxTicks = 50;
        private const int MobileNearbyPickupMoveThrottleMs = 180;

        private static bool _mobileNearbyPickupActive;
        private static long _mobileNearbyPickupStopAtMs;
        private static long _mobileNearbyPickupLastMoveAtMs;
        private static int _mobileNearbyPickupTickCount;
        private static Point? _mobileNearbyPickupTarget;
        private static TimerCallback _mobileNearbyPickupTimer;

        public static void TryPickUp()
        {
            // 单格拾取在服务端只会捡“脚下”格子；移动端按钮做一次小范围“寻物 -> 走近 -> 拾取”。
            TryStartMobileNearbyPickup();
        }

        private static void TryStartMobileNearbyPickup()
        {
            long now = CMain.Time;

            _mobileNearbyPickupActive = true;
            _mobileNearbyPickupStopAtMs = now + 5000;
            _mobileNearbyPickupTickCount = 0;
            _mobileNearbyPickupTarget = null;

            try
            {
                UserObject user = MapObject.User;
                if (user != null)
                    CMain.SaveLog($"MobilePickup: start loc={user.CurrentLocation.X},{user.CurrentLocation.Y} range={MobileNearbyPickupRange}");
            }
            catch
            {
            }

            _mobileNearbyPickupTimer ??= OnMobileNearbyPickupTick;
            try
            {
                if (!Timers.inst.Exists(_mobileNearbyPickupTimer))
                    Timers.inst.Add(0.08f, repeat: 0, _mobileNearbyPickupTimer);
            }
            catch
            {
            }

            // 立刻执行一次，减少按钮点击后的等待感
            TryMobileNearbyPickupOnce();
        }

        private static void OnMobileNearbyPickupTick(object _)
        {
            if (!_mobileNearbyPickupActive)
            {
                try { Timers.inst.Remove(_mobileNearbyPickupTimer); } catch { }
                return;
            }

            long now = CMain.Time;
            if (now > _mobileNearbyPickupStopAtMs || _mobileNearbyPickupTickCount++ >= MobileNearbyPickupMaxTicks)
            {
                _mobileNearbyPickupActive = false;
                _mobileNearbyPickupTarget = null;
                try { Timers.inst.Remove(_mobileNearbyPickupTimer); } catch { }
                return;
            }

            TryMobileNearbyPickupOnce();
        }

        private static void TryMobileNearbyPickupOnce()
        {
            GameScene scene = GameScene.Scene;
            if (scene == null)
                return;

            UserObject user = MapObject.User;
            if (user == null)
                return;

            if (!TryFindNearestGroundItem(user.CurrentLocation, MobileNearbyPickupRange, out ItemObject nearest, out int distance))
            {
                try
                {
                    CMain.SaveLog($"MobilePickup: no-nearby-item loc={user.CurrentLocation.X},{user.CurrentLocation.Y}");
                }
                catch
                {
                }

                TryPickUpCurrentCell();
                _mobileNearbyPickupActive = false;
                _mobileNearbyPickupTarget = null;
                try { Timers.inst.Remove(_mobileNearbyPickupTimer); } catch { }
                return;
            }

            Point target = nearest.CurrentLocation;

            // 已到达目标：尝试拾取
            if (distance <= 0 || target == user.CurrentLocation)
            {
                _mobileNearbyPickupTarget = null;
                TryPickUpCurrentCell();
                return;
            }

            if (!_mobileNearbyPickupTarget.HasValue || _mobileNearbyPickupTarget.Value != target)
            {
                try
                {
                    CMain.SaveLog($"MobilePickup: target name={nearest.Name} loc={target.X},{target.Y} distance={distance}");
                }
                catch
                {
                }
            }

            long now = CMain.Time;
            if (_mobileNearbyPickupTarget.HasValue && _mobileNearbyPickupTarget.Value == target && now - _mobileNearbyPickupLastMoveAtMs < MobileNearbyPickupMoveThrottleMs)
                return;

            _mobileNearbyPickupTarget = target;
            _mobileNearbyPickupLastMoveAtMs = now;

            try
            {
                CMain.SaveLog($"MobilePickup: move-to {target.X},{target.Y}");
                scene.MapControl?.SetMobileTapMoveDestination(target);
            }
            catch
            {
            }
        }

        private static void TryPickUpCurrentCell()
        {
            if (CMain.Time <= GameScene.PickUpTime)
                return;

            GameScene.PickUpTime = CMain.Time + 200;
            try
            {
                UserObject user = MapObject.User;
                if (user != null)
                    CMain.SaveLog($"MobilePickup: send PickUp at {user.CurrentLocation.X},{user.CurrentLocation.Y}");
            }
            catch
            {
            }
            Network.Enqueue(new C.PickUp());
        }

        private static bool TryFindNearestGroundItem(Point origin, int range, out ItemObject nearest, out int distance)
        {
            nearest = null;
            distance = int.MaxValue;

            if (range < 0)
                return false;

            try
            {
                for (int i = 0; i < MapControl.Objects.Count; i++)
                {
                    if (MapControl.Objects[i] is not ItemObject item)
                        continue;

                    Point p = item.CurrentLocation;
                    if (!Functions.InRange(p, origin, range))
                        continue;

                    int d = Math.Max(Math.Abs(p.X - origin.X), Math.Abs(p.Y - origin.Y));
                    if (d < distance)
                    {
                        nearest = item;
                        distance = d;
                        if (distance <= 0)
                            break;
                    }
                }
            }
            catch
            {
                nearest = null;
                distance = int.MaxValue;
            }

            return nearest != null;
        }

        public static void TryAttack()
        {
            GameScene scene = GameScene.Scene;
            if (scene == null)
                return;

            UserObject user = MapObject.User;
            if (user == null)
                return;

            MapObject target = MapObject.TargetObject;
            if (target == null || target.Dead)
            {
                scene.OutputMessage("请先点选目标");
                return;
            }

            if (CMain.Time < MapControl.InputDelay ||
                user.Poison.HasFlag(PoisonType.Paralysis) ||
                user.Poison.HasFlag(PoisonType.LRParalysis) ||
                user.Poison.HasFlag(PoisonType.Frozen) ||
                user.Fishing)
            {
                return;
            }

            if (CMain.Time < user.BlizzardStopTime || CMain.Time < user.ReincarnationStopTime)
                return;

            if (CMain.Time <= GameScene.AttackTime)
                return;

            if (user.RidingMount)
            {
                UserItem[] equipment = user.Equipment;
                if (equipment == null || equipment.Length <= (int)EquipmentSlot.Mount)
                    return;

                UserItem mount = equipment[(int)EquipmentSlot.Mount];
                if (mount == null ||
                    mount.Slots == null ||
                    mount.Slots.Length <= (int)MountSlot.Bells ||
                    mount.Slots[(int)MountSlot.Bells] == null)
                {
                    scene.OutputMessage("坐骑缺少铃铛，无法攻击。");
                    return;
                }
            }

            MirDirection direction = Functions.DirectionFromPoint(user.CurrentLocation, target.CurrentLocation);

            if (user.Class == MirClass.Archer && user.HasClassWeapon && !user.RidingMount)
            {
                if (!Functions.InRange(target.CurrentLocation, user.CurrentLocation, Globals.MaxAttackRange))
                {
                    scene.OutputMessage("目标太远了.");
                    return;
                }

                user.QueuedAction = new QueuedAction
                {
                    Action = MirAction.AttackRange1,
                    Direction = direction,
                    Location = user.CurrentLocation,
                    Params = new List<object>
                    {
                        target.ObjectID,
                        target.CurrentLocation,
                    },
                };
                return;
            }

            if (!Functions.InRange(target.CurrentLocation, user.CurrentLocation, 1))
            {
                scene.OutputMessage("目标太远了.");
                return;
            }

            user.QueuedAction = new QueuedAction
            {
                Action = MirAction.Attack1,
                Direction = direction,
                Location = user.CurrentLocation,
            };
        }
    }
}
