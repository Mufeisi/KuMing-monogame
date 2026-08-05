using Server.MirObjects;
using Server.MirEnvir;

namespace Server.MirDatabase
{
    public class QuestProgressInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        public PlayerObject Owner;

        public int Index;

        public QuestInfo Info;

        public DateTime StartDateTime = DateTime.MinValue;
        public DateTime EndDateTime = DateTime.MaxValue;

        public List<QuestKillTaskProgress> KillTaskCount = new List<QuestKillTaskProgress>();
        public List<QuestItemTaskProgress> ItemTaskCount = new List<QuestItemTaskProgress>();
        public List<QuestFlagTaskProgress> FlagTaskSet = new List<QuestFlagTaskProgress>();

        public List<string> TaskList = new List<string>();

        public bool Taken
        {
            get { return StartDateTime > DateTime.MinValue; }
        }

        public bool Completed
        {
            get { return EndDateTime < DateTime.MaxValue; }
        }

        public bool New
        {
            get { return StartDateTime > Envir.Now.AddDays(-1); }
        }

        public QuestProgressInfo(int index)
        {
            Index = index;

            var sourceInfo = Envir.QuestInfoList.FirstOrDefault(e => e.Index == index);
            Info = sourceInfo?.CreateSnapshot();

            if (Info == null) return;

            foreach (var kill in Info.KillTasks)
            {
                KillTaskCount.Add(new QuestKillTaskProgress
                {
                    MonsterID = kill.Monster.Index,
                    Info = kill
                });
            }

            foreach (var item in Info.ItemTasks)
            {
                ItemTaskCount.Add(new QuestItemTaskProgress
                {
                    ItemID = item.Item.Index,
                    Info = item
                });
            }

            foreach (var flag in Info.FlagTasks)
            {
                FlagTaskSet.Add(new QuestFlagTaskProgress
                {
                    Number = flag.Number,
                    Info = flag
                });
            }
        }

        public QuestProgressInfo(BinaryReader reader, int version, int customVersion)
        {
            Index = reader.ReadInt32();
            var sourceInfo = Envir.QuestInfoList.FirstOrDefault(e => e.Index == Index);
            Info = sourceInfo?.CreateSnapshot();

            StartDateTime = DateTime.FromBinary(reader.ReadInt64());
            EndDateTime = DateTime.FromBinary(reader.ReadInt64());

            if (version < 90)
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var killCount = reader.ReadInt32();

                    if (Info != null && Info.KillTasks.Count > i)
                    {
                        var progress = new QuestKillTaskProgress
                        {
                            MonsterID = Info.KillTasks[i].Monster.Index,
                            Count = killCount,
                            Info = Info.KillTasks[i]
                        };
                        KillTaskCount.Add(progress);
                    }
                }

                count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var itemCount = reader.ReadInt32();
                    if (Info != null && Info.ItemTasks.Count > i)
                    {
                        var progress = new QuestItemTaskProgress
                        {
                            ItemID = Info.ItemTasks[i].Item.Index,
                            Count = itemCount,
                            Info = Info.ItemTasks[i]
                        };
                        ItemTaskCount.Add(progress);
                    }
                }

                count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var flagState = reader.ReadBoolean();
                    if (Info != null && Info.FlagTasks.Count > i)
                    {
                        var progress = new QuestFlagTaskProgress
                        {
                            Number = Info.FlagTasks[i].Number,
                            State = flagState,
                            Info = Info.FlagTasks[i]
                        };
                        FlagTaskSet.Add(progress);
                    }
                }
            }
            else
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var progress = new QuestKillTaskProgress
                    {
                        MonsterID = reader.ReadInt32(),
                        Count = reader.ReadInt32()
                    };

                    if (Info == null) continue;

                    foreach (var task in Info.KillTasks)
                    {
                        if (task.Monster.Index != progress.MonsterID) continue;

                        progress.Info = task;
                        KillTaskCount.Add(progress);
                        break;
                    }
                }

                count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var progress = new QuestItemTaskProgress
                    {
                        ItemID = reader.ReadInt32(),
                        Count = reader.ReadInt32()
                    };

                    if (Info == null) continue;

                    foreach (var task in Info.ItemTasks)
                    {
                        if (task.Item.Index != progress.ItemID) continue;

                        progress.Info = task;
                        ItemTaskCount.Add(progress);
                        break;
                    }
                }

                count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var progress = new QuestFlagTaskProgress
                    {
                        Number = reader.ReadInt32(),
                        State = reader.ReadBoolean()
                    };

                    if (Info == null) continue;

                    foreach (var task in Info.FlagTasks)
                    {
                        if (task.Number != progress.Number) continue;

                        progress.Info = task;
                        FlagTaskSet.Add(progress);
                        break;
                    }
                }

                //Add any new tasks which may have been added
                if (Info == null) return;

                foreach (var kill in Info.KillTasks)
                {
                    if (KillTaskCount.Any(x => x.MonsterID == kill.Monster.Index)) continue;

                    KillTaskCount.Add(new QuestKillTaskProgress
                    {
                        MonsterID = kill.Monster.Index,
                        Info = kill
                    });
                }

                foreach (var item in Info.ItemTasks)
                {
                    if (ItemTaskCount.Any(x => x.ItemID == item.Item.Index)) continue;

                    ItemTaskCount.Add(new QuestItemTaskProgress
                    {
                        ItemID = item.Item.Index,
                        Info = item
                    });
                }

                foreach (var flag in Info.FlagTasks)
                {
                    if (FlagTaskSet.Any(x => x.Number == flag.Number)) continue;

                    FlagTaskSet.Add(new QuestFlagTaskProgress
                    {
                        Number = flag.Number,
                        Info = flag
                    });
                }
            }
        }

        public void Init(PlayerObject player)
        {
            Owner = player;

            if (StartDateTime == DateTime.MinValue)
            {
                StartDateTime = Envir.Now;
            }
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Index);

            writer.Write(StartDateTime.ToBinary());
            writer.Write(EndDateTime.ToBinary());

            writer.Write(KillTaskCount.Count);
            for (int i = 0; i < KillTaskCount.Count; i++)
            {
                writer.Write(KillTaskCount[i].MonsterID);
                writer.Write(KillTaskCount[i].Count);
            }

            writer.Write(ItemTaskCount.Count);
            for (int i = 0; i < ItemTaskCount.Count; i++)
            {
                writer.Write(ItemTaskCount[i].ItemID);
                writer.Write(ItemTaskCount[i].Count);
            }

            writer.Write(FlagTaskSet.Count);
            for (int i = 0; i < FlagTaskSet.Count; i++)
            {
                writer.Write(FlagTaskSet[i].Number);
                writer.Write(FlagTaskSet[i].State);
            }
        }


        public bool CheckCompleted()
        {
            UpdateTasks();

            bool canComplete = true;

            for (int j = 0; j < KillTaskCount.Count; j++)
            {
                if (KillTaskCount[j].Complete) continue;

                canComplete = false;
            }

            for (int j = 0; j < ItemTaskCount.Count; j++)
            {
                if (ItemTaskCount[j].Complete) continue;

                canComplete = false;
            }

            for (int j = 0; j < FlagTaskSet.Count; j++)
            {
                if (FlagTaskSet[j].Complete) continue;

                canComplete = false;
            }

            if (!canComplete) return false;

            if (!Completed)
            {
                EndDateTime = Envir.Now;

                if (Info.TimeLimitInSeconds > 0)
                {
                    Owner.ExpireTimer($"Quest-{Index}");
                }
            }

            return true;
        }

        #region Need Requirement

        public bool NeedItem(ItemInfo iInfo)
        {
            return ItemTaskCount.Where((task, i) => task.Info.Item == iInfo && !task.Complete).Any();
        }

        public bool NeedKill(MonsterInfo mInfo)
        {
            return KillTaskCount.Where((task, i) => mInfo.Name.StartsWith(task.Info.Monster.Name, StringComparison.OrdinalIgnoreCase) && !task.Complete).Any();
        }

        public bool NeedFlag(int flagNumber)
        {
            return FlagTaskSet.Where((task, i) => task.Number == flagNumber && !task.Complete).Any();
        }

        #endregion

        #region Process Quest Task

        public void ProcessKill(MonsterInfo mInfo)
        {
            if (KillTaskCount.Count < 1) return;

            for (int i = 0; i < KillTaskCount.Count; i++)
            {
                if (!mInfo.Name.StartsWith(KillTaskCount[i].Info.Monster.Name, StringComparison.OrdinalIgnoreCase)) continue;
                KillTaskCount[i].Count++;

                return;
            }
        }

        public void ProcessItem(UserItem[] inventory)
        {
            for (int i = 0; i < ItemTaskCount.Count; i++)
            {
                var count = inventory.Where(item => item != null).
                    Where(item => item.Info == ItemTaskCount[i].Info.Item).
                    Aggregate<UserItem, int>(0, (current, item) => current + item.Count);

                ItemTaskCount[i].Count = count;
            }
        }

        public void ProcessFlag(bool[] Flags)
        {
            for (int i = 0; i < FlagTaskSet.Count; i++)
            {
                for (int j = 0; j < Flags.Length - 1000; j++)
                {
                    if (FlagTaskSet[i].Number != j || !Flags[j]) continue;

                    FlagTaskSet[i].State = Flags[j];
                    break;
                }
            }
        }

        #endregion

        #region Update Task Messages

        public void UpdateTasks()
        {
            TaskList = new List<string>();

            UpdateKillTasks();
            UpdateItemTasks();
            UpdateFlagTasks();
            UpdateGotoTask();
        }

        public void UpdateKillTasks()
        {
            if(Info.KillMessage.Length > 0 && Info.KillTasks.Count > 0)
            {
                bool allComplete = true;
                for (int i = 0; i < KillTaskCount.Count; i++)
                {
                    if (KillTaskCount[i].Complete) continue;

                    allComplete = false;
                }

                TaskList.Add(string.Format("{0} {1}", Info.KillMessage, allComplete ? "(Completed)" : ""));
                return;
            }

            for (int i = 0; i < KillTaskCount.Count; i++)
            {
                var task = KillTaskCount[i].Info;
                if (task == null) continue;

                if (string.IsNullOrEmpty(task.Message))
                {
                    TaskList.Add(string.Format("狩猎 {0}: {1}/{2} {3}", task.Monster.GameName, KillTaskCount[i].Count,
                        task.Count, KillTaskCount[i].Complete ? "(完成)" : ""));
                }
                else
                {
                    TaskList.Add(string.Format("{0} {1}", task.Message, KillTaskCount[i].Complete ? "(√)" : ""));
                }
            }
        }

        public void UpdateItemTasks()
        {
            if (Info.ItemMessage.Length > 0 && Info.ItemTasks.Count > 0)
            {
                bool allComplete = true;
                for (int i = 0; i < ItemTaskCount.Count; i++)
                {
                    if (ItemTaskCount[i].Complete) continue;

                    allComplete = false;
                }

                TaskList.Add(string.Format("{0} {1}", Info.ItemMessage, allComplete ? "(Completed)" : ""));
                return;
            }

            for (int i = 0; i < ItemTaskCount.Count; i++)
            {
                var task = ItemTaskCount[i].Info;
                if (task == null) continue;

                if (string.IsNullOrEmpty(task.Message))
                {
                    TaskList.Add(string.Format("收集 {0}: {1}/{2} {3}", task.Item.FriendlyName, ItemTaskCount[i].Count,
                        task.Count, ItemTaskCount[i].Complete ? "(完成)" : ""));
                }
                else
                {
                    TaskList.Add(string.Format("{0} {1}", task.Message, ItemTaskCount[i].Complete ? "(√)" : ""));
                }
            }
        }

        public void UpdateFlagTasks()
        {
            if (Info.FlagMessage.Length > 0)
            {
                bool allComplete = true;
                for (int i = 0; i < FlagTaskSet.Count; i++)
                {
                    if (FlagTaskSet[i].State) continue;

                    allComplete = false;
                }

                TaskList.Add(string.Format("{0} {1}", Info.FlagMessage, allComplete ? "(Completed)" : ""));
                return;
            }

            for (int i = 0; i < FlagTaskSet.Count; i++)
            {
                var task = FlagTaskSet[i].Info;
                if (task == null) continue;

                if (string.IsNullOrEmpty(task.Message))
                {
                    TaskList.Add(string.Format("激活任务标记 {0} {1}", task.Number, FlagTaskSet[i].Complete ? "(完成)" : ""));
                }
                else
                {
                    TaskList.Add(string.Format("{0} {1}", task.Message, FlagTaskSet[i].Complete ? "(√)" : ""));
                }
            }
        }

        public void UpdateGotoTask()
        {
            if (Info.GotoMessage.Length <= 0 || !Completed) return;

            TaskList.Add(Info.GotoMessage);
        }

        #endregion

        #region Optional Functions

        public bool TryRebindSnapshot(PlayerObject owner, bool recalcProgress, bool resetTimer, out string error)
        {
            error = string.Empty;

            if (owner == null)
            {
                error = "owner 不能为空。";
                return false;
            }

            Owner = owner;

            var sourceInfo = Envir.QuestInfoList.FirstOrDefault(e => e.Index == Index);
            if (sourceInfo == null)
            {
                error = $"未找到任务定义：Index={Index}";
                return false;
            }

            var snapshot = sourceInfo.CreateSnapshot();
            Info = snapshot;

            var previousKillTaskCount = KillTaskCount;
            var previousItemTaskCount = ItemTaskCount;
            var previousFlagTaskSet = FlagTaskSet;

            KillTaskCount = new List<QuestKillTaskProgress>(snapshot.KillTasks.Count);
            for (var i = 0; i < snapshot.KillTasks.Count; i++)
            {
                var task = snapshot.KillTasks[i];
                var prev = previousKillTaskCount.FirstOrDefault(x => x.MonsterID == task.Monster.Index);

                KillTaskCount.Add(new QuestKillTaskProgress
                {
                    MonsterID = task.Monster.Index,
                    Count = prev?.Count ?? 0,
                    Info = task
                });
            }

            ItemTaskCount = new List<QuestItemTaskProgress>(snapshot.ItemTasks.Count);
            for (var i = 0; i < snapshot.ItemTasks.Count; i++)
            {
                var task = snapshot.ItemTasks[i];
                var prev = previousItemTaskCount.FirstOrDefault(x => x.ItemID == task.Item.Index);

                ItemTaskCount.Add(new QuestItemTaskProgress
                {
                    ItemID = task.Item.Index,
                    Count = prev?.Count ?? 0,
                    Info = task
                });
            }

            FlagTaskSet = new List<QuestFlagTaskProgress>(snapshot.FlagTasks.Count);
            for (var i = 0; i < snapshot.FlagTasks.Count; i++)
            {
                var task = snapshot.FlagTasks[i];
                var prev = previousFlagTaskSet.FirstOrDefault(x => x.Number == task.Number);

                FlagTaskSet.Add(new QuestFlagTaskProgress
                {
                    Number = task.Number,
                    State = prev?.State ?? false,
                    Info = task
                });
            }

            if (recalcProgress)
            {
                ProcessItem(owner.Info.Inventory);
                ProcessFlag(owner.Info.Flags);
            }

            if (resetTimer)
            {
                Owner.ExpireTimer($"Quest-{Index}");

                for (var i = 0; i < Owner.ActionList.Count; i++)
                {
                    var action = Owner.ActionList[i];
                    if (action.Type != DelayedType.Quest) continue;
                    if (action.Params == null || action.Params.Length < 2) continue;
                    if (!ReferenceEquals(action.Params[0], this)) continue;
                    if (action.Params[1] is not QuestAction questAction) continue;
                    if (questAction != QuestAction.TimeExpired) continue;

                    action.FlaggedToRemove = true;
                }

                if (!Completed)
                {
                    SetTimer();
                }
            }

            UpdateTasks();
            return true;
        }

        public void SetTimer()
        {
            if (Owner == null)
            {
                return;
            }

            if (Info.TimeLimitInSeconds > 0)
            {
                var secondsSinceStarted = (int)(Envir.Now - StartDateTime).TotalSeconds;

                var remainingSeconds = Info.TimeLimitInSeconds - secondsSinceStarted;

                if (remainingSeconds > 0)
                {
                    Owner.SetTimer($"Quest-{Index}", remainingSeconds, 1);
                }

                DelayedAction action = new DelayedAction(DelayedType.Quest, Envir.Time + (remainingSeconds * 1000), this, QuestAction.TimeExpired, true);
                Owner.ActionList.Add(action);
            }
        }

        public void RemoveTimer()
        {
            if (Owner == null)
            {
                return;
            }

            if (Info.TimeLimitInSeconds > 0)
            {
                Owner.ExpireTimer($"Quest-{Index}");
            }
        }

        #endregion

        public ClientQuestProgress CreateClientQuestProgress()
        {
            return new ClientQuestProgress
            {
                Id = Index,
                TaskList = TaskList,
                Taken = Taken,
                Completed = Completed,
                New = New
            };
        }
    }

    public class QuestKillTaskProgress
    {
        public int MonsterID { get; set; }
        public int Count { get; set; }
        public QuestKillTask Info { get; set; }

        public bool Complete { get { return Info != null && Count >= Info.Count; } }
    }

    public class QuestItemTaskProgress
    {
        public int ItemID { get; set; }
        public int Count { get; set; }
        public QuestItemTask Info { get; set; }

        public bool Complete { get { return Info != null && Count >= Info.Count; } }
    }

    public class QuestFlagTaskProgress
    {
        public int Number { get; set; }
        public bool State { get; set; }
        public QuestFlagTask Info { get; set; }

        public bool Complete { get { return Info != null && State == true; } }
    }
}
