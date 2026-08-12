using Server.MirObjects;
using System.Text.RegularExpressions;
using Server.MirEnvir;
using Server.Scripting;

namespace Server.MirDatabase
{
    public class QuestInfo
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        protected static Envir EditEnvir
        {
            get { return Envir.Edit; }
        }

        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public int Index;

        public uint NpcIndex;
        public NPCInfo NpcInfo;

        private uint _finishNpcIndex;

        public uint FinishNpcIndex
        {
            get { return _finishNpcIndex == 0 ? NpcIndex : _finishNpcIndex; }
            set { _finishNpcIndex = value; }
        }

        public NPCObject FinishNPC
        {
            get
            {
                return Envir.NPCs.Single(x => x.ObjectID == FinishNpcIndex);
            }
        }

        public string 
            Name = string.Empty, 
            Group = string.Empty, 
            FileName = string.Empty, 
            GotoMessage = string.Empty, 
            KillMessage = string.Empty, 
            ItemMessage = string.Empty,
            FlagMessage = string.Empty;

        public List<string> Description = new List<string>();
        public List<string> TaskDescription = new List<string>();
        public List<string> ReturnDescription = new List<string>();
        public List<string> CompletionDescription = new List<string>(); 

        public int RequiredMinLevel, RequiredMaxLevel, RequiredQuest;
        public RequiredClass RequiredClass = RequiredClass.全职业;

        public QuestType Type;

        public int TimeLimitInSeconds = 0;

        public List<QuestItemTask> CarryItems = new List<QuestItemTask>(); 

        public List<QuestKillTask> KillTasks = new List<QuestKillTask>();
        public List<QuestItemTask> ItemTasks = new List<QuestItemTask>();
        public List<QuestFlagTask> FlagTasks = new List<QuestFlagTask>();
        //TODO: ZoneTasks
        //TODO: EscortTasks

        public uint GoldReward;
        public uint ExpReward;
        public uint CreditReward;
        public List<QuestItemReward> FixedRewards = new List<QuestItemReward>();
        public List<QuestItemReward> SelectRewards = new List<QuestItemReward>();

        internal Func<QuestAcceptContext, QuestConditionResult> AcceptCondition;
        internal Func<QuestFinishContext, QuestConditionResult> FinishCondition;
        internal Func<QuestRewardContext, QuestRewardOverride> RewardResolver;
        internal Action<QuestAcceptedContext> OnAccepted;
        internal Action<QuestFinishContext> OnFinished;

        private bool _acceptConditionErrorLogged;
        private bool _finishConditionErrorLogged;
        private bool _rewardResolverErrorLogged;
        private bool _onAcceptedErrorLogged;
        private bool _onFinishedErrorLogged;

        private Regex _regexMessage = new Regex("\"([^\"]*)\"");


        public QuestInfo() { }

        public QuestInfo(BinaryReader reader)
        {
            Index = reader.ReadInt32();
            Name = reader.ReadString();
            Group = reader.ReadString();
            FileName = reader.ReadString();
            RequiredMinLevel = reader.ReadInt32();

            RequiredMaxLevel = reader.ReadInt32();
            if (RequiredMaxLevel == 0) RequiredMaxLevel = ushort.MaxValue;

            RequiredQuest = reader.ReadInt32();
            RequiredClass = (RequiredClass)reader.ReadByte();
            Type = (QuestType)reader.ReadByte();
            GotoMessage = reader.ReadString();
            KillMessage = reader.ReadString();
            ItemMessage = reader.ReadString();
            FlagMessage = reader.ReadString();

            if (Envir.LoadVersion > 90)
            {
                TimeLimitInSeconds = reader.ReadInt32();
            }

            LoadInfo();
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(Name);
            writer.Write(Group);
            writer.Write(FileName);
            writer.Write(RequiredMinLevel);
            writer.Write(RequiredMaxLevel);
            writer.Write(RequiredQuest);
            writer.Write((byte)RequiredClass);
            writer.Write((byte)Type);
            writer.Write(GotoMessage);
            writer.Write(KillMessage);
            writer.Write(ItemMessage);
            writer.Write(FlagMessage);
            writer.Write(TimeLimitInSeconds);
        }

        public void LoadInfo(bool clear = false)
        {
            if (clear) ClearInfo();

            var csharpScriptsEnabled = Settings.CSharpScriptsEnabled;
            var scriptsRuntimeActive = csharpScriptsEnabled && Envir.CSharpScripts.Enabled;
            var key = $"Quests/{FileName}";
            var allowCSharp = scriptsRuntimeActive && ScriptDispatchPolicy.ShouldTryCSharp(key);

            if (allowCSharp)
            {
                var definition = Envir.QuestProvider?.GetByKey(key);
                if (definition == null)
                {
                    // 启动阶段可能发生 Provider 尚未准备好（例如脚本系统先加载、DB 后加载）。
                    // 兜底：直接从当前脚本注册表取定义，避免无意义回落到 txt 并刷 File Not Found。
                    Envir.CSharpScripts?.CurrentRegistry?.Quests?.TryGet(key, out definition);
                }

                if (definition != null)
                {
                    if (TryApplyDefinition(definition, out var error))
                        return;

                    MessageQueue.Enqueue($"[Scripts] Quests 应用失败：{key} {error}");
                }
            }

            if (scriptsRuntimeActive && !Server.Scripting.TxtFallbackPolicy.ShouldFallbackToTxt(key))
            {
                if (Settings.TxtScriptsLogDispatch)
                {
                    var csharpState = Envir.CSharpScripts.Enabled ? $"v{Envir.CSharpScripts.Version}, handlers={Envir.CSharpScripts.LastRegisteredHandlerCount}" : $"不可用: {Envir.CSharpScripts.LastError}";
                    MessageQueue.Enqueue($"[Scripts][Load] Quests {FileName} -> 阻止回落TXT（key={key}，allowCSharp={allowCSharp}，C#={csharpState}）");
                }

                return;
            }

            // 最终目标：抛弃 Envir/Quests/*.txt。
            // 说明：当启用 C# 脚本时（哪怕脚本运行时尚未启动/尚未加载完成），这里也不再回落到磁盘 txt，避免刷 File Not Found。
            if (csharpScriptsEnabled)
            {
                if (Settings.TxtScriptsLogDispatch && !scriptsRuntimeActive)
                {
                    MessageQueue.Enqueue($"[Scripts][Load] Quests {FileName} -> 脚本运行时未就绪，已跳过加载（key={key}）");
                }

                return;
            }

            // 已移除磁盘 Envir/Quests/*.txt 读取：Quests 必须由 C# 脚本定义（QuestProvider/Registry）提供。
            return;
        }

        public void ClearInfo()
        {
            Description = new List<string>();
            TaskDescription = new List<string>();
            ReturnDescription = new List<string>();
            CompletionDescription = new List<string>();

            CarryItems = new List<QuestItemTask>();
            KillTasks = new List<QuestKillTask>();
            ItemTasks = new List<QuestItemTask>();
            FlagTasks = new List<QuestFlagTask>();

            FixedRewards = new List<QuestItemReward>();
            SelectRewards = new List<QuestItemReward>();
            ExpReward = 0;
            GoldReward = 0;
            CreditReward = 0;

            AcceptCondition = null;
            FinishCondition = null;
            RewardResolver = null;
            OnAccepted = null;
            OnFinished = null;

            _acceptConditionErrorLogged = false;
            _finishConditionErrorLogged = false;
            _rewardResolverErrorLogged = false;
            _onAcceptedErrorLogged = false;
            _onFinishedErrorLogged = false;
        }

        public void ParseFile(List<string> lines)
        {
            const string
                descriptionCollectKey = "[@DESCRIPTION]",
                descriptionTaskKey = "[@TASKDESCRIPTION]",
                descriptionReturnKey = "[@RETURNDESCRIPTION]",
                descriptionCompletionKey = "[@COMPLETION]",
                carryItemsKey = "[@CARRYITEMS]",
                killTasksKey = "[@KILLTASKS]",
                itemTasksKey = "[@ITEMTASKS]",
                flagTasksKey = "[@FLAGTASKS]",
                fixedRewardsKey = "[@FIXEDREWARDS]",
                selectRewardsKey = "[@SELECTREWARDS]",
                expRewardKey = "[@EXPREWARD]",
                goldRewardKey = "[@GOLDREWARD]",
                creditRewardKey = "[@CREDITREWARD]";

            List<string> headers = new List<string> 
            { 
                descriptionCollectKey, descriptionTaskKey, descriptionCompletionKey,
                carryItemsKey, killTasksKey, itemTasksKey, flagTasksKey,
                fixedRewardsKey, selectRewardsKey, expRewardKey, goldRewardKey, creditRewardKey, descriptionReturnKey
            };

            int currentHeader = 0;

            while (currentHeader < headers.Count)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].ToUpper();

                    if (line != headers[currentHeader].ToUpper()) continue;

                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        string innerLine = lines[j];

                        if (innerLine.StartsWith("[")) break;
                        if (string.IsNullOrEmpty(lines[j])) continue;

                        switch (line)
                        {
                            case descriptionCollectKey:
                                Description.Add(innerLine);
                                break;
                            case descriptionTaskKey:
                                TaskDescription.Add(innerLine);
                                break;
                            case descriptionReturnKey:
                                ReturnDescription.Add(innerLine);
                                break;
                            case descriptionCompletionKey:
                                CompletionDescription.Add(innerLine);
                                break;
                            case carryItemsKey:
                                QuestItemTask t = ParseItem(innerLine);
                                if (t != null) CarryItems.Add(t);
                                break;
                            case killTasksKey:
                                QuestKillTask t1 = ParseKill(innerLine);
                                if(t1 != null) KillTasks.Add(t1);
                                break;
                            case itemTasksKey:
                                QuestItemTask t2 = ParseItem(innerLine);
                                if (t2 != null) ItemTasks.Add(t2);
                                break;
                            case flagTasksKey:
                                QuestFlagTask t3 = ParseFlag(innerLine);
                                if (t3 != null) FlagTasks.Add(t3);
                                break;
                            case fixedRewardsKey:
                                {
                                    ParseReward(FixedRewards, innerLine);
                                    break;
                                }
                            case selectRewardsKey:
                                {
                                    ParseReward(SelectRewards, innerLine);
                                    break;
                                }
                            case expRewardKey:
                                uint.TryParse(innerLine, out ExpReward);
                                break;
                            case goldRewardKey:
                                uint.TryParse(innerLine, out GoldReward);
                                break;
                            case creditRewardKey:
                                uint.TryParse(innerLine, out CreditReward);
                                break;
                        }
                    }
                }

                currentHeader++;
            }
        }

        public void ParseReward(List<QuestItemReward> list, string line)
        {
            if (line.Length < 1) return;

            string[] split = line.Split(' ');
            ushort count = 1;

            if (split.Length > 1) ushort.TryParse(split[1], out count);

            ItemInfo mInfo = Envir.GetItemInfo(split[0]);

            if (mInfo == null)
            {
                mInfo = Envir.GetItemInfo(split[0] + "(M)");
                if (mInfo != null) list.Add(new QuestItemReward() { Item = mInfo, Count = count });

                mInfo = Envir.GetItemInfo(split[0] + "(F)");
                if (mInfo != null) list.Add(new QuestItemReward() { Item = mInfo, Count = count });
            }
            else
            {
                list.Add(new QuestItemReward() { Item = mInfo, Count = count });
            }
        }

        public QuestKillTask ParseKill(string line)
        {
            if (line.Length < 1) return null;

            string[] split = line.Split(' ');
            int count = 1;
            string message = "";

            MonsterInfo mInfo = Envir.GetMonsterInfo(split[0]);
            if (split.Length > 1) int.TryParse(split[1], out count);

            var match = _regexMessage.Match(line);
            if (match.Success)
            {
                message = match.Groups[1].Captures[0].Value;
            }

            return mInfo == null ? null : new QuestKillTask() { Monster = mInfo, Count = count, Message = message };
        }

        public QuestItemTask ParseItem(string line)
        {
            if (line.Length < 1) return null;

            string[] split = line.Split(' ');
            ushort count = 1;
            string message = "";

            ItemInfo mInfo = Envir.GetItemInfo(split[0]);
            if (split.Length > 1) ushort.TryParse(split[1], out count);

            var match = _regexMessage.Match(line);
            if (match.Success)
            {
                message = match.Groups[1].Captures[0].Value;
            }
            //if (mInfo.StackSize <= 1)
            //{
            //    //recursively add item if cant stack???
            //}

            return mInfo == null ? null : new QuestItemTask { Item = mInfo, Count = count, Message = message };
        }

        public QuestFlagTask ParseFlag(string line)
        {
            if (line.Length < 1) return null;

            string[] split = line.Split(' ');

            int number = -1;
            string message = "";

            int.TryParse(split[0], out number);

            if (number < 0 || number > Globals.FlagIndexCount - 1000) return null;

            var match = _regexMessage.Match(line);
            if (match.Success)
            {
                message = match.Groups[1].Captures[0].Value;
            }

            return new QuestFlagTask { Number = number, Message = message };
        }

        public bool TryApplyDefinition(QuestDefinition definition, out string error)
        {
            error = string.Empty;

            if (definition == null)
            {
                error = "definition 不能为空。";
                return false;
            }

            if (!LogicKey.TryNormalize($"Quests/{FileName}", out var expectedKey))
            {
                error = $"QuestInfo.FileName 无效：{FileName}";
                return false;
            }

            if (!string.Equals(definition.Key, expectedKey, StringComparison.Ordinal))
            {
                error = $"任务定义 Key 与 QuestInfo.FileName 不匹配：expected={expectedKey} actual={definition.Key}";
                return false;
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            var description = new List<string>(definition.Description);
            var taskDescription = new List<string>(definition.TaskDescription);
            var returnDescription = new List<string>(definition.ReturnDescription);
            var completionDescription = new List<string>(definition.CompletionDescription);

            var carryItems = new List<QuestItemTask>(definition.CarryItems.Count);
            var killTasks = new List<QuestKillTask>(definition.KillTasks.Count);
            var itemTasks = new List<QuestItemTask>(definition.ItemTasks.Count);
            var flagTasks = new List<QuestFlagTask>(definition.FlagTasks.Count);

            var fixedRewards = new List<QuestItemReward>(definition.FixedRewards.Count);
            var selectRewards = new List<QuestItemReward>(definition.SelectRewards.Count);

            for (var i = 0; i < definition.CarryItems.Count; i++)
            {
                var t = definition.CarryItems[i];
                if (string.IsNullOrWhiteSpace(t.ItemName))
                {
                    errors.Add("CarryItems 存在空 ItemName。");
                    continue;
                }

                if (!string.IsNullOrEmpty(t.Message) && t.Message.Contains("\""))
                {
                    errors.Add($"CarryItems 的 Message 不能包含引号：{t.ItemName}");
                    continue;
                }

                var line = t.ItemName.Trim();
                if (t.Count > 1) line += " " + t.Count;
                if (!string.IsNullOrEmpty(t.Message)) line += " \"" + t.Message + "\"";

                var parsed = ParseItem(line);
                if (parsed == null)
                {
                    // legacy txt 行为：找不到物品则跳过该条，而不是让整个任务失败。
                    warnings.Add($"CarryItems 找不到物品：{t.ItemName}");
                    continue;
                }

                carryItems.Add(parsed);
            }

            for (var i = 0; i < definition.KillTasks.Count; i++)
            {
                var t = definition.KillTasks[i];
                if (string.IsNullOrWhiteSpace(t.MonsterName))
                {
                    errors.Add("KillTasks 存在空 MonsterName。");
                    continue;
                }

                if (!string.IsNullOrEmpty(t.Message) && t.Message.Contains("\""))
                {
                    errors.Add($"KillTasks 的 Message 不能包含引号：{t.MonsterName}");
                    continue;
                }

                var line = t.MonsterName.Trim();
                if (t.Count > 1) line += " " + t.Count;
                if (!string.IsNullOrEmpty(t.Message)) line += " \"" + t.Message + "\"";

                var parsed = ParseKill(line);
                if (parsed == null)
                {
                    // legacy txt 行为：找不到怪物则跳过该条，而不是让整个任务失败。
                    warnings.Add($"KillTasks 找不到怪物：{t.MonsterName}");
                    continue;
                }

                killTasks.Add(parsed);
            }

            for (var i = 0; i < definition.ItemTasks.Count; i++)
            {
                var t = definition.ItemTasks[i];
                if (string.IsNullOrWhiteSpace(t.ItemName))
                {
                    errors.Add("ItemTasks 存在空 ItemName。");
                    continue;
                }

                if (!string.IsNullOrEmpty(t.Message) && t.Message.Contains("\""))
                {
                    errors.Add($"ItemTasks 的 Message 不能包含引号：{t.ItemName}");
                    continue;
                }

                var line = t.ItemName.Trim();
                if (t.Count > 1) line += " " + t.Count;
                if (!string.IsNullOrEmpty(t.Message)) line += " \"" + t.Message + "\"";

                var parsed = ParseItem(line);
                if (parsed == null)
                {
                    // legacy txt 行为：找不到物品则跳过该条，而不是让整个任务失败。
                    warnings.Add($"ItemTasks 找不到物品：{t.ItemName}");
                    continue;
                }

                itemTasks.Add(parsed);
            }

            for (var i = 0; i < definition.FlagTasks.Count; i++)
            {
                var t = definition.FlagTasks[i];
                if (!string.IsNullOrEmpty(t.Message) && t.Message.Contains("\""))
                {
                    errors.Add($"FlagTasks 的 Message 不能包含引号：{t.Number}");
                    continue;
                }

                var line = t.Number.ToString();
                if (!string.IsNullOrEmpty(t.Message)) line += " \"" + t.Message + "\"";

                var parsed = ParseFlag(line);
                if (parsed == null)
                {
                    errors.Add($"FlagTasks 无效 Flag：{t.Number}");
                    continue;
                }

                flagTasks.Add(parsed);
            }

            for (var i = 0; i < definition.FixedRewards.Count; i++)
            {
                var r = definition.FixedRewards[i];
                if (string.IsNullOrWhiteSpace(r.ItemName))
                {
                    errors.Add("FixedRewards 存在空 ItemName。");
                    continue;
                }

                var line = r.ItemName.Trim();
                if (r.Count > 1) line += " " + r.Count;

                var before = fixedRewards.Count;
                ParseReward(fixedRewards, line);
                if (fixedRewards.Count == before)
                    // legacy txt 行为：找不到物品则跳过该条，而不是让整个任务失败。
                    warnings.Add($"FixedRewards 找不到物品：{r.ItemName}");
            }

            for (var i = 0; i < definition.SelectRewards.Count; i++)
            {
                var r = definition.SelectRewards[i];
                if (string.IsNullOrWhiteSpace(r.ItemName))
                {
                    errors.Add("SelectRewards 存在空 ItemName。");
                    continue;
                }

                var line = r.ItemName.Trim();
                if (r.Count > 1) line += " " + r.Count;

                var before = selectRewards.Count;
                ParseReward(selectRewards, line);
                if (selectRewards.Count == before)
                    // legacy txt 行为：找不到物品则跳过该条，而不是让整个任务失败。
                    warnings.Add($"SelectRewards 找不到物品：{r.ItemName}");
            }

            if (errors.Count > 0)
            {
                error = string.Join("; ", errors);
                return false;
            }

            if (warnings.Count > 0 && Settings.TxtScriptsLogDispatch)
            {
                MessageQueue.Enqueue($"[Scripts][Load] Quests {definition.Key} -> 跳过 {warnings.Count} 条缺失引用：{string.Join("; ", warnings)}");
            }

            Description = description;
            TaskDescription = taskDescription;
            ReturnDescription = returnDescription;
            CompletionDescription = completionDescription;

            CarryItems = carryItems;
            KillTasks = killTasks;
            ItemTasks = itemTasks;
            FlagTasks = flagTasks;

            FixedRewards = fixedRewards;
            SelectRewards = selectRewards;

            ExpReward = definition.ExpReward;
            GoldReward = definition.GoldReward;
            CreditReward = definition.CreditReward;

            if (definition.TimeLimitInSeconds.HasValue)
                TimeLimitInSeconds = definition.TimeLimitInSeconds.Value;

            AcceptCondition = definition.AcceptCondition;
            FinishCondition = definition.FinishCondition;
            RewardResolver = definition.RewardResolver;
            OnAccepted = definition.OnAccepted;
            OnFinished = definition.OnFinished;

            _acceptConditionErrorLogged = false;
            _finishConditionErrorLogged = false;
            _rewardResolverErrorLogged = false;
            _onAcceptedErrorLogged = false;
            _onFinishedErrorLogged = false;

            return true;
        }

        internal QuestInfo CreateSnapshot()
        {
            var snapshot = new QuestInfo
            {
                Index = Index,

                NpcIndex = NpcIndex,
                NpcInfo = NpcInfo,
                _finishNpcIndex = _finishNpcIndex,

                Name = Name,
                Group = Group,
                FileName = FileName,
                GotoMessage = GotoMessage,
                KillMessage = KillMessage,
                ItemMessage = ItemMessage,
                FlagMessage = FlagMessage,

                RequiredMinLevel = RequiredMinLevel,
                RequiredMaxLevel = RequiredMaxLevel,
                RequiredQuest = RequiredQuest,
                RequiredClass = RequiredClass,
                Type = Type,

                TimeLimitInSeconds = TimeLimitInSeconds,

                GoldReward = GoldReward,
                ExpReward = ExpReward,
                CreditReward = CreditReward,
            };

            snapshot.Description = new List<string>(Description);
            snapshot.TaskDescription = new List<string>(TaskDescription);
            snapshot.ReturnDescription = new List<string>(ReturnDescription);
            snapshot.CompletionDescription = new List<string>(CompletionDescription);

            snapshot.CarryItems = new List<QuestItemTask>(CarryItems.Count);
            for (var i = 0; i < CarryItems.Count; i++)
            {
                var task = CarryItems[i];
                snapshot.CarryItems.Add(new QuestItemTask { Item = task.Item, Count = task.Count, Message = task.Message });
            }

            snapshot.KillTasks = new List<QuestKillTask>(KillTasks.Count);
            for (var i = 0; i < KillTasks.Count; i++)
            {
                var task = KillTasks[i];
                snapshot.KillTasks.Add(new QuestKillTask { Monster = task.Monster, Count = task.Count, Message = task.Message });
            }

            snapshot.ItemTasks = new List<QuestItemTask>(ItemTasks.Count);
            for (var i = 0; i < ItemTasks.Count; i++)
            {
                var task = ItemTasks[i];
                snapshot.ItemTasks.Add(new QuestItemTask { Item = task.Item, Count = task.Count, Message = task.Message });
            }

            snapshot.FlagTasks = new List<QuestFlagTask>(FlagTasks.Count);
            for (var i = 0; i < FlagTasks.Count; i++)
            {
                var task = FlagTasks[i];
                snapshot.FlagTasks.Add(new QuestFlagTask { Number = task.Number, Message = task.Message });
            }

            snapshot.FixedRewards = new List<QuestItemReward>(FixedRewards.Count);
            for (var i = 0; i < FixedRewards.Count; i++)
            {
                var reward = FixedRewards[i];
                snapshot.FixedRewards.Add(new QuestItemReward { Item = reward.Item, Count = reward.Count });
            }

            snapshot.SelectRewards = new List<QuestItemReward>(SelectRewards.Count);
            for (var i = 0; i < SelectRewards.Count; i++)
            {
                var reward = SelectRewards[i];
                snapshot.SelectRewards.Add(new QuestItemReward { Item = reward.Item, Count = reward.Count });
            }

            snapshot.AcceptCondition = AcceptCondition;
            snapshot.FinishCondition = FinishCondition;
            snapshot.RewardResolver = RewardResolver;
            snapshot.OnAccepted = OnAccepted;
            snapshot.OnFinished = OnFinished;

            return snapshot;
        }

        internal bool TryCheckAcceptCondition(PlayerObject player, out string failMessage)
        {
            failMessage = string.Empty;

            if (AcceptCondition == null) return true;

            try
            {
                var result = AcceptCondition(new QuestAcceptContext(player, this));
                if (result.Allowed) return true;

                failMessage = string.IsNullOrWhiteSpace(result.FailMessage) ? "无法接受任务" : result.FailMessage;
                return false;
            }
            catch (Exception ex)
            {
                failMessage = "无法接受任务";

                if (!_acceptConditionErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _acceptConditionErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest AcceptCondition 异常：index={Index} file={FileName} err={ex.Message}");
                }

                return false;
            }
        }

        internal bool TryCheckFinishCondition(PlayerObject player, QuestProgressInfo quest, int selectedItemIndex, out string failMessage)
        {
            failMessage = string.Empty;

            if (FinishCondition == null) return true;

            try
            {
                var result = FinishCondition(new QuestFinishContext(player, quest, selectedItemIndex));
                if (result.Allowed) return true;

                failMessage = string.IsNullOrWhiteSpace(result.FailMessage) ? "无法提交任务" : result.FailMessage;
                return false;
            }
            catch (Exception ex)
            {
                failMessage = "无法提交任务";

                if (!_finishConditionErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _finishConditionErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest FinishCondition 异常：index={Index} file={FileName} err={ex.Message}");
                }

                return false;
            }
        }

        internal void ResolveRewards(PlayerObject player, QuestProgressInfo quest, int selectedItemIndex, out uint gold, out uint exp, out uint credit, out List<QuestItemReward> fixedRewards, out List<QuestItemReward> selectRewards)
        {
            gold = GoldReward;
            exp = ExpReward;
            credit = CreditReward;
            fixedRewards = FixedRewards;
            selectRewards = SelectRewards;

            if (RewardResolver == null) return;

            QuestRewardOverride resolved;

            try
            {
                resolved = RewardResolver(new QuestRewardContext(player, quest, selectedItemIndex));
            }
            catch (Exception ex)
            {
                if (!_rewardResolverErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _rewardResolverErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest RewardResolver 异常：index={Index} file={FileName} err={ex.Message}");
                }

                return;
            }

            if (resolved == null) return;

            try
            {
                var newFixed = new List<QuestItemReward>(resolved.FixedRewards.Count);
                var newSelect = new List<QuestItemReward>(resolved.SelectRewards.Count);

                for (var i = 0; i < resolved.FixedRewards.Count; i++)
                {
                    var r = resolved.FixedRewards[i];
                    if (string.IsNullOrWhiteSpace(r.ItemName))
                        throw new InvalidOperationException("FixedRewards 存在空 ItemName。");

                    var line = r.ItemName.Trim();
                    if (r.Count > 1) line += " " + r.Count;

                    var before = newFixed.Count;
                    ParseReward(newFixed, line);

                    if (newFixed.Count == before)
                        throw new InvalidOperationException($"FixedRewards 找不到物品：{r.ItemName}");
                }

                for (var i = 0; i < resolved.SelectRewards.Count; i++)
                {
                    var r = resolved.SelectRewards[i];
                    if (string.IsNullOrWhiteSpace(r.ItemName))
                        throw new InvalidOperationException("SelectRewards 存在空 ItemName。");

                    var line = r.ItemName.Trim();
                    if (r.Count > 1) line += " " + r.Count;

                    var before = newSelect.Count;
                    ParseReward(newSelect, line);

                    if (newSelect.Count == before)
                        throw new InvalidOperationException($"SelectRewards 找不到物品：{r.ItemName}");
                }

                gold = resolved.GoldReward;
                exp = resolved.ExpReward;
                credit = resolved.CreditReward;
                fixedRewards = newFixed;
                selectRewards = newSelect;
            }
            catch (Exception ex)
            {
                if (!_rewardResolverErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _rewardResolverErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest RewardResolver 输出无效：index={Index} file={FileName} err={ex.Message}");
                }
            }
        }

        internal void TryInvokeOnAccepted(PlayerObject player, QuestProgressInfo quest)
        {
            if (OnAccepted == null) return;

            try
            {
                OnAccepted(new QuestAcceptedContext(player, quest));
            }
            catch (Exception ex)
            {
                if (!_onAcceptedErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _onAcceptedErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest OnAccepted 异常：index={Index} file={FileName} err={ex.Message}");
                }
            }
        }

        internal void TryInvokeOnFinished(PlayerObject player, QuestProgressInfo quest, int selectedItemIndex)
        {
            if (OnFinished == null) return;

            try
            {
                OnFinished(new QuestFinishContext(player, quest, selectedItemIndex));
            }
            catch (Exception ex)
            {
                if (!_onFinishedErrorLogged && Settings.CSharpScriptsLogDiagnostics)
                {
                    _onFinishedErrorLogged = true;
                    MessageQueue.Enqueue($"[Scripts] Quest OnFinished 异常：index={Index} file={FileName} err={ex.Message}");
                }
            }
        }

        public bool CanAccept(PlayerObject player)
        {
            if (RequiredMinLevel > player.Level || RequiredMaxLevel < player.Level)
                return false;

            if (RequiredQuest > 0 && !player.CompletedQuests.Contains(RequiredQuest))
                return false;

            switch (player.Class)
            {
                case MirClass.战士:
                    if (!RequiredClass.HasFlag(RequiredClass.战士))
                        return false;
                    break;
                case MirClass.法师:
                    if (!RequiredClass.HasFlag(RequiredClass.法师))
                        return false;
                    break;
                case MirClass.道士:
                    if (!RequiredClass.HasFlag(RequiredClass.道士))
                        return false;
                    break;
                case MirClass.刺客:
                    if (!RequiredClass.HasFlag(RequiredClass.刺客))
                        return false;
                    break;
                case MirClass.弓箭:
                    if (!RequiredClass.HasFlag(RequiredClass.弓箭))
                        return false;
                    break;
            }

            return true;
        }

        public ClientQuestInfo CreateClientQuestInfo()
        {
            return new ClientQuestInfo
            {
                Index = Index,
                NPCIndex = NpcIndex,
                FinishNPCIndex = FinishNpcIndex,
                Name = Name,
                Group = Group,
                Description = Description,
                TaskDescription = TaskDescription,
                ReturnDescription = ReturnDescription,
                CompletionDescription = CompletionDescription,
                MinLevelNeeded = RequiredMinLevel,
                MaxLevelNeeded = RequiredMaxLevel,
                ClassNeeded = RequiredClass,
                QuestNeeded = RequiredQuest,
                Type = Type,
                TimeLimitInSeconds = TimeLimitInSeconds,
                RewardGold = GoldReward,
                RewardExp = ExpReward,
                RewardCredit = CreditReward,
                RewardsFixedItem = FixedRewards,
                RewardsSelectItem = SelectRewards
            };
        }

        public static void FromText(string text)
        {
            string[] data = text.Split(new[] { ',' });

            if (data.Length < 10) return;

            QuestInfo info = new QuestInfo();

            info.Name = data[0];
            info.Group = data[1];

            byte temp;

            byte.TryParse(data[2], out temp);

            info.Type = (QuestType)temp;

            info.FileName = data[3];
            info.GotoMessage = data[4];
            info.KillMessage = data[5];
            info.ItemMessage = data[6];
            info.FlagMessage = data[7];

            int.TryParse(data[8], out info.RequiredMinLevel);
            int.TryParse(data[9], out info.RequiredMaxLevel);
            int.TryParse(data[10], out info.RequiredQuest);

            byte.TryParse(data[11], out temp);

            info.RequiredClass = (RequiredClass)temp;

            info.Index = ++EditEnvir.QuestIndex;
            EditEnvir.QuestInfoList.Add(info);
        }

        public string ToText()
        {
            return string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                Name, Group, (byte)Type, FileName, GotoMessage, KillMessage, ItemMessage, FlagMessage, RequiredMinLevel, RequiredMaxLevel, RequiredQuest, (byte)RequiredClass);
        }

        public override string ToString()
        {
            return string.Format("{0}:   {1}", Index, Name);
        }
    }

    public class QuestKillTask
    {
        public MonsterInfo Monster;
        public int Count;
        public string Message;
    }

    public class QuestItemTask
    {
        public ItemInfo Item;
        public ushort Count;
        public string Message;
    }

    public class QuestFlagTask
    {
        public int Number;
        public string Message;
    }
}
