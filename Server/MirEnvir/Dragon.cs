using System.Drawing;
﻿using Server.MirDatabase;
using Server.MirObjects;
using Server.MirObjects.Monsters;
using Server.Scripting;

namespace Server.MirEnvir
{
    public class Dragon
    {
        private readonly int ProcessDelay = 2000;
        public int DeLevelDelay = 60 * (60 * 1000);
        private long ProcessTime;
        public byte MaxLevel = Globals.MaxDragonLevel;
        private Rectangle DropArea;
        public long DeLevelTime;
        public bool Loaded;

        private static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        private readonly Point[] BodyLocations = new[]
        {
            new Point(-3, -1),
            new Point(-3, -0),
            new Point(-2, -3),
            new Point(-2, -2),
            new Point(-2, -1),
            new Point(-2, 0),
            new Point(-2, 1),
            new Point(-1, -2),
            new Point(-1, -1),
            new Point(-1, 0),
            new Point(-1, 1),
            new Point(-1, 2),
            new Point(0, -2),
            new Point(0, -1),
            new Point(0, 1),
            new Point(0, 2),
            new Point(0, 3),
            new Point(1, -2),
            new Point(1, 0),
            new Point(1, 1),
            new Point(1, 2),
            new Point(1, 3),
            new Point(2, 1),
            new Point(2, 2),
        };


        public DragonInfo Info;
        public MonsterObject LinkedMonster;

        public Dragon(DragonInfo info)
        {
            Info = info;
        }

        private ActivityDescriptor CreateActivityDescriptor()
        {
            var map = LinkedMonster?.CurrentMap ?? Envir.GetMapByNameAndInstance(Info.MapFileName);
            return new ActivityDescriptor(ActivitySourceType.Dragon, Info.MapFileName, Info.MonsterName ?? string.Empty, map);
        }

        private ActivityProgressRequest TryCSharpActivityProgressBefore(ActivityProgressReason reason, int previousValue, int value)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return null;
            if (!ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnActivityProgressBefore)) return null;

            var request = new ActivityProgressRequest(CreateActivityDescriptor(), ActivityProgressKind.Wave, reason, previousValue, value, MaxLevel, dragon: this);
            Envir.CSharpScripts.TryHandleActivityProgressBefore(request);
            return request;
        }

        private void TryCSharpActivityProgressAfter(ActivityProgressReason reason, int previousValue, int value, bool executedLegacy, ScriptHookDecision decision)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return;
            if (!ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnActivityProgressAfter)) return;

            var result = new ActivityProgressResult(CreateActivityDescriptor(), ActivityProgressKind.Wave, reason, previousValue, value, MaxLevel, executedLegacy, decision, dragon: this);
            Envir.CSharpScripts.TryHandleActivityProgressAfter(result);
        }

        private ActivityRewardRequest TryCSharpActivityRewardBefore(int rewardLevel, uint gold, IReadOnlyList<UserItem> items, MapObject owner, long ownerDuration, Rectangle dropArea)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return null;
            if (!ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnActivityRewardBefore)) return null;

            var request = new ActivityRewardRequest(CreateActivityDescriptor(), ActivityRewardReason.WaveAdvance, rewardLevel, gold, items, owner, ownerDuration, dropArea, this);
            Envir.CSharpScripts.TryHandleActivityRewardBefore(request);
            return request;
        }

        private void TryCSharpActivityRewardAfter(int rewardLevel, uint requestedGold, uint distributedGold, IReadOnlyList<UserItem> items, int distributedItemCount, MapObject owner, long ownerDuration, Rectangle dropArea, bool success, bool executedLegacy, ScriptHookDecision decision)
        {
            var scriptsRuntimeActive = Settings.CSharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            if (!scriptsRuntimeActive) return;
            if (!ScriptDispatchPolicy.ShouldTryCSharp(ScriptHookKeys.OnActivityRewardAfter)) return;

            var result = new ActivityRewardResult(CreateActivityDescriptor(), ActivityRewardReason.WaveAdvance, rewardLevel, requestedGold, distributedGold, items, distributedItemCount, owner, ownerDuration, dropArea, success, executedLegacy, decision, this);
            Envir.CSharpScripts.TryHandleActivityRewardAfter(result);
        }

        private static int ClampWaveValue(int value, int maxValue)
        {
            if (value < 1) return 1;
            if (value > maxValue) return maxValue;
            return value;
        }

        private Rectangle NormalizeDropArea(Rectangle area)
        {
            if (area.Width < 0 || area.Height < 0)
                return DropArea;

            return area;
        }
        public bool Load()
        {
            try
            {
                MonsterInfo info = Envir.GetMonsterInfo(Info.MonsterName);
                if (info == null)
                {
                    MessageQueue.Enqueue("破天魔龙加载失败因为使用了不可用的怪物名: " + Info.MonsterName);
                    return false;
                }
                LinkedMonster = MonsterObject.GetMonster(info);

                Map map = Envir.GetMapByNameAndInstance(Info.MapFileName);
                if (map == null)
                {
                    MessageQueue.Enqueue("破天魔龙加载失败因为使用了不可用的地图名: " + Info.MapFileName);
                    return false;
                }

                if (Info.Location.X > map.Width || Info.Location.Y > map.Height)
                {
                    MessageQueue.Enqueue("破天魔龙加载失败因为使用了不可用的坐标X|Y: " + Info.MapFileName);
                    return false;
                }

                if (LinkedMonster.Spawn(map, Info.Location))
                {
                    if (LinkedMonster is EvilMir mob)
                    {
                        if (mob != null)
                        {
                            mob.DragonLink = true;
                        }
                    }
                    MonsterInfo bodyinfo = Envir.GetMonsterInfo(Info.BodyName);
                    if (bodyinfo != null)
                    {
                        MonsterObject bodymob;
                        Point spawnlocation = Point.Empty;
                        for (int i = 0; i <= BodyLocations.Length - 1; i++)
                        {
                            bodymob = MonsterObject.GetMonster(bodyinfo);
                            spawnlocation = new Point(LinkedMonster.CurrentLocation.X + BodyLocations[i].X, LinkedMonster.CurrentLocation.Y + BodyLocations[i].Y);
                            if (bodymob != null) bodymob.Spawn(LinkedMonster.CurrentMap, spawnlocation);
                        }
                    }

                    DropArea = new Rectangle(Info.DropAreaTop.X, Info.DropAreaTop.Y, Info.DropAreaBottom.X - Info.DropAreaTop.X, Info.DropAreaBottom.Y - Info.DropAreaTop.Y);
                    Loaded = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }

            MessageQueue.Enqueue("破天魔龙加载失败");
            return false;
        }
        public void GainExp(int ammount)
        {
            if (ammount <= 0) return;

            Info.Experience += ammount;
            if (Info.Experience >= Info.Exps[Math.Min(11, Info.Level - 1)])
            {
                Info.Experience -= Info.Exps[Math.Min(11, Info.Level - 1)];
                LevelUp();
            }
        }
        public void LevelUp()
        {
            int previousLevel = Info.Level;
            int targetLevel = Info.Level < Globals.MaxDragonLevel ? Info.Level + 1 : Info.Level;
            ScriptHookDecision scriptDecision = ScriptHookDecision.Continue;

            var request = TryCSharpActivityProgressBefore(ActivityProgressReason.Advance, previousLevel, targetLevel);
            if (request != null)
            {
                targetLevel = ClampWaveValue(request.Value, MaxLevel);
                scriptDecision = request.Decision;

                if (scriptDecision == ScriptHookDecision.Cancel)
                    return;

                if (scriptDecision == ScriptHookDecision.Handled)
                {
                    TryCSharpActivityProgressAfter(ActivityProgressReason.Advance, previousLevel, Info.Level, false, scriptDecision);
                    return;
                }
            }

            Drop((byte)previousLevel);
            Info.Level = (byte)targetLevel;
            //if it reaches max level > make it stay that level for 6*deleveldelay and then reset to 0, rather then letting ppl farm it by making it drop every hour
            if (Info.Level == Globals.MaxDragonLevel)
                DeLevelTime = Envir.Time + (6 * DeLevelDelay);

            TryCSharpActivityProgressAfter(ActivityProgressReason.Advance, previousLevel, Info.Level, true, scriptDecision);
        }
        public void LevelDown()
        {
            if (Info.Level <= 1) return;

            int previousLevel = Info.Level;
            int targetLevel = Info.Level - 1;
            ScriptHookDecision scriptDecision = ScriptHookDecision.Continue;

            var request = TryCSharpActivityProgressBefore(ActivityProgressReason.Regress, previousLevel, targetLevel);
            if (request != null)
            {
                targetLevel = ClampWaveValue(request.Value, MaxLevel);
                scriptDecision = request.Decision;

                if (scriptDecision == ScriptHookDecision.Cancel)
                    return;

                if (scriptDecision == ScriptHookDecision.Handled)
                {
                    TryCSharpActivityProgressAfter(ActivityProgressReason.Regress, previousLevel, Info.Level, false, scriptDecision);
                    return;
                }
            }

            Info.Level = (byte)targetLevel;
            Info.Experience = 0;

            TryCSharpActivityProgressAfter(ActivityProgressReason.Regress, previousLevel, Info.Level, true, scriptDecision);
        }
        public void Drop(byte level)
        {
            if (level > Info.Drops.Length) return;
            if (Info.Drops[level - 1] == null) return;
            if (LinkedMonster == null) return;
            List<DragonInfo.DropInfo> droplist = new List<DragonInfo.DropInfo>(Info.Drops[level - 1]);
            uint gold = 0;
            List<UserItem> items = new List<UserItem>();

            PlayerObject expOwnerPlayer = LinkedMonster.EXPOwner as PlayerObject;

            if (expOwnerPlayer == null && LinkedMonster.EXPOwner is HeroObject heroOwner)
                expOwnerPlayer = heroOwner.Owner;

            var dropRateRequest = EconomyRateHooks.ResolveDropRate(expOwnerPlayer, "dragon");

            if (dropRateRequest.Decision == ScriptHookDecision.Cancel &&
                !string.IsNullOrEmpty(dropRateRequest.FailMessage) &&
                expOwnerPlayer != null)
            {
                expOwnerPlayer.ReceiveChat(dropRateRequest.FailMessage, ChatType.System);
            }

            var effectiveDropRate = dropRateRequest.Decision == ScriptHookDecision.Continue
                ? dropRateRequest.Rate
                : 0F;

            for (int i = 0; effectiveDropRate > 0 && i < droplist.Count; i++)
            {
                DragonInfo.DropInfo drop = droplist[i];

                int rate = (int)(drop.Chance / effectiveDropRate); if (rate < 1) rate = 1;
                if (Envir.Random.Next(rate) != 0) continue;

                if (drop.Gold > 0)
                {
                    int goldReward = Envir.Random.Next((int)(drop.Gold / 2), (int)(drop.Gold + drop.Gold / 2)); //Messy

                    if (goldReward <= 0) continue;
                    gold += (uint)goldReward;
                }
                else
                {
                    UserItem item = Envir.CreateDropItem(drop.Item);
                    if (item == null) continue;
                    items.Add(item);
                }
            }

            long ownerDuration = Settings.Minute;
            MapObject owner = LinkedMonster.EXPOwner;
            Rectangle dropArea = DropArea;
            ScriptHookDecision scriptDecision = ScriptHookDecision.Continue;

            var rewardRequest = TryCSharpActivityRewardBefore(level, gold, items, owner, ownerDuration, dropArea);
            if (rewardRequest != null)
            {
                gold = rewardRequest.Gold;
                items = rewardRequest.Items ?? new List<UserItem>(0);
                owner = rewardRequest.Owner;
                ownerDuration = rewardRequest.OwnerDuration;
                dropArea = NormalizeDropArea(rewardRequest.DropArea);
                scriptDecision = rewardRequest.Decision;

                if (scriptDecision == ScriptHookDecision.Cancel)
                {
                    TryCSharpActivityRewardAfter(level, gold, 0, items, 0, owner, ownerDuration, dropArea, false, false, scriptDecision);
                    return;
                }

                if (scriptDecision == ScriptHookDecision.Handled)
                {
                    TryCSharpActivityRewardAfter(level, gold, gold, items, items.Count, owner, ownerDuration, dropArea, true, false, scriptDecision);
                    return;
                }
            }

            bool success = true;
            uint distributedGold = 0;
            int distributedItemCount = 0;

            if (gold > 0)
            {
                if (DropGold(gold, owner, ownerDuration, dropArea))
                {
                    distributedGold = gold;
                }
                else
                {
                    success = false;
                }
            }

            if (success)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (!DropItem(items[i], owner, ownerDuration, dropArea))
                    {
                        success = false;
                        break;
                    }

                    distributedItemCount++;
                }
            }

            TryCSharpActivityRewardAfter(level, gold, distributedGold, items, distributedItemCount, owner, ownerDuration, dropArea, success, true, scriptDecision);
        }
        protected bool DropItem(UserItem item, MapObject owner, long ownerDuration, Rectangle dropArea)
        {
            Point droplocation = new Point(dropArea.Left + (dropArea.Width / 2), dropArea.Top);
            ItemObject ob = new ItemObject(this.LinkedMonster, item, droplocation)
            {
                Owner = owner,
                OwnerTime = Envir.Time + ownerDuration,
            };

            return ob.DragonDrop(dropArea.Width / 2);
        }

        protected bool DropGold(uint gold, MapObject owner, long ownerDuration, Rectangle dropArea)
        {
            if (owner != null && owner.CanGainGold(gold))
            {
                owner.WinGold(gold);
                return true;
            }

            Point droplocation = new Point(dropArea.Left + (dropArea.Width / 2), dropArea.Top);
            ItemObject ob = new ItemObject(this.LinkedMonster, gold, droplocation)
            {
                Owner = owner,
                OwnerTime = Envir.Time + ownerDuration,
            };

            return ob.DragonDrop(dropArea.Width / 2);
        }

        public void Process()
        {
            if (!Loaded) return;
            if (Envir.Time < ProcessTime) return;

            ProcessTime = Envir.Time + ProcessDelay;

            if ((Info.Level >= Globals.MaxDragonLevel) && (Envir.Time > DeLevelTime))
            {
                int previousLevel = Info.Level;
                int targetLevel = 1;
                ScriptHookDecision scriptDecision = ScriptHookDecision.Continue;

                var request = TryCSharpActivityProgressBefore(ActivityProgressReason.End, previousLevel, targetLevel);
                if (request != null)
                {
                    targetLevel = ClampWaveValue(request.Value, MaxLevel);
                    scriptDecision = request.Decision;

                    if (scriptDecision == ScriptHookDecision.Cancel)
                    {
                        DeLevelTime = Envir.Time + DeLevelDelay;
                        return;
                    }

                    if (scriptDecision == ScriptHookDecision.Handled)
                    {
                        TryCSharpActivityProgressAfter(ActivityProgressReason.End, previousLevel, Info.Level, false, scriptDecision);
                        DeLevelTime = Envir.Time + DeLevelDelay;
                        return;
                    }
                }

                Info.Level = (byte)targetLevel;
                Info.Experience = 0;
                TryCSharpActivityProgressAfter(ActivityProgressReason.End, previousLevel, Info.Level, true, scriptDecision);
                DeLevelTime = Envir.Time + DeLevelDelay;
            }

            if (Info.Level > 1 && Envir.Time > DeLevelTime)
            {
                LevelDown();
                DeLevelTime = Envir.Time + DeLevelDelay;
            }
        }
    }
}
