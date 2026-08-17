using Server;
using Server.MirObjects;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.Scripting;
using Server.Scripting.Variables;
using System.Drawing;
using Xunit;

namespace Base05.Tests;

[Collection(nameof(PhysicalTextFileProviderCollection))]
public sealed class LingFengPlayerCommandTests
{
    [Fact]
    public void 翎风扩展字符串分隔按起始变量连续写入并返回分段数()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject { Info = new CharacterInfo { Name = "命格分隔人物" } };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "EXTRACTSTRINGEX | 七杀|破军|紫微 S11 N20");
            segment.ParseAct(segment.ActList, "TEXTSPLIT , 天魁,天机 S21 N21");
            segment.ParseAct(segment.ActList, "TEXTLENGTH 命格AB N22");
            segment.ParseAct(segment.ActList, "SETSTRINGBLANK S10 6 1");
            segment.ParseAct(segment.ActList, "SETSTRINGBLANK S31 170 0");
            segment.AddVariable(player, "S10", "命格");
            segment.AddVariable(player, "S31", "命格");

            Assert.True(segment.Check(player));
            Assert.Equal("七杀", segment.FindVariable(player, "%S11"));
            Assert.Equal("破军", segment.FindVariable(player, "%S12"));
            Assert.Equal("紫微", segment.FindVariable(player, "%S13"));
            Assert.Equal("3", segment.FindVariable(player, "%N20"));
            Assert.Equal("天魁", segment.FindVariable(player, "%S21"));
            Assert.Equal("天机", segment.FindVariable(player, "%S22"));
            Assert.Equal("2", segment.FindVariable(player, "%N21"));
            Assert.Equal("6", segment.FindVariable(player, "%N22"));
            Assert.Equal("命格  ", segment.FindVariable(player, "%S10"));
            string wideRecord = segment.FindVariable(player, "%S31");
            Assert.EndsWith("命格", wideRecord, StringComparison.Ordinal);
            Assert.Equal(168, wideRecord.Length);

            var valid = new TextFileDefinition("NPCs/长记录补齐")
                .AddLines(new[] { "[@MAIN]", "#ACT", "SETSTRINGBLANK S11 170 0" });
            Assert.Empty(TxtScriptSnapshotValidator.Validate(new SingleProvider(valid)));
            var excessive = new TextFileDefinition("NPCs/超限补齐")
                .AddLines(new[] { "[@MAIN]", "#ACT", "SETSTRINGBLANK S11 1025 0" });
            Assert.Contains(TxtScriptSnapshotValidator.Validate(new SingleProvider(excessive)),
                error => error.Contains("SETSTRINGBLANK", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风Close清空响应并结束服务端NPC会话()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                NPCObjectID = 981625,
                NPCScriptID = 981626,
                NPCSpeech = new List<string> { "命格旧页面" },
                Info = new CharacterInfo { Name = "命格关闭人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CLOSE");

            Assert.True(segment.Check(player));
            Assert.Empty(player.NPCSpeech);
            Assert.Equal(0U, player.NPCObjectID);
            Assert.Equal(0, player.NPCScriptID);
            Assert.Null(player.NPCPage);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风背包与英雄检测读取真实人物领域状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var heroInfo = new HeroInfo { Name = "命格英雄" };
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格英雄人物",
                    Inventory = new UserItem[3],
                    Heroes = new[] { heroInfo }
                }
            };
            player.Info.Inventory[0] = new UserItem(new ItemInfo { Name = "占位命格" });
            player.Hero = new HeroObject(heroInfo, player);

            var segment = Segment();
            segment.ParseCheck("CHECKBAGSIZE 2");
            segment.ParseCheck("CHECKBAGGAGE");
            segment.ParseCheck("CHECKHAVEHERO");
            segment.ParseCheck("CHECKHEROONLINE");
            Assert.True(segment.Check(player));

            player.Hero = null;
            var offline = Segment();
            offline.ParseCheck("CHECKHAVEHERO");
            offline.ParseCheck("NOT CHECKHEROONLINE");
            Assert.True(offline.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风技能命令按中文技能名分别调整普通与强化持久等级()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var magicInfo = new MagicInfo
        {
            Name = "命格剑术探针",
            Spell = Spell.Fencing
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MagicInfoList.Add(magicInfo);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格技能人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "ADDSKILL 命格剑术探针 1");
            segment.ParseAct(segment.ActList, "SKILLLEVEL 命格剑术探针 + 2");
            segment.ParseAct(segment.ActList, "SKILLLEVEL 命格剑术探针 - 1");
            segment.ParseAct(segment.ActList, "SKILLLEVEL 命格剑术探针 = 1 1");
            segment.ParseAct(segment.ActList, "SKILLLEVEL 命格剑术探针 + 2 1");

            Assert.True(segment.Check(player));
            UserMagic learned = Assert.Single(player.Info.Magics);
            Assert.Equal(magicInfo.Spell, learned.Spell);
            Assert.Equal(2, learned.Level);
            Assert.Equal(0, learned.Experience);

            var learnedChecks = Segment();
            learnedChecks.ParseCheck("CHECKMAGICNAME 命格剑术探针");
            learnedChecks.ParseCheck("CHECKSKILL 命格剑术探针 > 1");
            learnedChecks.ParseCheck("NOT CHECKSKILL 命格剑术探针 < 2");
            learnedChecks.ParseCheck("CHECKSKILL 命格剑术探针 = 3 1");
            Assert.True(learnedChecks.Check(player));
            Assert.Equal(3,
                player.Info.LingFengProgress.GetEnhancedSkillLevel(magicInfo.Spell));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredStore = new CharacterScriptVariableStore();
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredStore.Load(reader);
            Assert.Equal(3,
                new LingFengCharacterProgress(restoredStore)
                    .GetEnhancedSkillLevel(magicInfo.Spell));
        }
        finally
        {
            Envir.Main.MagicInfoList.Remove(magicInfo);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风删除技能与清除冷却按中文技能名修改真实技能状态并同步客户端()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var removableInfo = new MagicInfo
        {
            Name = "命格删除技能探针",
            Spell = Spell.Fencing
        };
        var cooldownInfo = new MagicInfo
        {
            Name = "命格冷却技能探针",
            Spell = Spell.Slaying,
            DelayBase = 8_000
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MagicInfoList.Add(removableInfo);
            Envir.Main.MagicInfoList.Add(cooldownInfo);
            var removable = new UserMagic(removableInfo.Spell);
            var cooldown = new UserMagic(cooldownInfo.Spell)
            {
                CastTime = Envir.Main.Time
            };
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格技能维护人物",
                    Magics = new List<UserMagic> { removable, cooldown }
                }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CLEARSKILLCD 命格冷却技能探针");
            segment.ParseAct(segment.ActList, "DELSKILL 命格删除技能探针");

            Assert.True(segment.Check(player));
            Assert.DoesNotContain(player.Info.Magics, magic => magic.Spell == removableInfo.Spell);
            Assert.Contains(player.Info.Magics, magic => magic.Spell == cooldownInfo.Spell);
            Assert.True(cooldown.CastTime + cooldown.GetDelay() <= Envir.Main.Time);
            Assert.Contains(player.Packets, packet => packet is ServerPackets.RemoveMagic);
            var cleared = Assert.Single(player.Packets.OfType<ServerPackets.MagicCooldownCleared>());
            Assert.Equal(player.ObjectID, cleared.ObjectID);
            Assert.Equal(cooldownInfo.Spell, cleared.Spell);
            var restored = Assert.IsType<ServerPackets.MagicCooldownCleared>(
                Packet.ReceivePacket(cleared.GetPacketBytes().ToArray(), out _));
            Assert.Equal(cleared.ObjectID, restored.ObjectID);
            Assert.Equal(cleared.Spell, restored.Spell);
        }
        finally
        {
            Envir.Main.MagicInfoList.Remove(removableInfo);
            Envir.Main.MagicInfoList.Remove(cooldownInfo);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风按名称和数量杀死人物宝宝不会误伤其他宝宝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        int oldMonsterCount = Envir.Main.MonsterCount;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格宝宝人物" }
            };
            var first = new FateMonster(new MonsterInfo { Name = "命格守卫" }) { Master = player };
            var second = new FateMonster(new MonsterInfo { Name = "命格守卫" }) { Master = player };
            var other = new FateMonster(new MonsterInfo { Name = "其他守卫" }) { Master = player };
            player.Pets.Add(first);
            player.Pets.Add(second);
            player.Pets.Add(other);
            var segment = Segment();
            segment.ParseAct(segment.ActList, "KILLCALLMOB 命格守卫 1");

            Assert.True(segment.Check(player));
            Assert.Equal(1, player.Pets.Count(pet => pet.Info.Name == "命格守卫" && pet.Dead));
            Assert.Equal(1, player.Pets.Count(pet => pet.Info.Name == "命格守卫" && !pet.Dead));
            Assert.False(other.Dead);
        }
        finally
        {
            Envir.Main.MonsterCount = oldMonsterCount;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格宝宝数量检测可忽略名称末尾数字且只统计存活宝宝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格宝宝检测人物" }
            };
            var first = new FateMonster(new MonsterInfo { Name = "骷髅1" }) { Master = player };
            var second = new FateMonster(new MonsterInfo { Name = "骷髅2" }) { Master = player };
            var other = new FateMonster(new MonsterInfo { Name = "神兽" }) { Master = player };
            player.Pets.Add(first);
            player.Pets.Add(second);
            player.Pets.Add(other);

            var segment = Segment();
            segment.ParseCheck("CHECKSLAVECOUNT = 2 骷髅 0");
            segment.ParseCheck("CHECKSLAVECOUNT = 0 骷髅 1");
            segment.ParseCheck("CHECKSLAVECOUNT = 3");

            Assert.True(segment.Check(player));
            Assert.Throws<InvalidDataException>(() =>
                segment.ParseCheck("CHECKSLAVECOUNT = 2 骷髅 2"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风召唤宝宝应用数量等级叛变时间与固定名称颜色()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldMultithreaded = Settings.Multithreaded;
        long oldTime = Envir.Main.Time;
        int oldMonsterCount = Envir.Main.MonsterCount;
        var map = WalkableMap(981630, "LF-FATE-RECALL", 10, 10);
        PlayerObject player = null;
        var monsterInfo = new MonsterInfo
        {
            Index = 981631,
            Name = "命格召唤兽",
            Stats = new Stats { [Stat.HP] = 100 }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.Multithreaded = false;
            Envir.Main.MapList.Add(map);
            Envir.Main.MonsterInfoList.Add(monsterInfo);
            player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格召唤人物" },
                CurrentMap = map,
                CurrentLocation = new Point(5, 5)
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "RECALLMOB 命格召唤兽 7 2 0 122 0 2");

            Assert.True(segment.Check(player));
            Assert.Equal(2, player.Pets.Count);
            Assert.All(player.Pets, pet =>
            {
                Assert.Same(player, pet.Master);
                Assert.Equal(7, pet.PetLevel);
                Assert.Equal(oldTime + 2 * Settings.Minute, pet.TameTime);
                Assert.Equal(map, pet.CurrentMap);
                Assert.False(pet.Dead);
            });
            Assert.True(LingFengLegacyPalette.TryGetColor(122, out Color expected));
            Assert.All(player.Pets, pet => Assert.Equal(expected, pet.NameColour));

            MonsterObject[] recalled = player.Pets.ToArray();
            var kill = Segment();
            kill.ParseAct(kill.ActList, "KILLSLAVE 1");
            Assert.True(kill.Check(player));
            Assert.All(recalled, pet =>
            {
                Assert.True(pet.Dead);
                Assert.Null(pet.Master);
                Assert.Null(pet.Node);
            });
            Assert.Empty(player.Pets);
        }
        finally
        {
            foreach (MonsterObject pet in player?.Pets.ToArray() ?? Array.Empty<MonsterObject>())
                pet.Die();
            Envir.Main.MapList.Remove(map);
            Envir.Main.MonsterInfoList.Remove(monsterInfo);
            Envir.Main.MonsterCount = oldMonsterCount;
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.Multithreaded = oldMultithreaded;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风自定义装备属性经真实命令生效刷新并完成共享二进制往返()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var weaponInfo = new ItemInfo
            {
                Index = 981632,
                Name = "命格属性剑",
                Type = ItemType.武器,
                Stats = new Stats
                {
                    [Stat.MinDC] = 10,
                    [Stat.MaxDC] = 20
                }
            };
            var weapon = new UserItem(weaponInfo) { UniqueID = 981633 };
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格装备人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.Info.Equipment[(int)EquipmentSlot.武器] = weapon;
            player.RefreshStats();
            int originalMinDc = player.Stats[Stat.MinDC];
            int originalMaxDc = player.Stats[Stat.MaxDC];
            player.Packets.Clear();

            var segment = Segment();
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMABIL 1 0 0 249");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMABIL 1 0 1 3");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMABIL 1 0 2 7");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMABIL 1 0 3 0");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMABIL 1 0 4 9");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMABIL 1 0 0 N30");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMABIL 1 0 1 <$STR(N31)>");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMABIL 1 0 2 N32");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMABIL 1 0 3 N33");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMABIL 1 0 4 N34");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMVALUEEX 1 0 = 12 34 56");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMVALUE 1 0 N1 N2");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMVALUEEX 1 0 N3 N4 N5 N6");
            segment.ParseAct(segment.ActList, "GETALLCUSTOMITEMVALUE 3 N7 N8");
            segment.ParseAct(segment.ActList, "SETITEMADDBYTE 1 2 255");
            segment.ParseAct(segment.ActList, "SETITEMADDINT 1 3 123456");
            segment.ParseAct(segment.ActList, "SETITEMADDTEXT 1 1 命格标记");
            segment.ParseAct(segment.ActList, "GETITEMADDBYTE 1 2 N9");
            segment.ParseAct(segment.ActList, "GETITEMADDINT 1 3 N10");
            segment.ParseAct(segment.ActList, "GETITEMADDTEXT 1 1 S11");
            segment.ParseAct(segment.ActList, "CHANGEITEMADDVALUE 1 0 + 5");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMTEXT 1 命格自定义属性");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMTEXTCOLOR 1 249");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBAR 1 0 0 1");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBAR 1 0 1 刀魂%p-%m：");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBAR 1 0 2 255");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBAR 1 0 3 15");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBAR 1 0 4 2");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBARVALUE 1 0 0 = 1000");
            segment.ParseAct(segment.ActList, "SETCUSTOMITEMPROGRESSBARVALUE 1 0 2 = 60");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMPROGRESSBARVALUE 1 0 0 N12");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMPROGRESSBARVALUE 1 0 1 N13");
            segment.ParseAct(segment.ActList, "GETCUSTOMITEMPROGRESSBARVALUE 1 0 2 N14");
            segment.ParseAct(segment.ActList, "SETITEMEFFECT 1 219 1");
            segment.ParseAct(segment.ActList, "CHANGEITEMNAMECOLOR 1 249");
            segment.ParseAct(segment.ActList, "SETNEWITEMVALUE 1 1 = 30");
            segment.ParseAct(segment.ActList, "SETNEWITEMVALUE 1 1 + 5");
            segment.ParseAct(segment.ActList, "SETITEMSTATE 1 1");
            segment.ParseAct(segment.ActList, "SETITEMSTATE 1 0 1");
            segment.ParseAct(segment.ActList, "SETITEMSTATE 1 7 1");
            segment.ParseAct(segment.ActList, "GETITEMFIELDVALUE 1 Shape N15");
            segment.ParseAct(segment.ActList, "GETITEMFIELDVALUE 1 Uelement1 N16");
            segment.ParseAct(segment.ActList, "GETITEMFIELDVALUE 1 Name S17");
            segment.ParseAct(segment.ActList, "UPDATEITEM 1");

            Assert.True(segment.Check(player));
            player.RefreshStats();
            Assert.Equal(originalMinDc + 12, player.Stats[Stat.MinDC]);
            Assert.Equal(originalMaxDc + 17, player.Stats[Stat.MaxDC]);
            Assert.Equal(5, weapon.AddedStats[Stat.MaxDC]);
            LingFengCustomItemAttribute attribute = weapon.GetLingFengCustomAttribute(0);
            Assert.Equal(249, attribute.Colour);
            Assert.Equal(3, attribute.Binding);
            Assert.Equal(7, attribute.DisplayOrder);
            Assert.Equal(0, attribute.Mode);
            Assert.Equal(9, attribute.Module);
            Assert.Equal("249", segment.FindVariable(player, "%N30"));
            Assert.Equal("3", segment.FindVariable(player, "%N31"));
            Assert.Equal("7", segment.FindVariable(player, "%N32"));
            Assert.Equal("0", segment.FindVariable(player, "%N33"));
            Assert.Equal("9", segment.FindVariable(player, "%N34"));
            Assert.Equal(12, attribute.Value1);
            Assert.Equal(34, attribute.Value2);
            Assert.Equal(56, attribute.Value3);
            IReadOnlyList<string> displayLines = weapon.GetLingFengCustomAttributeDisplayLines();
            Assert.Contains("命格自定义属性", displayLines);
            Assert.Contains("攻击: 12/34/56", displayLines);
            Assert.Contains("刀魂600-1000：600/1000", displayLines);
            Assert.Equal("12", segment.FindVariable(player, "%N1"));
            Assert.Equal("0", segment.FindVariable(player, "%N2"));
            Assert.Equal("0", segment.FindVariable(player, "%N3"));
            Assert.Equal("12", segment.FindVariable(player, "%N4"));
            Assert.Equal("34", segment.FindVariable(player, "%N5"));
            Assert.Equal("56", segment.FindVariable(player, "%N6"));
            Assert.Equal("12", segment.FindVariable(player, "%N7"));
            Assert.Equal("0", segment.FindVariable(player, "%N8"));
            Assert.Equal("255", segment.FindVariable(player, "%N9"));
            Assert.Equal("123456", segment.FindVariable(player, "%N10"));
            Assert.Equal("命格标记", segment.FindVariable(player, "%S11"));
            Assert.Equal("1000", segment.FindVariable(player, "%N12"));
            Assert.Equal("600", segment.FindVariable(player, "%N13"));
            Assert.Equal("60", segment.FindVariable(player, "%N14"));
            Assert.Equal(weaponInfo.Shape.ToString(), segment.FindVariable(player, "%N15"));
            Assert.Equal("35", segment.FindVariable(player, "%N16"));
            Assert.Equal("命格属性剑", segment.FindVariable(player, "%S17"));
            Assert.Equal((ushort)219, weapon.GetLingFengItemEffect(1));
            Assert.Equal((byte)249, weapon.LingFengNameColour);
            Assert.True(LingFengLegacyColorTable.TryGetRgb(
                weapon.LingFengNameColour, out byte nameRed, out byte nameGreen, out byte nameBlue));
            Assert.NotEqual((nameRed, nameGreen, nameBlue), ((byte)0, (byte)0, (byte)0));
            Assert.True(weapon.TryGetLingFengNewItemValue(1, out int newItemValue));
            Assert.Equal(35, newItemValue);
            Assert.Contains("翎风新增属性[1]: 35", displayLines);
            var customCheck = Segment();
            customCheck.ParseCheck("CHECKCUSTOMITEMVALUE 1 0 = 12");
            customCheck.ParseCheck("CHECKITEMADDVALUE 1 0 = 5");
            customCheck.ParseCheck("CHECKCUSTOMITEMPROGRESSBARVALUE 1 0 2 = 60");
            customCheck.ParseCheck("CHECKITEMNAMECOLOR 1 249");
            customCheck.ParseCheck("CHECKITEMBIND 1");
            customCheck.ParseCheck("CHECKITEMSTATE 1 0");
            customCheck.ParseCheck("CHECKITEMSTATE 1 7");
            Assert.True(customCheck.Check(player));
            Assert.Equal(player.Info.Index, weapon.SoulBoundId);
            Assert.True(weapon.HasBindingFlag(BindMode.DontDrop));
            Assert.True(weapon.LingFengCannotTakeOff);
            player.RemoveItem(MirGridType.Inventory, weapon.UniqueID, 0);
            Assert.Same(weapon, player.Info.Equipment[(int)EquipmentSlot.武器]);
            Assert.Null(player.Info.Inventory[0]);
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, weapon));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                weapon.Save(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = new UserItem(reader, Envir.Version, Envir.CustomVersion);
            LingFengCustomItemAttribute restoredAttribute = restored.GetLingFengCustomAttribute(0);
            Assert.Equal(attribute.Colour, restoredAttribute.Colour);
            Assert.Equal(attribute.Binding, restoredAttribute.Binding);
            Assert.Equal(attribute.DisplayOrder, restoredAttribute.DisplayOrder);
            Assert.Equal(attribute.Mode, restoredAttribute.Mode);
            Assert.Equal(attribute.Module, restoredAttribute.Module);
            Assert.Equal(attribute.Value1, restoredAttribute.Value1);
            Assert.Equal(attribute.Value2, restoredAttribute.Value2);
            Assert.Equal(attribute.Value3, restoredAttribute.Value3);
            Assert.Equal(weapon.SoulBoundId, restored.SoulBoundId);
            Assert.True(restored.HasBindingFlag(BindMode.DontDrop));
            Assert.True(restored.LingFengCannotTakeOff);
            Assert.Equal(5, restored.AddedStats[Stat.MaxDC]);
            Assert.True(restored.TryGetLingFengByteMark(2, out byte restoredByteMark));
            Assert.Equal((byte)255, restoredByteMark);
            Assert.True(restored.TryGetLingFengIntMark(3, out int restoredIntMark));
            Assert.Equal(123456, restoredIntMark);
            Assert.True(restored.TryGetLingFengTextMark(1, out string restoredTextMark));
            Assert.Equal("命格标记", restoredTextMark);
            Assert.True(restored.TryGetLingFengCustomProgressBarValue(0, 0, out int restoredMaximum));
            Assert.Equal(1000, restoredMaximum);
            Assert.True(restored.TryGetLingFengCustomProgressBarValue(0, 1, out int restoredCurrent));
            Assert.Equal(600, restoredCurrent);
            Assert.Equal((ushort)219, restored.GetLingFengItemEffect(1));
            Assert.Equal((byte)249, restored.LingFengNameColour);
            Assert.True(restored.TryGetLingFengNewItemValue(1, out int restoredNewItemValue));
            Assert.Equal(35, restoredNewItemValue);
            Assert.Contains("命格自定义属性", restored.GetLingFengCustomAttributeDisplayLines());

            var placementSyntax = Segment();
            placementSyntax.ParseAct(placementSyntax.ActList, "SETNEWITEMVALUE BoxItem3 25 = 1000");
            placementSyntax.ParseAct(placementSyntax.ActList, "H.SETNEWITEMVALUE 3 0 = 5");
            Assert.Equal(2, placementSyntax.ActList.Count);

            byte[] validCustomData = Convert.FromBase64String(
                weapon.SerializeLingFengCustomAttributes());
            var legacyCustomData = new UserItem(weaponInfo);
            Assert.True(legacyCustomData.TryDeserializeLingFengCustomAttributes(
                Convert.ToBase64String(validCustomData[..^1])));
            Assert.False(legacyCustomData.LingFengCannotTakeOff);
            string trailingGarbage = Convert.ToBase64String([.. validCustomData, 0x7F]);
            Assert.False(restored.TryDeserializeLingFengCustomAttributes(trailingGarbage));
            Assert.True(restored.TryGetLingFengByteMark(2, out byte preservedByteMark));
            Assert.Equal((byte)255, preservedByteMark);
            Assert.True(restored.TryGetLingFengCustomProgressBarValue(
                0, 1, out int preservedCurrent));
            Assert.Equal(600, preservedCurrent);
            Assert.Equal((ushort)219, restored.GetLingFengItemEffect(1));
            Assert.Equal((byte)249, restored.LingFengNameColour);
            Assert.Contains("命格自定义属性", restored.GetLingFengCustomAttributeDisplayLines());
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风物品名称颜色按实例区分并支持英雄与恢复默认颜色()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var heroInfo = new HeroInfo { Name = "命格颜色英雄" };
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格颜色人物",
                    Heroes = new[] { heroInfo }
                }
            };
            player.Hero = new HeroObject(heroInfo, player);
            var playerTalisman = new UserItem(new ItemInfo { Name = "人物命格", Type = ItemType.护身符 });
            var heroTalisman = new UserItem(new ItemInfo { Name = "英雄命格", Type = ItemType.护身符 });
            player.Info.Equipment[(int)EquipmentSlot.护身符] = playerTalisman;
            heroInfo.Equipment[(int)EquipmentSlot.护身符] = heroTalisman;

            var change = Segment();
            change.ParseAct(change.ActList, "CHANGEITEMNAMECOLOR 9 69");
            change.ParseAct(change.ActList, "H.CHANGEITEMNAMECOLOR 9 249");
            Assert.True(change.Check(player));
            Assert.Equal((byte)69, playerTalisman.LingFengNameColour);
            Assert.Equal((byte)249, heroTalisman.LingFengNameColour);

            var check = Segment();
            check.ParseCheck("CHECKITEMNAMECOLOR 9 69");
            check.ParseCheck("H.CHECKITEMNAMECOLOR 9 249");
            Assert.True(check.Check(player));

            var restore = Segment();
            restore.ParseAct(restore.ActList, "CHANGEITEMNAMECOLOR 9 0");
            Assert.True(restore.Check(player));
            Assert.Equal((byte)0, playerTalisman.LingFengNameColour);
            Assert.Equal((byte)249, heroTalisman.LingFengNameColour);
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, playerTalisman));
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, heroTalisman));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风人物名称颜色使用旧调色板广播并随人物变量持久化()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "命格名称颜色人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CHANGENAMECOLOR 249");

            Assert.True(segment.Check(player));
            Assert.Equal((byte)249, player.Info.LingFengProgress.NameColour);
            Assert.True(LingFengLegacyColorTable.TryGetRgb(
                249, out byte red, out byte green, out byte blue));
            Color expected = Color.FromArgb(255, red, green, blue);
            Assert.Equal(expected, player.NameColour);
            Assert.Contains(player.Packets.OfType<ServerPackets.ColourChanged>(),
                packet => packet.NameColour == expected);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredStore = new CharacterScriptVariableStore();
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredStore.Load(reader);
            Assert.Equal((byte)249,
                new LingFengCharacterProgress(restoredStore).NameColour);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风物品内外观按实例修改并覆盖人物英雄与当前触发物品()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var heroInfo = new HeroInfo { Name = "命格幻化英雄", Level = 40, HP = 1, MP = 1 };
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格幻化人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1,
                    Heroes = new[] { heroInfo }
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.Hero = new HeroObject(heroInfo, player);

            var playerWeaponInfo = new ItemInfo
            {
                Name = "人物命格剑", Type = ItemType.武器, Shape = 12, Image = 1200,
                Stats = new Stats()
            };
            var heroArmourInfo = new ItemInfo
            {
                Name = "英雄命格甲", Type = ItemType.盔甲, Shape = 18, Image = 1800,
                Stats = new Stats()
            };
            var triggerItemInfo = new ItemInfo
            {
                Name = "触发命格剑", Type = ItemType.武器, Shape = 22, Image = 2200,
                Stats = new Stats()
            };
            var playerWeapon = new UserItem(playerWeaponInfo) { UniqueID = 981640 };
            var heroArmour = new UserItem(heroArmourInfo) { UniqueID = 981641 };
            var triggerItem = new UserItem(triggerItemInfo) { UniqueID = 981642 };
            player.Info.Equipment[(int)EquipmentSlot.武器] = playerWeapon;
            heroInfo.Equipment[(int)EquipmentSlot.盔甲] = heroArmour;
            player.Info.Inventory[2] = triggerItem;
            player.RefreshStats();
            player.Hero.RefreshStats();

            var change = Segment();
            change.ParseAct(change.ActList, "SETITEMLOOKS 1 = 3200");
            change.ParseAct(change.ActList, "SETITEMLOOKS 1 + 5");
            change.ParseAct(change.ActList, "SETITEMSHAPE 1 = 32");
            change.ParseAct(change.ActList, "SETITEMSHAPE 1 - 2");
            change.ParseAct(change.ActList, "H.SETITEMLOOKS 0 = 3800");
            change.ParseAct(change.ActList, "H.SETITEMSHAPE 0 = 38");
            Assert.True(change.Check(player));

            Assert.Equal((ushort)3205, playerWeapon.Image);
            Assert.Equal((short)30, playerWeapon.LingFengShape);
            Assert.Equal((short)30, player.Looks_Weapon);
            Assert.Equal((ushort)3800, heroArmour.Image);
            Assert.Equal((short)38, heroArmour.LingFengShape);
            Assert.Equal((short)38, player.Hero.Looks_Armour);
            Assert.Equal((ushort)1200, playerWeaponInfo.Image);
            Assert.Equal((short)12, playerWeaponInfo.Shape);
            Assert.Equal((ushort)1800, heroArmourInfo.Image);
            Assert.Equal((short)18, heroArmourInfo.Shape);

            using (LingFengTxtTriggerContext.Push(new LingFengItemTriggerEvent(
                       LingFengItemTriggerKind.Use, triggerItemInfo.Name, 2, 0)))
            {
                var current = Segment();
                current.ParseAct(current.ActList, "SETITEMLOOKS -1 = 4200");
                current.ParseAct(current.ActList, "SETITEMSHAPE -1 = 42");
                Assert.True(current.Check(player));
            }
            Assert.Equal((ushort)4200, triggerItem.Image);
            Assert.Equal((short)42, triggerItem.LingFengShape);
            Assert.Equal((ushort)2200, triggerItemInfo.Image);
            Assert.Equal((short)22, triggerItemInfo.Shape);
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, triggerItem));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                playerWeapon.Save(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = new UserItem(reader, Envir.Version, Envir.CustomVersion);
            Assert.Equal((ushort)3205, restored.LingFengLooks);
            Assert.Equal((short)30, restored.LingFengShape);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 人物与英雄装备星数按独立实例检测并完成新旧版本持久化往返()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var heroInfo = new HeroInfo { Name = "星数检测英雄" };
            var player = new PacketCapturingPlayerObject
            {
                NPCObjectID = 981648,
                Info = new CharacterInfo
                {
                    Name = "星数检测人物",
                    Heroes = new[] { heroInfo }
                }
            };
            player.Hero = new HeroObject(heroInfo, player);
            var necklaceInfo = new ItemInfo { Name = "星数检测项链", Type = ItemType.项链 };
            var playerNecklace = new UserItem(necklaceInfo) { UniqueID = 981646 };
            var heroNecklace = new UserItem(necklaceInfo) { UniqueID = 981647 };
            Assert.True(playerNecklace.TryChangeLingFengUpgradeCount("=", 3));
            Assert.True(heroNecklace.TryChangeLingFengUpgradeCount("=", 2));
            player.Info.Equipment[(int)EquipmentSlot.项链] = playerNecklace;
            heroInfo.Equipment[(int)EquipmentSlot.项链] = heroNecklace;

            var playerCheck = Segment();
            playerCheck.ParseCheck("CHECKUPGRADECOUNT 3 > 2");
            Assert.True(playerCheck.Check(player));
            var playerReject = Segment();
            playerReject.ParseCheck("CHECKUPGRADECOUNT 3 < 3");
            Assert.False(playerReject.Check(player));
            var heroCheck = Segment();
            heroCheck.ParseCheck("H.CHECKUPGRADECOUNT 3 = 2");
            Assert.True(heroCheck.Check(player));

            var change = Segment();
            player.Info.ScriptVariables.Set(
                ScriptVariableScope.U, "#351", ScriptVariableValue.FromInteger(9));
            change.ParseAct(change.ActList,
                "CHANGEITEMUPGRADECOUNT 3 = <$STR(U351)>");
            change.ParseAct(change.ActList,
                "H.CHANGEITEMUPGRADECOUNT 3 + 5");
            Assert.True(change.Check(player));
            Assert.Equal((byte)9, playerNecklace.LingFengUpgradeCount);
            Assert.Equal((byte)7, heroNecklace.LingFengUpgradeCount);
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, playerNecklace));
            Assert.Contains(player.Packets.OfType<ServerPackets.RefreshItem>(),
                packet => ReferenceEquals(packet.Item, heroNecklace));

            int refreshCount = player.Packets.OfType<ServerPackets.RefreshItem>().Count();
            var overflow = Segment();
            overflow.ParseAct(overflow.ActList,
                "CHANGEITEMUPGRADECOUNT 3 + 255");
            Assert.True(overflow.Check(player));
            Assert.Equal((byte)9, playerNecklace.LingFengUpgradeCount);
            Assert.Equal(refreshCount,
                player.Packets.OfType<ServerPackets.RefreshItem>().Count());

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                playerNecklace.Save(writer);
            byte[] currentBytes = stream.ToArray();
            stream.Position = 0;
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                var restored = new UserItem(reader, Envir.Version, Envir.CustomVersion);
                Assert.Equal((byte)9, restored.LingFengUpgradeCount);
                Assert.Equal((byte)9, playerNecklace.Clone().LingFengUpgradeCount);
                var blobRestored = new UserItem(necklaceInfo);
                Assert.True(blobRestored.TryDeserializeLingFengCustomAttributes(
                    restored.SerializeLingFengCustomAttributes()));
                Assert.Equal((byte)9, blobRestored.LingFengUpgradeCount);
            }

            using var legacyStream = new MemoryStream(currentBytes[..^2], writable: false);
            using var legacyReader = new BinaryReader(
                legacyStream, System.Text.Encoding.UTF8, leaveOpen: true);
            var legacy = new UserItem(legacyReader, Envir.Version, 4);
            Assert.Equal((byte)0, legacy.LingFengUpgradeCount);
            Assert.Equal(legacyStream.Length, legacyStream.Position);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格转生门槛与夺命书生称号经角色持久化往返后仍可检查()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new CharacterInfo
            {
                Index = 981614,
                Name = "命格进度人物",
                CreationIP = "127.0.0.1",
                Heroes = new HeroInfo[1]
            };
            info.LingFengProgress.SetRenewLevel(8);
            var player = new PlayerObject { Info = info };

            var renewGate = Segment();
            renewGate.ParseCheck("CHECKRENEWLEVEL > 7");
            Assert.True(renewGate.Check(player));

            var grant = Segment();
            grant.ParseAct(grant.ActList, "GIVEFENGHAO 夺命书生");
            Assert.True(grant.Check(player));

            var titleGate = Segment();
            titleGate.ParseCheck("CHECKFENGHAO 夺命书生");
            Assert.True(titleGate.Check(player));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                info.Save(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            var restored = new CharacterInfo(
                reader, Server.MirEnvir.Envir.Version, Server.MirEnvir.Envir.CustomVersion);

            Assert.Equal(8, restored.LingFengProgress.RenewLevel);
            Assert.True(restored.LingFengProgress.HasTitle("夺命书生"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风穿戴物品检测读取真实装备且回收称号同步清除激活状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new CharacterInfo { Name = "命格装备称号人物" };
            info.Equipment[(int)EquipmentSlot.Weapon] =
                new UserItem(new ItemInfo { Name = "破军命格剑" });
            var player = new SilentPlayerObject { Info = info };

            var equipped = Segment();
            equipped.ParseCheck("CHECKITEMW 破军命格剑 1");
            Assert.True(equipped.Check(player));

            var missing = Segment();
            missing.ParseCheck("CHECKITEMW 七杀命格剑");
            Assert.False(missing.Check(player));

            Assert.True(info.LingFengProgress.GrantTitle("夺命书生", true));
            Assert.Equal("夺命书生", info.LingFengProgress.ActiveTitle);
            var active = Segment();
            active.ParseCheck("CHECKACTIVEFENGHAO 夺命书生");
            Assert.True(active.Check(player));

            var revoke = Segment();
            revoke.ParseAct(revoke.ActList, "RECYCFENGHAO 夺命书生");
            Assert.True(revoke.Check(player));
            Assert.False(info.LingFengProgress.HasTitle("夺命书生"));
            Assert.Equal(string.Empty, info.LingFengProgress.ActiveTitle);
            Assert.False(active.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明月老TAKEW按名称原子删除已穿戴与内嵌装备并同步客户端()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var ringInfo = new ItemInfo { Name = "求婚戒指" };
            var wornRing = new UserItem(ringInfo) { UniqueID = 981701 };
            var embeddedRing = new UserItem(ringInfo) { UniqueID = 981702 };
            var weapon = new UserItem(new ItemInfo { Name = "命格试炼剑", Slots = 1 })
            {
                UniqueID = 981703
            };
            weapon.Slots[0] = embeddedRing;

            var info = new CharacterInfo
            {
                Name = "酷明月老人物",
                Class = MirClass.战士,
                Level = 40,
                HP = 1,
                MP = 1
            };
            info.Mount = new MountInfo(null);
            info.Equipment[(int)EquipmentSlot.RingL] = wornRing;
            info.Equipment[(int)EquipmentSlot.Weapon] = weapon;
            var player = new PacketCapturingPlayerObject { Info = info, Stats = new Stats() };
            info.Mount = new MountInfo(player);

            var insufficient = Segment();
            insufficient.ParseAct(insufficient.ActList, "TAKEW 求婚戒指 3");
            Assert.True(insufficient.Check(player));
            Assert.Same(wornRing, info.Equipment[(int)EquipmentSlot.RingL]);
            Assert.Same(embeddedRing, weapon.Slots[0]);
            Assert.Empty(player.Packets.OfType<ServerPackets.DeleteItem>());

            var take = Segment();
            take.ParseAct(take.ActList, "TAKEW 求婚戒指 2");
            Assert.True(take.Check(player));
            Assert.Null(info.Equipment[(int)EquipmentSlot.RingL]);
            Assert.Null(weapon.Slots[0]);
            Assert.Equal(2, player.Packets.OfType<ServerPackets.DeleteItem>().Count());
            Assert.Contains(player.Packets.OfType<ServerPackets.UserSlotsRefresh>(), packet =>
                packet.Equipment[(int)EquipmentSlot.RingL] == null &&
                packet.Equipment[(int)EquipmentSlot.Weapon]?.Slots[0] == null);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明月老结婚协议离婚与强制离婚复用真实关系领域链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        int oldCooldown = Settings.MarriageCooldown;
        int oldLevelRequired = Settings.MarriageLevelRequired;
        var first = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo
            {
                Index = 981681,
                Name = "酷明月老新郎",
                Gender = MirGender.Male,
                Level = 40,
                MarriedDate = DateTime.MinValue
            },
            Stats = new Stats(),
            AllowMarriage = true,
            CurrentLocation = Point.Empty,
            Direction = MirDirection.Right
        };
        var second = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo
            {
                Index = 981682,
                Name = "酷明月老新娘",
                Gender = MirGender.Female,
                Level = 40,
                MarriedDate = DateTime.MinValue
            },
            Stats = new Stats(),
            AllowMarriage = true,
            CurrentLocation = new Point(1, 0),
            Direction = MirDirection.Left
        };
        var map = new Map(new MapInfo { Index = 98168, FileName = "LF-MARRY" })
        {
            Width = 2,
            Height = 1,
            Cells = new Cell[2, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        map.Cells[1, 0] = new Cell { Attribute = CellAttribute.Walk };
        first.CurrentMap = map;
        second.CurrentMap = map;
        first.Info.Mount = new MountInfo(first);
        second.Info.Mount = new MountInfo(second);
        map.GetCell(first.CurrentLocation).Add(first);
        map.GetCell(second.CurrentLocation).Add(second);

        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.MarriageCooldown = 0;
            Settings.MarriageLevelRequired = 1;
            Envir.Main.Players.Add(first);
            Envir.Main.Players.Add(second);
            Envir.Main.CharacterList.Add(first.Info);
            Envir.Main.CharacterList.Add(second.Info);

            var propose = Segment();
            propose.ParseAct(propose.ActList, "MARRY");
            Assert.True(propose.Check(first));
            Assert.Same(first, second.MarriageProposal);
            Assert.Contains(second.Packets, packet => packet is ServerPackets.MarriageRequest);

            var accept = Segment();
            accept.ParseAct(accept.ActList, "MARRY RESPONSEMARRY OK");
            Assert.True(accept.Check(second));
            Assert.Equal(second.Info.Index, first.Info.Married);
            Assert.Equal(first.Info.Index, second.Info.Married);

            var divorce = Segment();
            divorce.ParseAct(divorce.ActList, "UNMARRY REQUESTUNMARRY");
            Assert.True(divorce.Check(first));
            Assert.Same(first, second.DivorceProposal);
            Assert.Contains(second.Packets, packet => packet is ServerPackets.DivorceRequest);

            var confirm = Segment();
            confirm.ParseAct(confirm.ActList, "UNMARRY RESPONSEUNMARRY");
            Assert.True(confirm.Check(second));
            Assert.Equal(0, first.Info.Married);
            Assert.Equal(0, second.Info.Married);

            first.Info.Married = second.Info.Index;
            second.Info.Married = first.Info.Index;
            var force = Segment();
            force.ParseAct(force.ActList, "UNMARRY REQUESTUNMARRY FORCE");
            Assert.True(force.Check(first));
            Assert.Equal(0, first.Info.Married);
            Assert.Equal(0, second.Info.Married);
        }
        finally
        {
            Envir.Main.Players.Remove(first);
            Envir.Main.Players.Remove(second);
            Envir.Main.CharacterList.Remove(first.Info);
            Envir.Main.CharacterList.Remove(second.Info);
            Settings.MarriageCooldown = oldCooldown;
            Settings.MarriageLevelRequired = oldLevelRequired;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明武馆拜师应答与强制解除复用真实师徒领域链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        byte oldGap = Settings.MentorLevelGap;
        byte oldLength = Settings.MentorLength;
        var student = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo
            {
                Index = 981683,
                Name = "酷明武馆徒弟",
                Class = MirClass.战士,
                Level = 20,
                MentorDate = DateTime.MinValue
            },
            Stats = new Stats(),
            CurrentLocation = Point.Empty,
            Direction = MirDirection.Right
        };
        var mentor = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo
            {
                Index = 981684,
                Name = "酷明武馆师傅",
                Class = MirClass.战士,
                Level = 40,
                MentorDate = DateTime.MinValue
            },
            Stats = new Stats(),
            AllowMentor = true,
            CurrentLocation = new Point(1, 0),
            Direction = MirDirection.Left
        };
        var map = new Map(new MapInfo { Index = 98169, FileName = "LF-MENTOR" })
        {
            Width = 2,
            Height = 1,
            Cells = new Cell[2, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        map.Cells[1, 0] = new Cell { Attribute = CellAttribute.Walk };
        student.CurrentMap = map;
        mentor.CurrentMap = map;
        student.Info.Mount = new MountInfo(student);
        mentor.Info.Mount = new MountInfo(mentor);
        map.GetCell(student.CurrentLocation).Add(student);
        map.GetCell(mentor.CurrentLocation).Add(mentor);

        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.MentorLevelGap = 10;
            Settings.MentorLength = 7;
            Envir.Main.Players.Add(student);
            Envir.Main.Players.Add(mentor);
            Envir.Main.CharacterList.Add(student.Info);
            Envir.Main.CharacterList.Add(mentor.Info);

            var request = Segment();
            request.ParseAct(request.ActList, "MASTER REQUESTMASTER");
            Assert.True(request.Check(student));
            Assert.Same(student, mentor.MentorRequest);
            Assert.Contains(mentor.Packets, packet => packet is ServerPackets.MentorRequest);

            var accept = Segment();
            accept.ParseAct(accept.ActList, "MASTER RESPONSEMASTER OK");
            Assert.True(accept.Check(mentor));
            Assert.Equal(mentor.Info.Index, student.Info.Mentor);
            Assert.False(student.Info.IsMentor);
            Assert.Equal(student.Info.Index, mentor.Info.Mentor);
            Assert.True(mentor.Info.IsMentor);

            var force = Segment();
            force.ParseAct(force.ActList, "UNMASTER REQUESTUNMASTER FORCE");
            Assert.True(force.Check(student));
            Assert.Equal(0, student.Info.Mentor);
            Assert.Equal(0, mentor.Info.Mentor);
            Assert.False(student.Info.IsMentor);
            Assert.False(mentor.Info.IsMentor);
        }
        finally
        {
            Envir.Main.Players.Remove(student);
            Envir.Main.Players.Remove(mentor);
            Envir.Main.CharacterList.Remove(student.Info);
            Envir.Main.CharacterList.Remove(mentor.Info);
            Settings.MentorLevelGap = oldGap;
            Settings.MentorLength = oldLength;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风声望调整与检测复用账户持久字段并发送真实增减包()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "命格声望人物" },
                Account = new AccountInfo { Credit = 10 }
            };
            var increase = Segment();
            increase.ParseAct(increase.ActList, "CREDITPOINT + 5");
            Assert.True(increase.Check(player));
            Assert.Equal((uint)15, player.Account.Credit);
            Assert.Contains(player.Packets.OfType<ServerPackets.GainedCredit>(),
                packet => packet.Credit == 5);

            var check = Segment();
            check.ParseCheck("CHECKCREDITPOINT > 14");
            Assert.True(check.Check(player));

            var decrease = Segment();
            decrease.ParseAct(decrease.ActList, "CREDITPOINT - 100");
            Assert.True(decrease.Check(player));
            Assert.Equal((uint)0, player.Account.Credit);
            Assert.Contains(player.Packets.OfType<ServerPackets.LoseCredit>(),
                packet => packet.Credit == 15);

            var assign = Segment();
            assign.ParseAct(assign.ActList, "CREDITPOINT = 8");
            Assert.True(assign.Check(player));
            Assert.Equal((uint)8, player.Account.Credit);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风历史网页地址运行时失败关闭且大对话框命令关闭当前NPC会话()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldHighRisk = Settings.TxtScriptsHighRiskCapabilitiesEnabled;
        string oldHosts = Settings.TxtScriptsAllowedHttpsHosts;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = true;
            Settings.TxtScriptsAllowedHttpsHosts = "docs.example.com";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "命格安全网页人物" },
                NPCObjectID = 981699,
                NPCScriptID = 981698,
                NPCSpeech = new List<string> { "大对话框内容" }
            };

            var unsafeWebsite = Segment();
            unsafeWebsite.ParseAct(unsafeWebsite.ActList,
                "OPENWEBSITE http://legacy-pay.example.com/order?id=1");
            Assert.True(unsafeWebsite.Check(player));
            Assert.Empty(player.Packets.OfType<ServerPackets.OpenBrowser>());

            var close = Segment();
            close.ParseAct(close.ActList, "CLOSEBIGDIALOGBOX");
            Assert.True(close.Check(player));
            Assert.Empty(player.NPCSpeech);
            Assert.Equal((uint)0, player.NPCObjectID);
            Assert.Equal(0, player.NPCScriptID);

            var merchantClose = Segment();
            merchantClose.ParseAct(merchantClose.ActList, "CLOSEMERCHANTBIGDLG");
            Assert.Single(merchantClose.ActList);
            Assert.Equal(ActionType.LingFengCloseNpc, merchantClose.ActList[0].Type);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsHighRiskCapabilitiesEnabled = oldHighRisk;
            Settings.TxtScriptsAllowedHttpsHosts = oldHosts;
        }
    }

    [Fact]
    public void 翎风字符串列表位置只读候选快照并将零基下标或未命中值写入N0()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        ITextFileProvider oldProvider = Envir.Main.TextFileProvider;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var definition = new TextFileDefinition("QuestDiary/命格名称.txt")
                .AddLines(new[] { "七杀", "破军", "紫微" });
            typeof(Envir).GetProperty(nameof(Envir.TextFileProvider))!
                .SetValue(Envir.Main, new SingleProvider(definition));
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格列表人物" }
            };

            var found = Segment();
            found.ParseAct(found.ActList,
                @"GETSTRINGPOS ..\QuestDiary\命格名称.txt 破军");
            Assert.True(found.Check(player));
            Assert.Equal("1", found.FindVariable(player, "%N0"));

            var missing = Segment();
            missing.ParseAct(missing.ActList,
                @"GETSTRINGPOS ..\QuestDiary\命格名称.txt 天机");
            Assert.True(missing.Check(player));
            Assert.Equal("9999999", missing.FindVariable(player, "%N0"));

            var absolute = Segment();
            absolute.ParseAct(absolute.ActList,
                @"GETSTRINGPOS C:\Windows\win.ini 七杀 1");
            Assert.True(absolute.Check(player));
            Assert.Equal("9999999", absolute.FindVariable(player, "%N0"));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.TextFileProvider))!
                .SetValue(Envir.Main, oldProvider);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风NPC大对话框从已发布资源表解析完整样式且关闭时清理客户端状态()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        bool oldPacketSide = Packet.IsServer;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-big-dialog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "EffectImageList.txt"),
                "NewopUi.Pak\r\nNpc界面.Pak\r\nTianFu.Pak\r\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "命格大对话框人物" },
                NPCObjectID = 981690,
                NPCScriptID = 981691
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "OPENMERCHANTBIGDLG 1 653 1 4 0 -65 1 480 0 1");
            Assert.True(segment.Check(player));

            ServerPackets.LingFengDialog packet = Assert.Single(
                player.Packets.OfType<ServerPackets.LingFengDialog>());
            Assert.Equal(0, packet.DialogId);
            Assert.True(packet.NpcStyle);
            Assert.Equal("Npc界面", packet.LibraryName);
            Assert.Equal(653, packet.ImageIndex);
            Assert.True(packet.Movable);
            Assert.Equal(4, packet.Position);
            Assert.Equal(0, packet.X);
            Assert.Equal(-65, packet.Y);
            Assert.True(packet.ShowCloseButton);
            Assert.Equal(480, packet.CloseButtonX);
            Assert.Equal(0, packet.CloseButtonY);
            Assert.True(packet.ContinueNpcStyle);

            Packet.IsServer = false;
            ServerPackets.LingFengDialog restored = Assert.IsType<ServerPackets.LingFengDialog>(
                Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out byte[] extra));
            Assert.Empty(extra);
            var state = new LingFengClientPresentationState();
            state.Apply(restored);
            Assert.Equal("Npc界面", state.Dialogs[0].LibraryName);
            Assert.Equal(new Point(300, 135),
                LingFengClientPresentationState.ResolveNpcDialogLocation(
                    new Size(1000, 700), new Size(400, 300), 4, 0, -65));

            var close = Segment();
            close.ParseAct(close.ActList, "CLOSEMERCHANTBIGDLG");
            Assert.True(close.Check(player));
            ServerPackets.LingFengDialog remove = player.Packets
                .OfType<ServerPackets.LingFengDialog>().Last();
            Assert.True(remove.Remove);
            Assert.Equal(0, remove.DialogId);
            state.Apply(remove);
            Assert.Empty(state.Dialogs);
        }
        finally
        {
            Packet.IsServer = oldPacketSide;
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 高频比较与取反通过真实NPC检测链执行()
    {
        Assert.Equal(56, (int)CheckType.IsGuildLeader);
        Assert.Equal(57, (int)CheckType.LingFengCompare);
        bool oldEnabled = Settings.TxtScriptsEnabled;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "高频比较人物" },
                Account = new AccountInfo(),
                Stats = new Stats()
            };

            var matching = Segment();
            matching.ParseCheck("EQUAL 奖励甲 奖励甲");
            matching.ParseCheck("LARGE 30 29");
            matching.ParseCheck("SMALL 29 30");
            matching.ParseCheck("NOT EQUAL 玩家甲 玩家乙");
            matching.ParseCheck("!SMALL 30 30");
            matching.ParseCheck("EQUAL <$SCRIPTPARAM1>");

            using (LingFengTxtTriggerContext.PushScriptParameters(new[] { string.Empty }))
            {
                Assert.Equal(string.Empty,
                    matching.ReplaceValue(player, "<$SCRIPTPARAM1>"));
                Assert.True(matching.Check(player));
            }
            Assert.Equal(6, matching.CheckList.Count);
            Assert.True(matching.CheckList[3].Negated);
            Assert.True(matching.CheckList[4].Negated);
            Assert.Equal(string.Empty, matching.CheckList[5].Params[2]);

            var unaryNotEmpty = Segment();
            unaryNotEmpty.ParseCheck("NOT EQUAL 已解封");
            Assert.True(unaryNotEmpty.Check(player));

            var existingCheck = Segment();
            existingCheck.ParseCheck("NOT CHECKLEVEL > 100");
            Assert.True(existingCheck.CheckList.Single().Negated);

            var failing = Segment();
            failing.ParseCheck("LARGE 非数字 1");
            Assert.False(failing.Check(player));
        }
        finally
        {
            Settings.TxtScriptsEnabled = oldEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格文本包含检测按原始文本执行并支持取反()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject();
            var matching = Segment();
            matching.ParseCheck("CHECKCONTAINSTEXT 命格洗炼完成 洗炼");
            matching.ParseCheck("NOT CHECKCONTAINSTEXT 命格洗炼完成 失败");

            Assert.Equal(
                new[] { CheckType.LingFengContainsText, CheckType.LingFengContainsText },
                matching.CheckList.Select(check => check.Type));
            Assert.True(matching.Check(player));

            var caseSensitive = Segment();
            caseSensitive.ParseCheck("CHECKCONTAINSTEXT Fate fate");
            Assert.False(caseSensitive.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格百分比计算截断为整数并写回脚本变量()
    {
        Assert.True(LingFengNumericCommandExecutor.TryCalculatePercent(
            "205", "5", out long calculated, out string diagnostic));
        Assert.Equal(10, calculated);
        Assert.Empty(diagnostic);
        Assert.False(LingFengNumericCommandExecutor.TryCalculatePercent(
            long.MaxValue.ToString(), "200", out _, out diagnostic));
        Assert.Contains("范围", diagnostic, StringComparison.Ordinal);

        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject();
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CALCPERCENT 205 5 N1");

            Assert.Equal(ActionType.LingFengCalcPercent, Assert.Single(segment.ActList).Type);
            Assert.True(segment.Check(player));
            Assert.Equal("10", segment.FindVariable(player, "%N1"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风Equal动作与Mov保持同一变量赋值语义()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject { Info = new CharacterInfo { Name = "命格赋值人物" } };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "EQUAL N1 37");
            segment.ParseAct(segment.ActList, "EQUAL S2 破军");

            Assert.True(segment.Check(player));
            Assert.Equal("37", segment.FindVariable(player, "%N1"));
            Assert.Equal("破军", segment.FindVariable(player, "%S2"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格发送消息按翎风颜色模式进入当前人物聊天链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new CapturingPlayerObject();
            var segment = Segment();
            segment.ParseAct(segment.ActList, "SENDMSG 6 [命格] 洗炼完成");
            segment.ParseAct(segment.ActList, "SENDMSG 7 蓝色提示");
            segment.ParseAct(segment.ActList,
                "SENDCENTERMSG 250 0 玩家[命格人物]获得了隐藏称号 0 3");
            segment.ParseAct(segment.ActList,
                "SENDMOVEMSG 1 244 0 100 1 命格滚动公告 11 30 0");

            Assert.True(segment.Check(player));
            Assert.Equal(
                new[]
                {
                    ("[命格] 洗炼完成", ChatType.Shout2),
                    ("蓝色提示", ChatType.LineMessage),
                    ("玩家[命格人物]获得了隐藏称号", ChatType.Announcement),
                    ("命格滚动公告", ChatType.Announcement)
                },
                player.Messages);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 全服消息过滤按人物和消息类型独立生效并可恢复()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var source = new CapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "过滤发起者" }
        };
        var filtered = new CapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "过滤接收者" }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(filtered);

            var filter = Segment();
            filter.ParseAct(filter.ActList, "FILTERGLOBALMSG 1 1");
            filter.ParseAct(filter.ActList, "FILTERGLOBALMSG 3 1");
            Assert.True(filter.Check(filtered));

            var send = Segment();
            send.ParseAct(send.ActList, "SENDCENTERMSG 250 0 全服中央消息 1 3");
            send.ParseAct(send.ActList, "SENDMSG 0 全服普通消息");
            send.ParseAct(send.ActList, "SENDMSG 6 当前人物消息");
            Assert.True(send.Check(source));

            Assert.Contains(("全服中央消息", ChatType.Announcement), source.Messages);
            Assert.Contains(("全服普通消息", ChatType.Normal), source.Messages);
            Assert.Contains(("当前人物消息", ChatType.Shout2), source.Messages);
            Assert.DoesNotContain(filtered.Messages, message =>
                message.Item1 is "全服中央消息" or "全服普通消息");

            var restore = Segment();
            restore.ParseAct(restore.ActList, "FILTERGLOBALMSG 1 0");
            restore.ParseAct(restore.ActList, "FILTERGLOBALMSG 3 0");
            Assert.True(restore.Check(filtered));
            Assert.False(filtered.Info.LingFengProgress.IsGlobalMessageFiltered(1));
            Assert.False(filtered.Info.LingFengProgress.IsGlobalMessageFiltered(3));
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(filtered);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 通用铜钱绑定货币检测与扣除共用账户金币原子账本()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new CapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "绑定货币人物" },
                Account = new AccountInfo { Gold = 100 }
            };
            var payable = Segment();
            payable.ParseCheck("CHECKBINDMONEY 铜钱 80");
            payable.ParseAct(payable.ActList, "DECBINDMONEY 铜钱 80");

            Assert.True(payable.Check(player));
            Assert.Equal(20U, player.Account.Gold);

            var insufficient = Segment();
            insufficient.ParseCheck("CHECKBINDMONEY 金币 30");
            insufficient.ParseAct(insufficient.ActList, "DECBINDMONEY 金币 30");
            Assert.False(insufficient.Check(player));
            Assert.Equal(20U, player.Account.Gold);

            var unknown = Segment();
            unknown.ParseAct(unknown.ActList, "DECBINDMONEY 封神币 1");
            Assert.True(unknown.Check(player));
            Assert.Equal(20U, player.Account.Gold);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风彩色公告默认全服且显式Self只发当前人物()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var source = new CapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "公告发起者" }
        };
        var other = new CapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "公告接收者" }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(other);

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "GUILDNOTICEMSG 253 0 命格全服公告");
            segment.ParseAct(segment.ActList,
                "GUILDNOTICEMSG 250 0 命格个人公告 Self");
            Assert.True(segment.Check(source));

            Assert.Equal(
                new[]
                {
                    ("命格全服公告", ChatType.Announcement),
                    ("命格个人公告", ChatType.Announcement)
                },
                source.Messages);
            Assert.Equal(
                new[] { ("命格全服公告", ChatType.Announcement) },
                other.Messages);

            var robotSegment = Segment();
            robotSegment.ParseAct(robotSegment.ActList,
                "GUILDNOTICEMSG 255 252 Robot全服公告");
            Assert.True(robotSegment.Check());
            Assert.Contains(("Robot全服公告", ChatType.Announcement), source.Messages);
            Assert.Contains(("Robot全服公告", ChatType.Announcement), other.Messages);
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(other);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格客户端Buff携带图标槽位倒计时与中文说明并可协议往返()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldPacketSide = Packet.IsServer;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "命格Buff人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "SETCLIENTBUFF 12 185 4 30 0 0 战斗饥渴：主属性提高");

            Assert.True(segment.Check(player));
            var packet = Assert.IsType<ServerPackets.AddBuff>(Assert.Single(player.Packets));
            Assert.Equal((BuffType)244, packet.Buff.Type);
            Assert.True(packet.Buff.IsLingFengScript);
            Assert.Equal(12, packet.Buff.LingFengIconPackage);
            Assert.Equal(185, packet.Buff.LingFengIconIndex);
            Assert.Equal((byte)4, packet.Buff.LingFengSlot);
            Assert.Equal(30 * Settings.Second, packet.Buff.ExpireTime);
            Assert.Equal("战斗饥渴：主属性提高", packet.Buff.LingFengDescription);

            Packet.IsServer = false;
            var restored = Assert.IsType<ServerPackets.AddBuff>(Packet.ReceivePacket(
                packet.GetPacketBytes().ToArray(), out byte[] extra));
            Assert.Empty(extra);
            Assert.Equal(packet.Buff.LingFengDescription, restored.Buff.LingFengDescription);
            Assert.Equal(packet.Buff.LingFengIconIndex, restored.Buff.LingFengIconIndex);

            player.Packets.Clear();
            var close = Segment();
            close.ParseAct(close.ActList, "CLOSECLIENTBUFF 310");
            Assert.True(close.Check(player));
            Assert.Empty(player.Packets);
        }
        finally
        {
            Packet.IsServer = oldPacketSide;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格屏幕特效经协议启动并由停止命令精确移除()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldPacketSide = Packet.IsServer;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject();
            var segment = Segment();
            segment.ParseAct(segment.ActList, "SCREENEFFECT 100 120 12 17 1 -1 100 0 0 1");
            segment.ParseAct(segment.ActList, "STOPSCREENEFFECT 100 120 12 17 1 -1 100 0 0 1");

            Assert.True(segment.Check(player));
            Assert.Equal(2, player.Packets.Count);
            var start = Assert.IsType<ServerPackets.LingFengScreenEffect>(player.Packets[0]);
            var stop = Assert.IsType<ServerPackets.LingFengScreenEffect>(player.Packets[1]);
            Assert.False(start.Stop);
            Assert.True(stop.Stop);
            Assert.Equal(17, start.StartIndex);
            Assert.Equal(-1, start.LoopCount);

            Packet.IsServer = false;
            var restored = Assert.IsType<ServerPackets.LingFengScreenEffect>(Packet.ReceivePacket(
                start.GetPacketBytes().ToArray(), out byte[] extra));
            Assert.Empty(extra);
            var state = new LingFengClientPresentationState();
            state.Apply(restored);
            Assert.Single(state.ScreenEffects);
            state.Apply(stop);
            Assert.Empty(state.ScreenEffects);
        }
        finally
        {
            Packet.IsServer = oldPacketSide;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格扩展对话框经协议建立并按编号删除()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldPacketSide = Packet.IsServer;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject();
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                @"ADDDLGEX 41 82 16 0 275:-25 0:0 0 ..\..\..\..\Mir通服数据\11命格通服\命格选择界面.Txt 0");
            segment.ParseAct(segment.ActList, "DELDLG 41");

            Assert.True(segment.Check(player));
            var add = Assert.IsType<ServerPackets.LingFengDialog>(player.Packets[0]);
            var remove = Assert.IsType<ServerPackets.LingFengDialog>(player.Packets[1]);
            Assert.False(add.Remove);
            Assert.Equal(41, add.DialogId);
            Assert.Equal(275, add.X);
            Assert.Equal(-25, add.Y);
            Assert.EndsWith("命格选择界面.Txt", add.ExternalTextFile, StringComparison.Ordinal);
            Assert.True(remove.Remove);

            Packet.IsServer = false;
            var restored = Assert.IsType<ServerPackets.LingFengDialog>(Packet.ReceivePacket(
                add.GetPacketBytes().ToArray(), out byte[] extra));
            Assert.Empty(extra);
            var state = new LingFengClientPresentationState();
            state.Apply(restored);
            Assert.True(state.Dialogs.ContainsKey(41));
            state.Apply(remove);
            Assert.Empty(state.Dialogs);
        }
        finally
        {
            Packet.IsServer = oldPacketSide;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明物品数据库字段与背包叠加数量通过真实领域对象读取()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        ItemInfo[] oldItems = Envir.Main.ItemInfoList.ToArray();
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var itemInfo = new ItemInfo
            {
                Index = 981620,
                Name = "命格试炼石",
                Type = ItemType.CraftingMaterial,
                Grade = ItemGrade.Rare,
                Shape = 37,
                Image = 512,
                Durability = 1000,
                StackSize = 100
            };
            itemInfo.Stats[Stat.MaxDC] = 9;
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.Add(itemInfo);

            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "物品字段人物" },
                NPCObjectID = 981620
            };
            player.Info.Inventory[0] = new UserItem(itemInfo)
            {
                UniqueID = 98162001,
                Count = 3,
                CurrentDura = 1000,
                MaxDura = 1000
            };
            player.Info.Inventory[1] = new UserItem(itemInfo)
            {
                UniqueID = 98162002,
                Count = 2,
                CurrentDura = 900,
                MaxDura = 1000
            };

            var segment = Segment();
            segment.ParseAct(segment.ActList, "GETDBITEMFIELDVALUE 命格试炼石 IDX N1");
            segment.ParseAct(segment.ActList, "GETDBITEMFIELDVALUE 命格试炼石 DC2 N2");
            segment.ParseAct(segment.ActList,
                "GETDBIDXITEMFIELDVALUE 981620 NAME S$可回收异兽");
            segment.ParseAct(segment.ActList, "GETBAGITEMCOUNT 命格试炼石 N3");
            segment.ParseAct(segment.ActList, "GETBAGITEMCOUNT 命格试炼石 N4 1 1");
            segment.ParseAct(segment.ActList, "GETITEMCOUNT 0 命格试炼石 N5");
            segment.ParseAct(segment.ActList, "GETBAGINFO ItemIdx L$背包物品");
            segment.ParseAct(segment.ActList, "GETBAGINFO ItemMakeIndex L$背包唯一号");
            segment.ParseAct(segment.ActList, "GETBAGINFO ItemName L$背包名称 16");
            segment.ParseAct(segment.ActList, "GETBAGINFO ItemCount N6 16");

            Assert.True(segment.Check(player));
            Assert.Equal("981620", segment.FindVariable(player, "%N1"));
            Assert.Equal("9", segment.FindVariable(player, "%N2"));
            var context = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            Assert.Equal("命格试炼石", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "S$可回收异兽").Text);
            Assert.Equal("5", segment.FindVariable(player, "%N3"));
            Assert.Equal("3", segment.FindVariable(player, "%N4"));
            Assert.Equal("5", segment.FindVariable(player, "%N5"));
            Assert.Equal("[981620,981620]", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "L$背包物品").Text);
            Assert.Equal("[98162001,98162002]", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "L$背包唯一号").Text);
            Assert.Equal("[命格试炼石,命格试炼石]", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "L$背包名称").Text);
            Assert.Equal("2", segment.FindVariable(player, "%N6"));
        }
        finally
        {
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.AddRange(oldItems);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格GMExecute仅允许类型化探测且返回在线人物真实位置()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo
        {
            Index = 981609,
            FileName = "LF-FATE-PROBE",
            Title = "命格探测地图"
        });
        var source = new CapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格探测者" }
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格追踪目标" },
            CurrentMap = map,
            CurrentLocation = new Point(17, 29)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(target);
            var segment = Segment();
            segment.ParseAct(segment.ActList, "GMEXECUTE 探测 命格追踪目标");
            segment.ParseAct(segment.ActList,
                "GMEXECUTE 探测 <$Str(S$命格上下文_追踪目标)>");

            Assert.True(segment.Check(source));
            Assert.Contains(
                ("命格追踪目标 位于 命格探测地图 (17,29)", ChatType.System),
                source.Messages);

            var forbidden = Segment();
            Assert.Throws<InvalidDataException>(() =>
                forbidden.ParseAct(forbidden.ActList, "GMEXECUTE KILL 命格追踪目标"));
        }
        finally
        {
            Envir.Main.Players.Remove(target);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void GMExecute开始提问从QManage向全部在线人物派发原页面()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-global-question-{Guid.NewGuid():N}");
        NPCScript loadedManage = null;
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "全服提问发起者" },
            Account = new AccountInfo()
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "全服提问接收者" },
            Account = new AccountInfo()
        };
        Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QManage.txt"),
                "[@04轮回踢出]\n#ACT\nGIVEGOLD 1\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            loadedManage = NPCScript.GetOrAdd(
                0, "SystemScripts/QManage", NPCScriptType.Called);
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "GMEXECUTE 开始提问 @04轮回踢出");

            Assert.True(segment.Check(source));
            Assert.Equal(1u, source.Account.Gold);
            Assert.Equal(1u, target.Account.Gold);
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(target);
            if (loadedManage != null) Envir.Main.Scripts.Remove(loadedManage.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HCall只向指定在线人物派发唯一原页面()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-human-call-{Guid.NewGuid():N}");
        NPCScript loadedTarget = null;
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "法宝收录发起者" },
            Account = new AccountInfo()
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "法宝属性接收者" },
            Account = new AccountInfo()
        };
        Directory.CreateDirectory(Path.Combine(root, "QuestDiary"));
        try
        {
            File.WriteAllText(Path.Combine(root, "QuestDiary", "属性脚本.txt"),
                "[@属性计算]\n#ACT\nGIVEGOLD 7\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            loadedTarget = NPCScript.GetOrAdd(
                0, "QuestDiary/属性脚本", NPCScriptType.Called);
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "HCALL 法宝属性接收者 @属性计算");

            Assert.Equal(ActionType.LingFengHumanCall, Assert.Single(segment.ActList).Type);
            Assert.True(segment.Check(source));
            Assert.Equal(0u, source.Account.Gold);
            Assert.Equal(7u, target.Account.Gold);
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(target);
            if (loadedTarget != null) Envir.Main.Scripts.Remove(loadedTarget.ScriptID);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 翎风Map与MapMove支持随机精确坐标及中心范围且走真实传送链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        NPCScript oldDefaultNpc = Envir.Main.DefaultNPC;
        var source = WalkableMap(981620, "LF-MAPMOVE-SOURCE", 1, 1);
        var destination = WalkableMap(981621, "LF-MAPMOVE-TARGET", 9, 9);
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格传送人物" },
            Account = new AccountInfo(),
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100,
            CurrentMap = source,
            CurrentLocation = Point.Empty
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.DefaultNPC = NPCScript.GetOrAdd(
                uint.MaxValue - 1620, "LFENV16-MAPMOVE-DEFAULT", NPCScriptType.AutoPlayer);
            Envir.Main.MapList.Add(source);
            Envir.Main.MapList.Add(destination);
            source.AddObject(player);

            var random = Segment();
            random.ParseAct(random.ActList, "MAP LF-MAPMOVE-TARGET");
            Assert.True(random.Check(player));
            Assert.Same(destination, player.CurrentMap);
            Assert.True(destination.ValidPoint(player.CurrentLocation));

            var mapExact = Segment();
            mapExact.ParseAct(mapExact.ActList, "MAP LF-MAPMOVE-TARGET 7 6 0");
            Assert.True(mapExact.Check(player));
            Assert.Same(destination, player.CurrentMap);
            Assert.Equal(new Point(7, 6), player.CurrentLocation);

            var exact = Segment();
            exact.ParseAct(exact.ActList, "MAPMOVE LF-MAPMOVE-TARGET 4 5");
            Assert.True(exact.Check(player));
            Assert.Same(destination, player.CurrentMap);
            Assert.Equal(new Point(4, 5), player.CurrentLocation);

            var ranged = Segment();
            ranged.ParseAct(ranged.ActList, "MAPMOVE LF-MAPMOVE-TARGET 4 4 2");
            Assert.True(ranged.Check(player));
            Assert.InRange(player.CurrentLocation.X, 2, 6);
            Assert.InRange(player.CurrentLocation.Y, 2, 6);
            Assert.True(destination.ValidPoint(player.CurrentLocation));
        }
        finally
        {
            player.CurrentMap?.RemoveObject(player);
            Envir.Main.MapList.Remove(source);
            Envir.Main.MapList.Remove(destination);
            Envir.Main.DefaultNPC = oldDefaultNpc;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明镜像地图创建检测计时到期回送与客户端物理地图保持一致()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        NPCScript oldDefaultNpc = Envir.Main.DefaultNPC;
        var source = WalkableMap(981624, "A_0148", 6, 6);
        source.Info.Title = "未知暗殿模板";
        var returnMap = WalkableMap(981625, "3", 5, 5);
        returnMap.Info.Title = "盟重省";
        var player = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格镜像人物" },
            Account = new AccountInfo(),
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100,
            CurrentMap = returnMap,
            CurrentLocation = new Point(1, 1)
        };
        const string runtimeName = "命格镜像人物个人副本";
        const string deletedRuntimeName = "命格镜像人物删除副本";
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.DefaultNPC = NPCScript.GetOrAdd(
                uint.MaxValue - 1624, "LFENV16-MIRROR-DEFAULT", NPCScriptType.AutoPlayer);
            Envir.Main.MapList.Add(source);
            Envir.Main.MapList.Add(returnMap);
            returnMap.AddObject(player);

            var create = Segment();
            create.ParseAct(create.ActList,
                $"ADDMIRRORMAP A_0148 {runtimeName} 命格个人副本 30 3 20148 M0 1 1,1");
            Assert.True(create.Check(player));
            Assert.Equal("1", create.FindVariable(player, "%M0"));
            Assert.True(Envir.Main.TryGetLingFengMirrorMapStatus(
                runtimeName, out LingFengMirrorMapStatus created));
            Assert.Equal("A_0148", created.Map.Info.GetClientFileName());
            Assert.Equal(runtimeName, created.Map.Info.FileName);
            Assert.Equal((ushort)20148, created.Map.Info.MiniMap);
            Assert.NotSame(source.Cells[2, 2], created.Map.Cells[2, 2]);

            var check = Segment();
            check.ParseCheck($"CHECKMIRRORMAP {runtimeName}");
            Assert.True(check.Check(player));

            var moveAndReset = Segment();
            moveAndReset.ParseAct(moveAndReset.ActList, $"MAP {runtimeName} 2 2");
            moveAndReset.ParseAct(moveAndReset.ActList,
                $"SETMIRRORMAPTIME {runtimeName} 10 1");
            moveAndReset.ParseAct(moveAndReset.ActList,
                $"GETMIRRORMAPTIME {runtimeName} N1 N2");
            Assert.True(moveAndReset.Check(player));
            Assert.Same(created.Map, player.CurrentMap);
            Assert.Equal(new Point(2, 2), player.CurrentLocation);
            Assert.Equal("10", moveAndReset.FindVariable(player, "%N1"));
            Assert.Equal("10", moveAndReset.FindVariable(player, "%N2"));
            Assert.Contains(player.Packets.OfType<ServerPackets.MapChanged>(),
                packet => packet.FileName == "A_0148" && packet.Title == "命格个人副本");

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            Envir.Main.Process();

            Assert.Same(returnMap, player.CurrentMap);
            Assert.Equal(new Point(1, 1), player.CurrentLocation);
            Assert.False(Envir.Main.IsLingFengMirrorMap(runtimeName));
            Assert.DoesNotContain(created.Map, Envir.Main.MapList);

            var delete = Segment();
            delete.ParseAct(delete.ActList,
                $"ADDMIRRORMAP A_0148 {deletedRuntimeName} 删除测试副本 30 3 20148 M1 1 1,1");
            delete.ParseAct(delete.ActList, $"DELMIRRORMAP {deletedRuntimeName}");
            Assert.True(delete.Check(player));
            Assert.Equal("1", delete.FindVariable(player, "%M1"));
            Assert.False(Envir.Main.IsLingFengMirrorMap(deletedRuntimeName));

            var rejected = Segment();
            rejected.ParseAct(rejected.ActList,
                "ADDMIRRORMAP 不存在地图 不应创建的副本 失败副本 30 3 20148 M2 1 1,1");
            Assert.True(rejected.Check(player));
            Assert.Equal("0", rejected.FindVariable(player, "%M2"));
            Assert.False(Envir.Main.IsLingFengMirrorMap("不应创建的副本"));
        }
        finally
        {
            if (Envir.Main.IsLingFengMirrorMap(runtimeName))
                Envir.Main.TryDeleteLingFengMirrorMap(runtimeName);
            if (Envir.Main.IsLingFengMirrorMap(deletedRuntimeName))
                Envir.Main.TryDeleteLingFengMirrorMap(deletedRuntimeName);
            player.CurrentMap?.RemoveObject(player);
            Envir.Main.MapList.Remove(source);
            Envir.Main.MapList.Remove(returnMap);
            Envir.Main.DefaultNPC = oldDefaultNpc;
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风个人副本按人物隔离刷怪并在空图和总时限到期后安全回收()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldMultithreaded = Settings.Multithreaded;
        long oldTime = Envir.Main.Time;
        int oldMonsterCount = Envir.Main.MonsterCount;
        long[] oldOrbsExpList = Settings.OrbsExpList.ToArray();
        NPCScript oldDefaultNpc = Envir.Main.DefaultNPC;
        var source = WalkableMap(981626, "LF-ECTYPE-SOURCE", 12, 12);
        source.Info.Title = "命格个人副本";
        source.Info.LingFengOptions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["FB"] = "60,命格个人副本,2,1,2"
        };
        var returnMap = WalkableMap(981627, "LF-ECTYPE-RETURN", 5, 5);
        var monsterInfo = new MonsterInfo
        {
            Index = 981628,
            Name = "命格副本怪物",
            Stats = new Stats { [Stat.HP] = 100 }
        };
        var first = new SilentPlayerObject
        {
            Info = new CharacterInfo { Index = 9816261, Name = "命格副本人物甲" },
            Account = new AccountInfo(),
            CurrentMap = returnMap,
            CurrentLocation = new Point(1, 1),
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100
        };
        var second = new SilentPlayerObject
        {
            Info = new CharacterInfo { Index = 9816262, Name = "命格副本人物乙" },
            Account = new AccountInfo(),
            CurrentMap = returnMap,
            CurrentLocation = new Point(2, 2),
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100
        };
        first.Info.Mount = new MountInfo(first);
        second.Info.Mount = new MountInfo(second);
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.Multithreaded = false;
            if (Settings.OrbsExpList.Count == 0)
                Settings.OrbsExpList.Add(0);
            Envir.Main.DefaultNPC = NPCScript.GetOrAdd(
                uint.MaxValue - 1626, "LFENV17-ECTYPE-DEFAULT", NPCScriptType.AutoPlayer);
            Envir.Main.MapList.Add(source);
            Envir.Main.MapList.Add(returnMap);
            Envir.Main.MonsterInfoList.Add(monsterInfo);
            returnMap.AddObject(first);
            returnMap.AddObject(second);

            var sharedMapSpawn = Segment("ectype-shared-map-rejected");
            sharedMapSpawn.ParseAct(sharedMapSpawn.ActList,
                "MOBECTYPEMON SELF 5 5 命格副本怪物 1 1 249");
            Assert.True(sharedMapSpawn.Check(first));
            Assert.DoesNotContain(Envir.Main.Objects.OfType<MonsterObject>(),
                monster => monster.Info == monsterInfo);

            foreach (SilentPlayerObject player in new[] { first, second })
            {
                var create = Segment($"ectype-create-{player.Info.Index}");
                create.ParseAct(create.ActList, "CREATEECTYPE 命格个人副本 30");
                Assert.True(create.Check(player));

                var canMove = Segment($"ectype-check-{player.Info.Index}");
                canMove.ParseCheck("CANMOVEECTYPE 命格个人副本");
                Assert.True(canMove.Check(player));

                var move = Segment($"ectype-move-{player.Info.Index}");
                move.ParseAct(move.ActList, "MOVEECTYPE 命格个人副本 6 6");
                Assert.True(move.Check(player));
            }

            Map firstInstance = first.CurrentMap;
            Map secondInstance = second.CurrentMap;
            Assert.NotSame(source, firstInstance);
            Assert.NotSame(firstInstance, secondInstance);
            Assert.Equal(source.Info.FileName, firstInstance.Info.GetClientFileName());
            Assert.Equal(source.Info.FileName, secondInstance.Info.GetClientFileName());

            var firstSpawn = Segment("ectype-spawn-first");
            firstSpawn.ParseAct(firstSpawn.ActList,
                "MOBECTYPEMON SELF 6 6 命格副本怪物 2 2 249");
            Assert.True(firstSpawn.Check(first));
            var secondSpawn = Segment("ectype-spawn-second");
            secondSpawn.ParseAct(secondSpawn.ActList,
                "MOBECTYPEMON FBMAP 6 6 命格副本怪物 1 2 250");
            Assert.True(secondSpawn.Check(second));
            Assert.Equal(2, Envir.Main.Objects.OfType<MonsterObject>().Count(
                monster => monster.Info == monsterInfo && monster.CurrentMap == firstInstance));
            Assert.Single(Envir.Main.Objects.OfType<MonsterObject>().Where(
                monster => monster.Info == monsterInfo && monster.CurrentMap == secondInstance));

            Assert.True(first.Teleport(returnMap, new Point(1, 1)));
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + Settings.Second);
            Envir.Main.Process();
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 4 * Settings.Second);
            Envir.Main.Process();
            Assert.DoesNotContain(firstInstance, Envir.Main.MapList);
            Assert.Contains(secondInstance, Envir.Main.MapList);
            Assert.Same(secondInstance, second.CurrentMap);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * 60 * Settings.Second);
            Envir.Main.Process();
            Assert.Same(returnMap, second.CurrentMap);
            Assert.DoesNotContain(secondInstance, Envir.Main.MapList);
        }
        finally
        {
            foreach (Map map in Envir.Main.MapList.Where(map =>
                         map.Info.LingFengIsMirror &&
                         map.Info.GetClientFileName() == source.Info.FileName).ToArray())
                Envir.Main.TryDeleteLingFengMirrorMap(map.Info.FileName);
            first.CurrentMap?.RemoveObject(first);
            second.CurrentMap?.RemoveObject(second);
            Envir.Main.MonsterInfoList.Remove(monsterInfo);
            Envir.Main.MapList.Remove(source);
            Envir.Main.MapList.Remove(returnMap);
            Envir.Main.MonsterCount = oldMonsterCount;
            Settings.OrbsExpList.Clear();
            Settings.OrbsExpList.AddRange(oldOrbsExpList);
            Envir.Main.DefaultNPC = oldDefaultNpc;
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.Multithreaded = oldMultithreaded;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风组队与行会副本只允许合格队长会长创建并向成员开放()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var returnMap = WalkableMap(981629, "LF-ECTYPE-ACCESS-RETURN", 5, 5);
        Map threeClass = EctypeMap(981630, "LF-ECTYPE-THREE-CLASS",
            "20,命格三职业副本,0,2,10");
        Map group = EctypeMap(981631, "LF-ECTYPE-GROUP",
            "20,命格组队副本,1,2,10");
        Map guildMap = EctypeMap(981632, "LF-ECTYPE-GUILD",
            "20,命格行会副本,3,2,10");
        Map guildRejected = EctypeMap(981633, "LF-ECTYPE-GUILD-REJECTED",
            "20,命格会长限定副本,3,2,10");

        SilentPlayerObject Player(int index, string name, MirClass mirClass) => new()
        {
            Info = new CharacterInfo { Index = index, Name = name, Class = mirClass },
            Account = new AccountInfo(),
            CurrentMap = returnMap,
            CurrentLocation = new Point(1, 1),
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100
        };

        var leader = Player(9816301, "命格队长", MirClass.Warrior);
        var wizard = Player(9816302, "命格法师", MirClass.Wizard);
        var taoist = Player(9816303, "命格道士", MirClass.Taoist);
        var outsider = Player(9816304, "命格局外人", MirClass.Warrior);
        var guildLeader = Player(9816305, "命格会长", MirClass.Warrior);
        var guildMember = Player(9816306, "命格会员", MirClass.Wizard);
        var leaderRank = new GuildRank { Index = 0, Name = "会长" };
        var memberRank = new GuildRank { Index = 1, Name = "会员" };
        var guild = new GuildObject(new GuildInfo
        {
            GuildIndex = 98163,
            Name = "命格副本行会",
            Ranks = new List<GuildRank> { leaderRank, memberRank }
        });
        guildLeader.MyGuild = guild;
        guildLeader.MyGuildRank = leaderRank;
        guildMember.MyGuild = guild;
        guildMember.MyGuildRank = memberRank;

        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.AddRange(new[]
                { returnMap, threeClass, group, guildMap, guildRejected });
            foreach (SilentPlayerObject player in new[]
                     { leader, wizard, taoist, outsider, guildLeader, guildMember })
                returnMap.AddObject(player);

            var incompleteGroup = new List<PlayerObject> { leader, wizard };
            leader.GroupMembers = incompleteGroup;
            wizard.GroupMembers = incompleteGroup;
            Assert.Equal(LingFengEctypeCreateResult.GroupLeaderRequired,
                Envir.Main.TryCreateLingFengEctype(leader, "命格三职业副本", 30));
            Assert.False(CanMoveEctype(leader, "命格三职业副本"));

            var completeGroup = new List<PlayerObject> { leader, wizard, taoist };
            leader.GroupMembers = completeGroup;
            wizard.GroupMembers = completeGroup;
            taoist.GroupMembers = completeGroup;
            Assert.Equal(LingFengEctypeCreateResult.Created,
                Envir.Main.TryCreateLingFengEctype(leader, "命格三职业副本", 30));
            Assert.True(CanMoveEctype(leader, "命格三职业副本"));
            Assert.True(CanMoveEctype(wizard, "命格三职业副本"));
            Assert.True(CanMoveEctype(taoist, "命格三职业副本"));
            Assert.False(CanMoveEctype(outsider, "命格三职业副本"));

            Assert.Equal(LingFengEctypeCreateResult.GroupLeaderRequired,
                Envir.Main.TryCreateLingFengEctype(wizard, "命格组队副本", 30));
            Assert.False(CanMoveEctype(wizard, "命格组队副本"));

            Assert.Equal(LingFengEctypeCreateResult.Created,
                Envir.Main.TryCreateLingFengEctype(guildLeader, "命格行会副本", 30));
            Assert.True(CanMoveEctype(guildLeader, "命格行会副本"));
            Assert.True(CanMoveEctype(guildMember, "命格行会副本"));
            Assert.False(CanMoveEctype(outsider, "命格行会副本"));

            Assert.Equal(LingFengEctypeCreateResult.GuildLeaderRequired,
                Envir.Main.TryCreateLingFengEctype(guildMember, "命格会长限定副本", 30));
            Assert.False(CanMoveEctype(guildMember, "命格会长限定副本"));
        }
        finally
        {
            foreach (Map map in Envir.Main.MapList.Where(map =>
                         map.Info.LingFengIsMirror &&
                         map.Info.FileName.StartsWith("LFECTYPE-", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
                Envir.Main.TryDeleteLingFengMirrorMap(map.Info.FileName);
            foreach (SilentPlayerObject player in new[]
                     { leader, wizard, taoist, outsider, guildLeader, guildMember })
                player.CurrentMap?.RemoveObject(player);
            Envir.Main.MapList.Remove(returnMap);
            Envir.Main.MapList.Remove(threeClass);
            Envir.Main.MapList.Remove(group);
            Envir.Main.MapList.Remove(guildMap);
            Envir.Main.MapList.Remove(guildRejected);
            Envir.Main.Guilds.Remove(guild);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }

        static Map EctypeMap(int index, string fileName, string definition)
        {
            Map map = WalkableMap(index, fileName, 5, 5);
            map.Info.Title = fileName;
            map.Info.LingFengOptions = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase) { ["FB"] = definition };
            return map;
        }

        static bool CanMoveEctype(PlayerObject player, string name)
        {
            NPCSegment segment = Segment("ectype-access-check-" + name);
            segment.ParseCheck($"CANMOVEECTYPE {name}");
            return segment.Check(player);
        }
    }

    [Fact]
    public void 翎风副本创建成功与进入超时按原脚本页面异步回调()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var source = WalkableMap(981634, "LF-ECTYPE-CALLBACK", 5, 5);
        source.Info.Title = "命格回调副本";
        source.Info.LingFengOptions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["FB"] = "10,命格回调副本,2,0,10"
        };
        var returnMap = WalkableMap(981635, "LF-ECTYPE-CALLBACK-RETURN", 5, 5);
        NPCScript script = NPCScript.GetOrAdd(
            981634, "LFENV17-ECTYPE-CALLBACK", NPCScriptType.Normal);
        script.NPCPages.Clear();
        AddCallbackPage(script, "[@CREATEECTYPE_OK]", "MOV N$副本回调 1");
        AddCallbackPage(script, "[@MOVEECTYPE_FAIL_TIME]", "MOV N$副本回调 2");
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Index = 9816341, Name = "命格副本回调人物" },
            Account = new AccountInfo(),
            CurrentMap = returnMap,
            CurrentLocation = new Point(1, 1),
            NPCObjectID = script.LoadedObjectID,
            NPCScriptID = script.ScriptID,
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(source);
            Envir.Main.MapList.Add(returnMap);
            returnMap.AddObject(player);

            NPCSegment create = Segment("ectype-callback-create");
            create.ParseAct(create.ActList, "CREATEECTYPE 命格回调副本 10");
            Assert.True(create.Check(player));
            Assert.Single(player.ActionList, action =>
                action.Type == DelayedType.NPC && action.Time == -1);
            player.RunNextNpcForTest();
            Assert.Equal("1", ReadNamedNumber(player, "N$副本回调"));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 61 * Settings.Second);
            NPCSegment move = Segment("ectype-callback-move");
            move.ParseAct(move.ActList, "MOVEECTYPE 命格回调副本 2 2");
            Assert.True(move.Check(player));
            Assert.Single(player.ActionList, action =>
                action.Type == DelayedType.NPC && action.Time == -1);
            player.RunNextNpcForTest();
            Assert.Equal("2", ReadNamedNumber(player, "N$副本回调"));
            Assert.Same(returnMap, player.CurrentMap);
        }
        finally
        {
            foreach (Map map in Envir.Main.MapList.Where(map =>
                         map.Info.LingFengIsMirror &&
                         map.Info.GetClientFileName() == source.Info.FileName).ToArray())
                Envir.Main.TryDeleteLingFengMirrorMap(map.Info.FileName);
            player.CurrentMap?.RemoveObject(player);
            Envir.Main.MapList.Remove(source);
            Envir.Main.MapList.Remove(returnMap);
            Envir.Main.Scripts.Remove(script.ScriptID);
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }

        static void AddCallbackPage(NPCScript owner, string key, string action)
        {
            var page = new NPCPage(key);
            var segment = new NPCSegment(
                page, new List<string>(), new List<string>(), new List<string>(),
                new List<string>(), new List<string>(), "ectype-callback-" + key);
            segment.ParseAct(segment.ActList, action);
            page.SegmentList.Add(segment);
            owner.NPCPages.Add(page);
        }

        static string ReadNamedNumber(PlayerObject target, string name)
        {
            var context = ScriptVariableContext.ForConversation(
                target, target.NPCObjectID, target.CurrentMap);
            return Envir.Main.CSharpScripts.VariableCommands.Format(context, name).Text;
        }
    }

    [Fact]
    public void 翎风MongenEx按范围数量刷怪并应用旧引擎名字颜色()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldMultithreaded = Settings.Multithreaded;
        int oldMonsterCount = Envir.Main.MonsterCount;
        var map = WalkableMap(981622, "LF-MONGENEX", 5, 5);
        var monsterInfo = new MonsterInfo
        {
            Index = 981623,
            Name = "命格刷怪探针",
            Stats = new Stats { [Stat.HP] = 100 }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.Multithreaded = false;
            Envir.Main.MapList.Add(map);
            Envir.Main.MonsterInfoList.Add(monsterInfo);
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "MONGENEX LF-MONGENEX 2 2 命格刷怪探针 1 3 249");

            Assert.True(segment.Check(new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格刷怪人物" }
            }));

            MonsterObject[] monsters = map.Cells.Cast<Cell>()
                .SelectMany(cell => cell.Objects ?? Enumerable.Empty<MapObject>())
                .OfType<MonsterObject>()
                .ToArray();
            Assert.Equal(3, monsters.Length);
            Assert.All(monsters, monster =>
            {
                Assert.InRange(monster.CurrentLocation.X, 1, 3);
                Assert.InRange(monster.CurrentLocation.Y, 1, 3);
                Assert.Equal(Color.FromArgb(255, 255, 0, 0), monster.NameColour);
            });
        }
        finally
        {
            foreach (MonsterObject monster in map.Cells.Cast<Cell>()
                         .SelectMany(cell => cell.Objects ?? Enumerable.Empty<MapObject>())
                         .OfType<MonsterObject>()
                         .ToArray())
            {
                map.RemoveObject(monster);
                if (monster.Node != null) monster.Despawn();
            }
            Envir.Main.MonsterCount = oldMonsterCount;
            Envir.Main.MonsterInfoList.Remove(monsterInfo);
            Envir.Main.MapList.Remove(map);
            Settings.Multithreaded = oldMultithreaded;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格两秒隐身到期只撤销自身且禁止管理无敌模式()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var map = new Map(new MapInfo { Index = 981610, FileName = "LF-FATE-HIDE" });
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格隐身人物" },
            CurrentMap = map
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            player.Hidden = true;

            var hide = Segment();
            hide.ParseAct(hide.ActList, "HIDEMODEEX 2 1");
            Assert.True(hide.Check(player));
            Assert.True(player.Hidden);
            Assert.True(player.LingFengSemiTransparent);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 3 * Settings.Second);
            player.Process();
            Assert.True(player.Hidden);
            Assert.False(player.LingFengSemiTransparent);

            player.Hidden = false;
            Assert.False(player.Hidden);

            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Assert.True(hide.Check(player));
            var clear = Segment();
            clear.ParseAct(clear.ActList, "CHANGEMODE 3 0");
            Assert.True(clear.Check(player));
            Assert.False(player.Hidden);

            Assert.Throws<InvalidDataException>(() =>
                Segment().ParseAct(new List<NPCActions>(), "CHANGEMODE 1 1"));
            Assert.Throws<InvalidDataException>(() =>
                Segment().ParseAct(new List<NPCActions>(), "CHANGEMODE 2 1"));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格ChangeModeEx隐身按本模式重计时且临时无敌不覆盖管理状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var map = new Map(new MapInfo
            { Index = 981640, FileName = "LF-FATE-CHANGE-MODE" });
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格临时模式人物", HP = 100 },
            Stats = new Stats { [Stat.HP] = 100 },
            HP = 100,
            CurrentMap = map
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var hide = Segment("cool-fate-change-mode-hide");
            hide.ParseAct(hide.ActList, "CHANGEMODEEX 2 3");
            Assert.True(hide.Check(player));
            Assert.True(player.Hidden);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 2 * Settings.Second);
            var replaceHide = Segment("cool-fate-change-mode-hide-replace");
            replaceHide.ParseAct(replaceHide.ActList, "CHANGEMODEEX 2 1");
            Assert.True(replaceHide.Check(player));
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 4 * Settings.Second);
            player.Process();
            Assert.False(player.Hidden);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime);
            var invincible = Segment("cool-change-mode-invincible");
            invincible.ParseAct(invincible.ActList, "CHANGEMODEEX 1 2");
            Assert.True(invincible.Check(player));
            Assert.True(player.LingFengInvincible);
            player.ChangeHP(-50);
            Assert.Equal(100, player.HP);
            Assert.False(player.GMNeverDie);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 3 * Settings.Second);
            Assert.False(player.LingFengInvincible);
            player.ChangeHP(-50);
            Assert.Equal(50, player.HP);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格移动攻击魔法速度按来源叠加且独立到期()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格速度人物" }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var fourSeconds = Segment();
            fourSeconds.ParseAct(fourSeconds.ActList, "CHANGESPEED 1 1 4");
            fourSeconds.ParseAct(fourSeconds.ActList, "CHANGESPEED 2 1 4");
            fourSeconds.ParseAct(fourSeconds.ActList, "CHANGESPEED 3 1 4");
            Assert.True(fourSeconds.Check(player));
            Assert.Equal(540, player.GetDelayTime(600));
            Assert.Equal(540, player.GetLingFengSpeedDelay(2, 600));
            Assert.Equal(540, player.GetLingFengSpeedDelay(3, 600));

            Assert.True(fourSeconds.Check(player));
            Assert.Equal(540, player.GetDelayTime(600));

            var fiveSeconds = Segment();
            fiveSeconds.ParseAct(fiveSeconds.ActList, "CHANGESPEED 1 2 5");
            Assert.True(fiveSeconds.Check(player));
            Assert.Equal(420, player.GetDelayTime(600));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 4500);
            Assert.Equal(480, player.GetDelayTime(600));
            Assert.Equal(600, player.GetLingFengSpeedDelay(2, 600));
            Assert.Equal(600, player.GetLingFengSpeedDelay(3, 600));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 5500);
            Assert.Equal(600, player.GetDelayTime(600));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明分身速度三十秒与十秒来源独立到期且不覆盖长期层()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明命格人物" }
        };
        var clone = new FateMonster(new MonsterInfo
        {
            Name = Settings.CloneName,
            MoveSpeed = 600,
            AttackSpeed = 600,
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            Master = player
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            player.Pets.Add(clone);

            var permanent = Segment();
            permanent.ParseAct(permanent.ActList, "FS.CHANGESPEED 1 5");
            Assert.True(permanent.Check(player));

            var thirtySeconds = Segment();
            thirtySeconds.ParseAct(thirtySeconds.ActList, "FS.CHANGESPEED 1 20 30");
            Assert.True(thirtySeconds.Check(player));

            var tenSeconds = Segment();
            tenSeconds.ParseAct(tenSeconds.ActList, "FS.CHANGESPEED 1 30 10");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(6700, clone.GetLingFengSpeedDelay(1, 10000));
            Assert.Equal(10000, player.GetLingFengSpeedDelay(1, 10000));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            Assert.Equal(8500, clone.GetLingFengSpeedDelay(1, 10000));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            Assert.Equal(9700, clone.GetLingFengSpeedDelay(1, 10000));
        }
        finally
        {
            player.Pets.Remove(clone);
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格M前缀速度作用当前事件怪物且不同来源独立到期()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var map = new Map(new MapInfo { Index = 981610, FileName = "LF-FATE-SPEED" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981611,
            Name = "命格减速目标",
            MoveSpeed = 600,
            AttackSpeed = 600,
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(32, 32)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            monster.RefreshAll();
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格减速施法者" },
                CurrentMap = map,
                CurrentLocation = new Point(31, 32)
            };
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                monster.Name,
                monster.Name,
                10,
                10,
                true,
                true,
                monster.CurrentLocation.X,
                monster.CurrentLocation.Y);

            var fiveSeconds = Segment();
            fiveSeconds.ParseAct(fiveSeconds.ActList, "M.CHANGESPEED 1 -4 5");
            fiveSeconds.ParseAct(fiveSeconds.ActList, "M.CHANGESPEED 2 -4 5");
            fiveSeconds.ParseAct(fiveSeconds.ActList, "M.CHANGESPEED 3 -4 5");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(fiveSeconds.Check(player));
            Assert.Equal((ushort)840, monster.MoveSpeed);
            Assert.Equal(1080, monster.AttackSpeed);

            var threeSeconds = Segment();
            threeSeconds.ParseAct(threeSeconds.ActList, "M.CHANGESPEED 1 -6 3");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(threeSeconds.Check(player));
            Assert.Equal((ushort)1200, monster.MoveSpeed);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 4 * Settings.Second);
            Assert.Equal((ushort)840, monster.MoveSpeed);
            Assert.Equal(1080, monster.AttackSpeed);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 6 * Settings.Second);
            Assert.Equal((ushort)600, monster.MoveSpeed);
            Assert.Equal(600, monster.AttackSpeed);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Envir.Main.Objects.Remove(monster);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格M前缀变量按稳定对象编号写入并在目标作用域比较()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981640, FileName = "LF-FATE-TARGET-VAR" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981641,
            Name = "同名命格目标",
            Stats = new Stats { [Stat.HP] = 100 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(42, 42)
        };
        var sameName = new FateMonster(monster.Info)
        {
            CurrentMap = map,
            CurrentLocation = new Point(43, 42)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            Envir.Main.Objects.AddLast(sameName);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格变量施法者" },
                CurrentMap = map,
                CurrentLocation = new Point(41, 42)
            };
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                monster.Name,
                monster.Name,
                10,
                10,
                true,
                true,
                monster.CurrentLocation.X,
                monster.CurrentLocation.Y)
            {
                CurrentTargetObjectId = monster.ObjectID
            };
            var mutate = Segment();
            mutate.ParseAct(mutate.ActList, "M.MOV N$命格_目标计数 40");
            mutate.ParseAct(mutate.ActList, "M.INC N$命格_目标计数 2");
            var check = Segment();
            check.ParseCheck("M.EQUAL N$命格_目标计数 42");

            using (LingFengTxtTriggerContext.Push(payload))
            {
                Assert.True(mutate.Check(player));
                Assert.True(check.Check(player));
            }

            ScriptVariableTextResult selected = Envir.Main.CSharpScripts.VariableCommands.Format(
                ScriptVariableContext.ForPlayer(monster, map), "N$命格_目标计数");
            ScriptVariableTextResult untouched = Envir.Main.CSharpScripts.VariableCommands.Format(
                ScriptVariableContext.ForPlayer(sameName, map), "N$命格_目标计数");
            Assert.True(selected.Success);
            Assert.Equal("42", selected.Text);
            Assert.True(untouched.Success);
            Assert.Equal("0", untouched.Text);
        }
        finally
        {
            Envir.Main.Objects.Remove(monster);
            Envir.Main.Objects.Remove(sameName);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格伤害吸收按千分比减伤并消耗护盾总量()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981611, FileName = "LF-FATE-SUCK" });
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格玄武战躯", HP = 1000 },
            CurrentMap = map,
            Stats = new Stats { [Stat.HP] = 1000 },
            HP = 1000,
            ArmourRate = 1,
            DamageRate = 1
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseAct(segment.ActList, "SETSUCKDAMAGE = 1000 100 100");
            Assert.True(segment.Check(player));

            Assert.Equal(90, player.Struck(100, DefenceType.Agility));
            Assert.Equal(910, player.HP);
            Assert.Equal(990, player.LingFengSuckDamageRemaining);

            var replace = Segment();
            replace.ParseAct(replace.ActList, "SETSUCKDAMAGE = 5 1000 100");
            Assert.True(replace.Check(player));
            Assert.Equal(95, player.Struck(100, DefenceType.Agility));
            Assert.Equal(815, player.HP);
            Assert.Equal(0, player.LingFengSuckDamageRemaining);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格范围伤害只命中半径内指定类型并走真实受击链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981612, FileName = "LF-FATE-RANGE" });
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格南明离火" },
            CurrentMap = map,
            CurrentLocation = new Point(20, 20),
            AMode = AttackMode.All,
            Node = new LinkedListNode<MapObject>(null),
            Stats = new Stats()
        };
        FateMonster CreateMonster(string name, Point location) => new(new MonsterInfo
        {
            Index = Envir.Main.MonsterInfoList.Count + 981620,
            Name = name,
            Stats = new Stats { [Stat.HP] = 500 }
        })
        {
            CurrentMap = map,
            CurrentLocation = location,
            Node = new LinkedListNode<MapObject>(null)
        };
        var inside = CreateMonster("范围内怪物", new Point(21, 20));
        var outside = CreateMonster("范围外怪物", new Point(24, 20));
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Objects.AddLast(inside);
            Envir.Main.Objects.AddLast(outside);
            inside.RefreshAll();
            outside.RefreshAll();
            inside.Stats[Stat.HP] = outside.Stats[Stat.HP] = 500;
            inside.ArmourRate = outside.ArmourRate = 1;
            inside.DamageRate = outside.DamageRate = 1;
            inside.HP = outside.HP = 500;

            var segment = Segment();
            segment.ParseAct(segment.ActList, "RANGEHARM 20 20 2 100 0 0 1 2");
            Assert.True(segment.Check(source));

            Assert.True(inside.HP < 500);
            Assert.Equal(500, outside.HP);
            Assert.Same(source, inside.EXPOwner);
        }
        finally
        {
            Envir.Main.Objects.Remove(inside);
            Envir.Main.Objects.Remove(outside);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void L前缀范围伤害以当前伤害来源命中人物并附加冰冻与地图特效()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-rangeharm-{Guid.NewGuid():N}");
        var map = new Map(new MapInfo { Index = 981614, FileName = "LF-RANGEHARM" })
        {
            Width = 100,
            Height = 100
        };
        var source = new FateMonster(new MonsterInfo
        {
            Index = 981615,
            Name = "命格范围伤害来源",
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(20, 19),
            Node = new LinkedListNode<MapObject>(null)
        };
        PacketCapturingPlayerObject CreateTarget(string name, Point location) => new()
        {
            Info = new CharacterInfo { Name = name, HP = 500, MP = 100 },
            CurrentMap = map,
            CurrentLocation = location,
            Node = new LinkedListNode<MapObject>(null),
            Stats = new Stats { [Stat.HP] = 500, [Stat.MP] = 100 },
            HP = 500,
            MP = 100,
            ArmourRate = 1,
            DamageRate = 1
        };
        var executor = CreateTarget("命格范围伤害承受者", new Point(20, 20));
        var nearby = CreateTarget("命格范围伤害邻近人物", new Point(21, 20));
        var outside = CreateTarget("命格范围伤害范围外人物", new Point(23, 20));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllLines(Path.Combine(root, "EffectImageList.txt"),
                Enumerable.Range(0, 49).Select(index => $"Effect{index}.Pak"));
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(source);
            foreach (PlayerObject target in new[] { executor, nearby, outside })
            {
                Envir.Main.Players.Add(target);
                map.Players.Add(target);
            }

            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Incoming,
                source.Name,
                executor.Name,
                source.Name,
                10,
                10,
                true)
            {
                CurrentTargetObjectId = source.ObjectID,
                ActorObjectId = source.ObjectID
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "L.RANGEHARM 20 20 1 40 8 3 1 1 36 5410 9 80 0 0");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(executor));

            Assert.True(executor.HP < 500);
            Assert.True(nearby.HP < 500);
            Assert.Equal(500, outside.HP);
            Assert.Contains(executor.PoisonList, poison => poison.PType == PoisonType.Frozen);
            Assert.Contains(nearby.PoisonList, poison => poison.PType == PoisonType.Frozen);
            Assert.DoesNotContain(outside.PoisonList, poison => poison.PType == PoisonType.Frozen);
            Assert.Contains(executor.Packets.OfType<ServerPackets.LingFengMapEffect>(),
                packet => packet.Location == executor.CurrentLocation &&
                          packet.LibraryName == "Effect36" && packet.StartIndex == 5410 &&
                          packet.FrameCount == 9 && packet.FrameDelay == 80 && !packet.Blend);

            int hpBeforeRejectedEffect = executor.HP;
            int poisonCountBeforeRejectedEffect = executor.PoisonList.Count;
            int packetCountBeforeRejectedEffect = executor.Packets.Count;
            var unsupportedEffect = Segment();
            unsupportedEffect.ParseAct(unsupportedEffect.ActList,
                "L.RANGEHARM 20 20 1 40 6 100 1 1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(unsupportedEffect.Check(executor));
            Assert.Equal(hpBeforeRejectedEffect, executor.HP);
            Assert.Equal(poisonCountBeforeRejectedEffect, executor.PoisonList.Count);
            Assert.Equal(packetCountBeforeRejectedEffect, executor.Packets.Count);
        }
        finally
        {
            foreach (PlayerObject target in new[] { executor, nearby, outside })
            {
                map.Players.Remove(target);
                Envir.Main.Players.Remove(target);
            }
            Envir.Main.Objects.Remove(source);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 命格重燃战火按当前技能三级免蓝免冷却重新施放且不递归()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981613, FileName = "LF-FATE-RELEASE" })
        {
            Width = 3,
            Height = 1,
            Cells = new Cell[3, 1]
        };
        for (int x = 0; x < map.Width; x++)
            map.Cells[x, 0] = new Cell { Attribute = CellAttribute.Walk };

        var magicInfo = new MagicInfo
        {
            Name = "命格火球",
            Spell = Spell.FireBall,
            PowerBase = 100,
            PowerBonus = 10,
            BaseCost = 100,
            DelayBase = 60_000,
            Range = 9
        };
        var learned = new UserMagic(Spell.FireBall)
        {
            Info = magicInfo,
            Level = 0,
            CastTime = Envir.Main.Time
        };
        long learnedCastTime = learned.CastTime;
        var source = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格重燃施法者", MP = 0 },
            CurrentMap = map,
            CurrentLocation = new Point(0, 0),
            Node = new LinkedListNode<MapObject>(null),
            Stats = new Stats { [Stat.MinMC] = 100, [Stat.MaxMC] = 100, [Stat.MP] = 0 },
            MP = 0,
            AMode = AttackMode.All
        };
        source.Info.Magics.Add(learned);
        var target = new FateMonster(new MonsterInfo
        {
            Index = 9816132,
            Name = "命格重燃目标",
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(1, 0),
            Node = new LinkedListNode<MapObject>(null)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Objects.AddLast(target);
            map.Cells[0, 0].Add(source);
            map.Cells[1, 0].Add(target);
            target.RefreshAll();
            target.Stats[Stat.HP] = 1000;
            target.ArmourRate = target.DamageRate = 1;
            target.HP = 1000;
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                source.Name,
                target.Name,
                target.Name,
                1,
                1,
                true,
                true,
                target.CurrentLocation.X,
                target.CurrentLocation.Y,
                MagicId: ((ushort)Spell.FireBall).ToString());

            var regular = Segment();
            regular.ParseAct(regular.ActList,
                $"RELEASEMAGIC {(ushort)Spell.FireBall} 0 3 1 1 0");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(regular.Check(source));
            DelayedAction damageAction = Assert.Single(source.ActionList.Where(action =>
                action.Type == DelayedType.Magic));
            Assert.Equal((byte)3, Assert.IsType<UserMagic>(damageAction.Params[0]).Level);
            Assert.Equal(0, source.MP);
            Assert.Contains(source.Packets, packet => packet is ServerPackets.Magic);

            source.Process(damageAction);
            Assert.True(target.HP < 1000);
            int baselineDamage = 1000 - target.HP;
            Assert.Equal(learnedCastTime, learned.CastTime);

            target.HP = 1000;
            source.ActionList.Clear();
            Assert.True(source.TryChangeLingFengSkillPower(
                (int)Spell.FireBall, "=", new[] { 0, 0, 100, 0, 0, 0 }, 0, false));
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(regular.Check(source));
            DelayedAction enhancedAction = Assert.Single(source.ActionList,
                action => action.Type == DelayedType.Magic);
            source.Process(enhancedAction);
            Assert.True(1000 - target.HP > baselineDamage);

            source.ActionList.Clear();
            source.Packets.Clear();
            var withoutAction = Segment();
            withoutAction.ParseAct(withoutAction.ActList,
                $"RELEASEMAGICEX {(ushort)Spell.FireBall} 0 3 1 1 0");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(withoutAction.Check(source));
            Assert.Single(source.ActionList.Where(action => action.Type == DelayedType.Magic));
            Assert.DoesNotContain(source.Packets, packet => packet is ServerPackets.Magic);
        }
        finally
        {
            Envir.Main.Objects.Remove(target);
            map.Cells[0, 0].Remove(source);
            map.Cells[1, 0].Remove(target);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格脚本参数检测逐项精确匹配当前调用参数()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject();
            var segment = Segment();
            segment.ParseCheck("CHECKSCRIPTPARAM M001,破军,战士");

            using (LingFengTxtTriggerContext.PushScriptParameters(new[] { "M001", "破军", "战士" }))
                Assert.True(segment.Check(player));
            using (LingFengTxtTriggerContext.PushScriptParameters(new[] { "M001", "破军", "法师" }))
                Assert.False(segment.Check(player));
            using (LingFengTxtTriggerContext.PushScriptParameters(new[] { "M001", "破军" }))
                Assert.False(segment.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格查找当前地图最近同名怪物并写回坐标()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981601, FileName = "LF-FATE" });
        var far = new FateMonster(new MonsterInfo { Index = 981602, Name = "命格守卫" })
        {
            CurrentMap = map,
            CurrentLocation = new Point(20, 20)
        };
        var near = new FateMonster(new MonsterInfo { Index = 981603, Name = "命格守卫" })
        {
            CurrentMap = map,
            CurrentLocation = new Point(11, 10)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(far);
            Envir.Main.Objects.AddLast(near);
            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "命格坐标人物" },
                CurrentMap = map,
                CurrentLocation = new Point(10, 10)
            };
            var segment = Segment();
            segment.ParseCheck("FINDMONPOINT LF-FATE 命格守卫 N1 N2");

            Assert.True(segment.Check(player));
            Assert.Equal("11", segment.FindVariable(player, "%N1"));
            Assert.Equal("10", segment.FindVariable(player, "%N2"));

            player.CurrentPoison = PoisonType.Red;
            near.CurrentPoison = PoisonType.Green;
            var stateChecks = Segment();
            stateChecks.ParseCheck("CHECKSTATEVALUE 1");
            stateChecks.ParseCheck("M.CHECKSTATEVALUE 0");
            var damage = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                near.Name,
                near.Name,
                1,
                1,
                false,
                true,
                near.CurrentLocation.X,
                near.CurrentLocation.Y);
            using (LingFengTxtTriggerContext.Push(damage))
                Assert.True(stateChecks.Check(player));

            var stateActions = Segment();
            stateActions.ParseAct(stateActions.ActList, "CHANGESTATE 4 0");
            stateActions.ParseAct(stateActions.ActList, "M.CHANGESTATE 2 3 1");
            using (LingFengTxtTriggerContext.Push(damage))
                Assert.True(stateActions.Check(player));
            Assert.False(player.CurrentPoison.HasFlag(PoisonType.Red));
            Assert.Contains(near.PoisonList, poison => poison.PType == PoisonType.Frozen);
        }
        finally
        {
            Envir.Main.Objects.Remove(far);
            Envir.Main.Objects.Remove(near);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格随机比例保留子值母值边界语义()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject();
            var never = Segment();
            never.ParseCheck("RANDOMEX 0 100");
            Assert.False(never.Check(player));

            var always = Segment();
            always.ParseCheck("RANDOMEX 100 100");
            Assert.True(always.Check(player));

            var invalid = Segment();
            invalid.ParseCheck("RANDOMEX 101 100");
            Assert.False(invalid.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格消息框复用跨端NPC页面协议并保留按钮语法()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject { NPCSpeech = new List<string> { "旧页面" } };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                @"MESSAGEBOX 系统提示\确认洗炼？ <确定/@洗炼(40)> <关闭/@exit>");

            Assert.True(segment.Check(player));
            Assert.Equal(
                @"系统提示\确认洗炼？ <确定/@洗炼(40)> <关闭/@exit>",
                Assert.Single(player.NPCSpeech));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格等级与生命百分比命令通过人物真实状态执行()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格状态人物", Level = 60, HP = 20, MP = 10 },
                Stats = new Stats()
            };
            player.Stats[Stat.HP] = 100;
            player.Stats[Stat.MP] = 50;

            var segment = Segment();
            segment.ParseCheck("CHECKLEVELEX > 59");
            segment.ParseCheck("CHECKHPPER = 20");
            segment.ParseAct(segment.ActList, "ADDHPPER + 10");
            segment.ParseAct(segment.ActList, "ADDMPPER = 50");

            Assert.True(segment.Check(player));
            Assert.Equal(30, player.HP);
            Assert.Equal(25, player.MP);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格死亡归属攻击模式在线与固定生命魔法通过真实人物状态执行()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var online = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "在线目标" }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(online);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格状态人物", HP = 20, MP = 10 },
                Stats = new Stats(),
                AMode = AttackMode.All,
                LastHitter = online
            };
            player.Stats[Stat.HP] = 100;
            player.Stats[Stat.MP] = 50;

            var segment = Segment();
            segment.ParseCheck("KILLBYHUM");
            segment.ParseCheck("CHECKATTACKMODE 0");
            segment.ParseCheck("CHECKONLINE 在线目标");
            segment.ParseCheck("CHECKSTRINGLENGTH 命格A = 5");
            segment.ParseAct(segment.ActList, "HUMANHP + 30 0 1");
            segment.ParseAct(segment.ActList, "HUMANMP - 5 0 1");
            segment.ParseAct(segment.ActList, "HUMANMP = 40");

            Assert.True(segment.Check(player));
            Assert.Equal(50, player.HP);
            Assert.Equal(40, player.MP);

            player.AMode = AttackMode.Peace;
            var peace = Segment();
            peace.ParseCheck("CHECKATTACKMODE 1");
            Assert.True(peace.Check(player));
        }
        finally
        {
            Envir.Main.Players.Remove(online);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明会员转职三个职业复用真实人物职业字段()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格转职人物", Class = MirClass.Warrior }
            };
            player.Info.Magics.Add(new UserMagic(Spell.Fencing));
            player.Info.Magics.Add(new UserMagic(Spell.FireBall));

            var clearSkills = Segment();
            clearSkills.ParseAct(clearSkills.ActList, "CLEARSKILL");
            Assert.True(clearSkills.Check(player));
            Assert.Empty(player.Info.Magics);

            foreach ((string command, MirClass expected) in new[]
                     {
                         ("CHANGEJOB Wizard", MirClass.Wizard),
                         ("CHANGEJOB Taoist", MirClass.Taoist),
                         ("CHANGEJOB Warrior", MirClass.Warrior)
                     })
            {
                var segment = Segment();
                segment.ParseAct(segment.ActList, command);
                Assert.True(segment.Check(player));
                Assert.Equal(expected, player.Info.Class);
            }
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 神秘战场强制全体攻击模式到期恢复原模式且重触发不覆盖基线()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, 981700000L);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "神秘战场人物",
                    AMode = AttackMode.Peace
                }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "SETHUMATTACKMODE 5 2");

            Assert.True(segment.Check(player));
            Assert.Equal(AttackMode.All, player.AMode);
            Assert.False(player.TryChangeAttackModeFromClient(AttackMode.Group));
            Assert.Equal(AttackMode.All, player.AMode);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, Envir.Main.Time + Settings.Second);
            Assert.True(segment.Check(player));
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, Envir.Main.Time + Settings.Second + 1);
            player.Process();
            Assert.Equal(AttackMode.All, player.AMode);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, Envir.Main.Time + Settings.Second);
            player.Process();
            Assert.Equal(AttackMode.Peace, player.AMode);
            Assert.True(player.TryChangeAttackModeFromClient(AttackMode.Group));
            Assert.Equal(AttackMode.Group, player.AMode);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明二至四号账户仓库永久开启并按真实存储长度校验()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明多页仓库人物" },
                Account = new AccountInfo()
            };
            var secondClosed = Segment();
            secondClosed.ParseCheck("NOT CHECKSTORAGEOPEN 2");
            Assert.True(secondClosed.Check(player));

            var openSecond = Segment();
            openSecond.ParseAct(openSecond.ActList, "OPENSTORATGE 2 1");
            Assert.True(openSecond.Check(player));
            Assert.True(player.Account.IsLingFengStorageOpen(2));
            Assert.Equal(2 * Globals.StorageGridSize, player.Account.Storage.Length);
            Assert.True(player.Account.IsValidStorageIndex(2 * Globals.StorageGridSize - 1));

            var openFourth = Segment();
            openFourth.ParseAct(openFourth.ActList, "OPENSTORATGE 4 1");
            Assert.True(openFourth.Check(player));
            Assert.Equal(4 * Globals.StorageGridSize, player.Account.Storage.Length);
            Assert.True(player.Account.IsLingFengStorageOpen(3));
            Assert.True(player.Account.IsLingFengStorageOpen(4));
            Assert.True(player.Account.IsValidStorageIndex(4 * Globals.StorageGridSize - 1));
            Assert.False(player.Account.IsValidStorageIndex(4 * Globals.StorageGridSize));

            var fourthOpen = Segment();
            fourthOpen.ParseCheck("CHECKSTORAGEOPEN 4");
            Assert.True(fourthOpen.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明转生增加持久转数与未分配属性点且等级零保持原等级()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "酷明转生人物",
                    Level = 80
                }
            };
            player.Info.LingFengProgress.SetRenewLevel(7);
            var segment = Segment();
            segment.ParseAct(segment.ActList, "RENEWLEVEL 1 0 25");

            Assert.True(segment.Check(player));
            Assert.Equal(8, player.Info.LingFengProgress.RenewLevel);
            Assert.Equal(25, player.Info.LingFengProgress.RenewPoints);
            Assert.Equal((ushort)80, player.Level);

            Assert.True(segment.Check(player));
            Assert.Equal(9, player.Info.LingFengProgress.RenewLevel);
            Assert.Equal(50, player.Info.LingFengProgress.RenewPoints);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格L前缀职业等级只读取当前伤害事件攻击者()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var attacker = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "事件战士",
                Class = MirClass.Warrior,
                Level = 66,
                HP = 100
            },
            Stats = new Stats { [Stat.HP] = 100 }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(attacker);
            var segment = Segment();
            segment.ParseCheck("L.CHECKJOB Warrior");
            segment.ParseCheck("L.CHECKLEVELEX >= 66");
            segment.ParseAct(segment.ActList, "L.CHANGESTATE 1 1 1");
            segment.ParseAct(segment.ActList, "L.HUMANHP - 10 0 1");
            segment.ParseAct(segment.ActList, "<$KILLER>.CHANGEPKPOINT + 200");
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Incoming,
                attacker.Name,
                "命格承伤者",
                "命格承伤者",
                10,
                10,
                false,
                ActorKind: LingFengCombatActorKind.Player);

            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(new PlayerObject()));
            Assert.Contains(attacker.PoisonList, poison => poison.PType == PoisonType.Paralysis);
            Assert.Equal(90, attacker.HP);
            Assert.Equal(200, attacker.PKPoints);

            var stale = Segment();
            stale.ParseCheck("L.CHECKJOB Warrior");
            Assert.False(stale.Check(new PlayerObject()));
        }
        finally
        {
            Envir.Main.Players.Remove(attacker);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格婚姻与P前缀性别婚姻只读取当前伤害目标人物()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格单身攻击者", Gender = MirGender.女性 }
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "命格已婚男目标",
                Gender = MirGender.男性,
                Married = 9527
            }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);
            var segment = Segment();
            segment.ParseCheck("NOT CHECKMARRY");
            segment.ParseCheck("P.CHECKMARRY");
            segment.ParseCheck("P.GENDER MAN");
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                source.Name,
                target.Name,
                target.Name,
                10,
                10,
                false,
                ActorKind: LingFengCombatActorKind.Player);

            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(source));
            Assert.False(segment.Check(source));
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(target);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格当前目标种类宝宝检测与扩展随机变量读取事件快照()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject { Info = new CharacterInfo { Name = "命格种类人物" } };
            var random = Segment();
            random.ParseAct(random.ActList, "MOVR N1 1 4");
            Assert.True(random.Check(player));
            int randomValue = int.Parse(random.FindVariable(player, "%N1"));
            Assert.InRange(randomValue, 1, 4);

            var playerRace = Segment();
            playerRace.ParseCheck("CHECKCURRTARGETRACE = 0");
            using (LingFengTxtTriggerContext.Push(DamageWithActor(
                       LingFengCombatActorKind.Player)))
                Assert.True(playerRace.Check(player));

            var heroRace = Segment();
            heroRace.ParseCheck("CHECKCURRTARGETRACE = 1");
            using (LingFengTxtTriggerContext.Push(DamageWithActor(
                       LingFengCombatActorKind.Hero)))
                Assert.True(heroRace.Check(player));

            var petRace = Segment();
            petRace.ParseCheck("CHECKCURRTARGETRACE = 151");
            petRace.ParseCheck("CHECKCURRTARGETSLAVE");
            using (LingFengTxtTriggerContext.Push(DamageWithActor(
                       LingFengCombatActorKind.Pet)))
                Assert.True(petRace.Check(player));
            Assert.False(petRace.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }

        static LingFengDamageEvent DamageWithActor(LingFengCombatActorKind actorKind) => new(
            PlayerDamagePerspective.Incoming,
            "命格事件来源",
            "命格种类人物",
            "命格事件来源",
            1,
            1,
            false,
            ActorKind: actorKind);
    }

    [Fact]
    public void 命格元宝检测扣除与额外经验走现有人物账本()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格账本人物",
                    Level = 1,
                    PearlCount = 1_100_000,
                    Experience = 50
                },
                Stats = new Stats(),
                MaxExperience = 1_000
            };
            var segment = Segment();
            segment.ParseCheck("CHECKGAMEGOLD >= 1000000");
            segment.ParseAct(segment.ActList, "GAMEGOLD - 1000000");
            segment.ParseAct(segment.ActList, "CHANGEEXP + 200");

            Assert.True(segment.Check(player));
            Assert.Equal(100_000, player.Info.PearlCount);
            Assert.Equal(250U, player.Experience);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风灵符检测调整常量显示与人物变量持久化形成闭环()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "灵符闭环人物" }
            };
            player.Info.LingFengProgress.SetGameGird(1_000);
            var segment = Segment();
            segment.ParseCheck("CHECKGAMEGIRD ? 500");
            segment.ParseAct(segment.ActList, "GAMEGIRD - 500");

            Assert.True(segment.Check(player));
            Assert.Equal(500, player.Info.LingFengProgress.GameGird);
            Assert.Equal("500", segment.ReplaceValue(player, "<$GAMEGIRD>"));

            var insufficient = Segment();
            insufficient.ParseCheck("CHECKGAMEGIRD ? 600");
            insufficient.ParseAct(insufficient.ActList, "GAMEGIRD - 600");
            Assert.False(insufficient.Check(player));
            Assert.Equal(500, player.Info.LingFengProgress.GameGird);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredStore = new CharacterScriptVariableStore();
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredStore.Load(reader);
            Assert.Equal(500, new LingFengCharacterProgress(restoredStore).GameGird);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明游戏点检测调整常量显示与人物变量持久化形成闭环()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明骰子人物" }
            };
            player.Info.LingFengProgress.SetGamePoint(120);

            var exchange = Segment();
            exchange.ParseCheck("CHECKGAMEPOINT ? 100");
            exchange.ParseAct(exchange.ActList, "GAMEPOINT - 100");
            Assert.True(exchange.Check(player));
            Assert.Equal(20, player.Info.LingFengProgress.GamePoint);
            Assert.Equal("20", exchange.ReplaceValue(player, "<$GAMEPOINT>"));

            var insufficient = Segment();
            insufficient.ParseCheck("CHECKGAMEPOINT >= 100");
            insufficient.ParseAct(insufficient.ActList, "GAMEPOINT - 100");
            Assert.False(insufficient.Check(player));
            Assert.Equal(20, player.Info.LingFengProgress.GamePoint);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredStore = new CharacterScriptVariableStore();
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredStore.Load(reader);
            Assert.Equal(20, new LingFengCharacterProgress(restoredStore).GamePoint);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 迦楼金刚石检测调整常量显示与人物变量持久化形成闭环()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "迦楼金刚石人物" }
            };
            player.Info.LingFengProgress.SetGameDiamond(1_888);

            var exchange = Segment();
            exchange.ParseCheck("CHECKGAMEDIAMOND > 1887");
            exchange.ParseAct(exchange.ActList, "GAMEDIAMOND - 1888");
            Assert.True(exchange.Check(player));
            Assert.Equal(0, player.Info.LingFengProgress.GameDiamond);
            Assert.Equal("0", exchange.ReplaceValue(player, "<$GAMEDIAMOND>"));

            var invalid = Segment();
            invalid.ParseAct(invalid.ActList, "GAMEDIAMOND - 1");
            Assert.True(invalid.Check(player));
            Assert.Equal(0, player.Info.LingFengProgress.GameDiamond);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredStore = new CharacterScriptVariableStore();
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredStore.Load(reader);
            Assert.Equal(0, new LingFengCharacterProgress(restoredStore).GameDiamond);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格穿戴目标护盾与攻城状态读取现有领域状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var conquest = new ConquestObject(new ConquestGuildInfo
        {
            Info = new ConquestInfo { Index = 981608, Name = "命格攻城" }
        });
        typeof(ConquestObject).GetField("AtWar",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .SetValue(conquest, true);
        var castleGuild = new GuildObject(new GuildInfo
        {
            GuildIndex = 981608,
            Name = "命格沙城行会"
        });
        var leaderRank = new GuildRank { Index = 0, Name = "命格城主" };
        castleGuild.Ranks.Add(leaderRank);
        conquest.Guild = castleGuild;
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格穿戴人物" },
            MyGuild = castleGuild,
            MyGuildRank = leaderRank
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格护盾目标" },
            MyGuild = castleGuild
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            source.Info.Equipment[(int)EquipmentSlot.Armour] =
                new UserItem(new ItemInfo { Name = "命格测试衣服" });
            var heroInfo = new HeroInfo
            {
                Name = "命格穿戴英雄",
                Equipment = new UserItem[14]
            };
            source.Hero = new HeroObject(heroInfo, source);
            heroInfo.Equipment[(int)EquipmentSlot.Necklace] =
                new UserItem(new ItemInfo { Name = "英雄幸运项链" });
            var magicShield = (Buff)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(Buff));
            magicShield.Info = new BuffInfo { Type = BuffType.MagicShield };
            magicShield.Stats = new Stats();
            target.Buffs.Add(magicShield);
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);
            Envir.Main.Conquests.Add(conquest);
            var segment = Segment();
            segment.ParseCheck("CHECKUSEITEM 0");
            segment.ParseCheck("H.CHECKUSEITEM 3");
            segment.ParseCheck("P.CHECKSHIELDSTATEOPEN 1");
            segment.ParseCheck("CHECKBATTLESTATUS");
            segment.ParseCheck("CHECKUNDERWAR 命格攻城");
            segment.ParseCheck("ISCASTLEGUILD");
            segment.ParseCheck("ISCASTLEMASTER");
            segment.ParseCheck("M.ISCASTLEGUILD");
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                source.Name,
                target.Name,
                target.Name,
                1,
                1,
                false,
                ActorKind: LingFengCombatActorKind.Player);

            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(source));

            source.MyGuildRank = new GuildRank { Index = 1, Name = "命格普通成员" };
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.False(segment.Check(source));
        }
        finally
        {
            Envir.Main.Conquests.Remove(conquest);
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(target);
            Envir.Main.Guilds.Remove(castleGuild);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明月老检测正对面异性与对面人物等级()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        Map map = WalkableMap(981609, "LF-POSE", 30, 30);
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "月老男方", Gender = MirGender.Male, Level = 40 },
            CurrentMap = map,
            CurrentLocation = new Point(10, 10),
            Direction = MirDirection.Up
        };
        var partner = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "月老女方", Gender = MirGender.Female, Level = 35 },
            CurrentMap = map,
            CurrentLocation = player.Front,
            Direction = MirDirection.Down
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            map.AddObject(player);
            map.AddObject(partner);

            var segment = Segment();
            segment.ParseCheck("CHECKPOSEDIR 2");
            segment.ParseCheck("CHECKPOSELEVEL > 34");
            Assert.True(segment.Check(player));

            partner.Direction = MirDirection.Up;
            Assert.False(segment.Check(player));
        }
        finally
        {
            map.RemoveObject(partner);
            map.RemoveObject(player);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明武馆师徒检测读取既有持久关系()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明武馆徒弟", Mentor = 981609 }
            };
            var segment = Segment();
            segment.ParseCheck("HAVEMASTER");

            Assert.True(segment.Check(player));
            player.Info.Mentor = 0;
            Assert.False(segment.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明月老武馆与地图数量检测读取真实领域状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        Map map = WalkableMap(981644, "LF-SOCIAL-CHECK", 5, 5);
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "酷明社会检测人物", Gender = MirGender.Male,
                Mentor = 9816441, IsMentor = true
            },
            CurrentMap = map,
            CurrentLocation = new Point(2, 2),
            Direction = MirDirection.Up
        };
        var partner = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "酷明社会检测对象", Gender = MirGender.Female,
                Married = 9816442, Mentor = 9816443
            },
            CurrentMap = map,
            CurrentLocation = player.Front
        };
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981644, Name = "酷明地图检测怪",
            Stats = new Stats
            {
                [Stat.HP] = 100,
                [Stat.MinDC] = 1,
                [Stat.MaxDC] = 1
            },
            ViewRange = 5, CoolEye = 5
        });
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            map.AddObject(player);
            map.AddObject(partner);
            monster.CurrentMap = map;
            monster.CurrentLocation = new Point(0, 0);
            Envir.Main.Objects.AddLast(monster);

            var segment = Segment();
            segment.ParseCheck("CHECKPOSEMARRY");
            segment.ParseCheck("CHECKPOSEGENDER 女");
            segment.ParseCheck("CHECKISMASTER");
            segment.ParseCheck("CHECKMASTER");
            segment.ParseCheck("CHECKPOSEMASTER");
            segment.ParseCheck("CHECKMAPHUMANCOUNT LF-SOCIAL-CHECK > 1");
            segment.ParseCheck("CHECKMONMAP LF-SOCIAL-CHECK 1");
            Assert.True(segment.Check(player));

            partner.Info.Mentor = 0;
            Assert.False(segment.Check(player));
        }
        finally
        {
            Envir.Main.Objects.Remove(monster);
            if (map.GetCell(partner.CurrentLocation).Objects?.Contains(partner) == true)
                map.RemoveObject(partner);
            if (map.GetCell(player.CurrentLocation).Objects?.Contains(player) == true)
                map.RemoveObject(player);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明范围人物时间戳与文本替换按官方参数执行()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        Map map = WalkableMap(981645, "LF-TEXT-RANGE", 8, 8);
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明文本人物" },
            NPCObjectID = 981645,
            CurrentMap = map,
            CurrentLocation = new Point(4, 4)
        };
        var nearby = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明范围人物" },
            CurrentMap = map,
            CurrentLocation = new Point(5, 4)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            map.AddObject(player);
            map.AddObject(nearby);

            var segment = Segment();
            segment.ParseCheck("CHECKRANGEHUMCOUNT SELF 0 0 10 > 1");
            segment.ParseAct(segment.ActList,
                "TEXTREPLACE AAABBBAAA AAA 字母 S$替换结果 0 0");
            segment.ParseAct(segment.ActList,
                "TEXTREPLACE aaAA aa 命格 S$单次结果 0 1");
            segment.ParseAct(segment.ActList, "UNIXTOSTR 0 S$时间结果 0");
            Assert.True(segment.Check(player));

            var context = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            Assert.Equal("字母BBB字母", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "S$替换结果").Text);
            Assert.Equal("命格AA", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "S$单次结果").Text);
            Assert.Equal("1970-01-01 00:00:00", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "S$时间结果").Text);
        }
        finally
        {
            if (map.GetCell(nearby.CurrentLocation).Objects?.Contains(nearby) == true)
                map.RemoveObject(nearby);
            if (map.GetCell(player.CurrentLocation).Objects?.Contains(player) == true)
                map.RemoveObject(player);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格性别组员数量与发型别名路由到既有类型化命令()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var female = new PlayerObject { Info = new CharacterInfo { Name = "女命格", Gender = MirGender.Female } };
            var femaleCheck = Segment();
            femaleCheck.ParseCheck("GENDER");
            Assert.True(femaleCheck.Check(female));

            var male = new PlayerObject
            {
                Info = new CharacterInfo { Name = "男命格", Gender = MirGender.Male, Hair = 7 }
            };
            var maleCheck = Segment();
            maleCheck.ParseCheck("GENDER MAN");
            Assert.True(maleCheck.Check(male));

            var groupCheck = Segment();
            groupCheck.ParseCheck("CHECKGROUPMEMBERCOUNT < 2");
            Assert.Equal(CheckType.GroupCount, Assert.Single(groupCheck.CheckList).Type);

            var hair = Segment();
            hair.ParseAct(hair.ActList, "HAIRSTYLE 1");
            Assert.Equal(ActionType.ChangeHair, Assert.Single(hair.ActList).Type);

            var getHair = Segment();
            getHair.ParseAct(getHair.ActList, "GETPLAYINFO Hair N1");
            Assert.True(getHair.Check(male));
            Assert.Equal("7", getHair.FindVariable(male, "%N1"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格免费全身特修恢复可修装备且保留禁止特修装备()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格修理大师" },
                Account = new AccountInfo()
            };
            var repairable = new UserItem(new ItemInfo
            {
                Name = "可修命格武器",
                Price = 10_000,
                Durability = 1_000
            })
            {
                UniqueID = 981611,
                CurrentDura = 100,
                MaxDura = 1000
            };
            var protectedItem = new UserItem(new ItemInfo
            {
                Name = "禁止特修命格衣服",
                Bind = BindMode.NoSRepair
            })
            {
                UniqueID = 981612,
                CurrentDura = 200,
                MaxDura = 1000
            };
            player.Info.Equipment[(int)EquipmentSlot.Weapon] = repairable;
            player.Info.Equipment[(int)EquipmentSlot.Armour] = protectedItem;

            uint expectedCost = checked(repairable.RepairPrice() * 3);
            var cost = Segment();
            cost.ParseCheck("CHECKREPAIRALLGOLD <$STR(N99)>");
            player.Account.Gold = expectedCost - 1;
            Assert.False(cost.Check(player));
            Assert.Equal(expectedCost.ToString(), cost.FindVariable(player, "%N99"));
            player.Account.Gold = expectedCost;
            Assert.True(cost.Check(player));

            var segment = Segment();
            segment.ParseAct(segment.ActList, "RepairAll");
            segment.ParseAct(segment.ActList, "ACTREPAIRALL");
            Assert.True(segment.Check(player));

            Assert.Equal((ushort)1000, repairable.CurrentDura);
            Assert.Equal((ushort)200, protectedItem.CurrentDura);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格临时属性按固定值与原始属性百分比叠加并可覆盖()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格属性人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.RefreshStats();
            int originalMinAc = player.Stats[Stat.MinAC];
            int originalMaxDc = player.Stats[Stat.MaxDC];

            var segment = Segment();
            segment.ParseAct(segment.ActList, "CHANGEHUMABILITY 1 + 35 6 0");
            segment.ParseAct(segment.ActList, "CHANGEHUMABILITY 2 = 40 6");
            segment.ParseAct(segment.ActList, "CHANGEHUMABILITYPERCENTAGE 6 = 90 6");

            Assert.True(segment.Check(player));
            Assert.Equal(originalMinAc + 35, player.Stats[Stat.MinAC]);
            Assert.Equal(40, player.Stats[Stat.MaxAC]);
            Assert.Equal(originalMaxDc * 90 / 100, player.Stats[Stat.MaxDC]);

            var restore = Segment();
            restore.ParseAct(restore.ActList, "CHANGEHUMABILITYPERCENTAGE 6 = 100 0");
            Assert.True(restore.Check(player));
            Assert.Equal(originalMaxDc, player.Stats[Stat.MaxDC]);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格临时属性短效到期只撤销自身并保留长效永久层和基础属性()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格临时属性分层人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.RefreshStats();
            int baseMinAc = player.Stats[Stat.MinAC];

            NPCSegment permanent = Segment("cool-fate-ability-permanent");
            permanent.ParseAct(permanent.ActList, "CHANGEHUMABILITY 1 + 20 0 0");
            Assert.True(permanent.Check(player));

            NPCSegment thirtySeconds = Segment("cool-fate-ability-thirty");
            thirtySeconds.ParseAct(thirtySeconds.ActList, "CHANGEHUMABILITY 1 + 30 30 0");
            Assert.True(thirtySeconds.Check(player));

            NPCSegment tenSeconds = Segment("cool-fate-ability-ten");
            tenSeconds.ParseAct(tenSeconds.ActList, "CHANGEHUMABILITY 1 + 10 10 0");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(baseMinAc + 20 + 30 + 10, player.Stats[Stat.MinAC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(baseMinAc + 20 + 30, player.Stats[Stat.MinAC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(baseMinAc + 20, player.Stats[Stat.MinAC]);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格当前目标公式扣金与临时减防只作用真实受击人物()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = WalkableMap(981730, "LF-FATE-TARGET", 8, 8);
        var source = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "命格施法人物", Class = MirClass.战士, Level = 40, HP = 1, MP = 1
            },
            Account = new AccountInfo { Gold = 500 },
            CurrentMap = map,
            CurrentLocation = new Point(2, 2),
            NPCObjectID = 981730,
            Stats = new Stats()
        };
        var target = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "命格受击人物", Class = MirClass.战士, Level = 40, HP = 1, MP = 1
            },
            Account = new AccountInfo { Gold = 100 },
            CurrentMap = map,
            CurrentLocation = new Point(3, 2),
            Stats = new Stats()
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            source.Info.Mount = new MountInfo(source);
            target.Info.Mount = new MountInfo(target);
            target.Info.Equipment[(int)EquipmentSlot.盔甲] = new UserItem(new ItemInfo
            {
                Index = 981731,
                Name = "命格受击目标防具",
                Type = ItemType.盔甲,
                Stats = new Stats { [Stat.MaxAC] = 100 }
            });
            source.RefreshStats();
            target.RefreshStats();
            int targetMaxAc = target.Stats[Stat.MaxAC];
            Envir.Main.MapList.Add(map);
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);

            var damage = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing, source.Name, target.Name, target.Name,
                10, 10, true)
            {
                CurrentTargetObjectId = target.ObjectID
            };
            var segment = Segment("cool-fate-current-target");
            segment.ParseAct(segment.ActList,
                "<$CURRRTARGETNAME>.FORMULATION <$MaxAc>*7/100 N$减防");
            Assert.Equal(
                ActionType.LingFengTargetFormulation,
                segment.ActList[0].Type);
            segment.ParseAct(segment.ActList,
                "<$CURRRTARGETNAME>.CHANGEHUMABILITY 2 - <$Str(N$减防)> 7");
            segment.ParseAct(segment.ActList,
                "<$CURRRTARGETNAME>.TAKE 金币 30");
            Assert.Equal(
                $"{targetMaxAc}*7/100",
                segment.ReplaceValue(target, "<$MaxAc>*7/100"));

            using (LingFengTxtTriggerContext.Push(damage))
                Assert.True(segment.Check(source));

            int reduction = targetMaxAc * 7 / 100;
            Assert.True(targetMaxAc >= 100);
            Assert.Equal(70U, target.Account.Gold);
            Assert.Equal(reduction.ToString(), Envir.Main.CSharpScripts.VariableCommands.Format(
                ScriptVariableContext.ForConversation(
                    source, source.NPCObjectID, source.CurrentMap), "N$减防").Text);
            Assert.Equal(targetMaxAc - reduction, target.Stats[Stat.MaxAC]);
            Assert.Equal(500U, source.Account.Gold);
        }
        finally
        {
            Envir.Main.Players.Remove(source);
            Envir.Main.Players.Remove(target);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明狂暴死亡奖励只增加真实击杀者元宝与灵符账本()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var victim = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "狂暴死亡人物", HP = 1 },
            Account = new AccountInfo(), Stats = new Stats()
        };
        var killer = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "狂暴击杀人物", HP = 1, PearlCount = 20 },
            Account = new AccountInfo(), Stats = new Stats()
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(killer);
            var segment = Segment("cool-rage-killer-currency");
            segment.ParseAct(segment.ActList, "<$KILLER>.GAMEGIRD + 3");
            segment.ParseAct(segment.ActList, "<$KILLER>.GAMEGOLD + 100");
            var damage = new LingFengDamageEvent(
                PlayerDamagePerspective.Incoming, killer.Name, victim.Name, killer.Name,
                1, 1, true, false, 0, 0, 0, 0, "0",
                LingFengCombatActorKind.Player)
            {
                ActorObjectId = killer.ObjectID
            };
            using (LingFengTxtTriggerContext.Push(damage))
                Assert.True(segment.Check(victim));

            Assert.Equal(3, killer.Info.LingFengProgress.GameGird);
            Assert.Equal(120, killer.Info.PearlCount);
            Assert.Equal(0, victim.Info.LingFengProgress.GameGird);
            Assert.Equal(0, victim.Info.PearlCount);
        }
        finally
        {
            Envir.Main.Players.Remove(killer);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格伤害事件可把中心提示定向发送给当前人物目标()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981701, FileName = "LF-FATE-TARGET-MSG" });
        var actor = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格提示施法者" },
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        var target = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格提示目标" },
            CurrentMap = map,
            CurrentLocation = new Point(1, 0)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(target);
            var segment = Segment("cool-fate-target-center-message");
            segment.ParseAct(segment.ActList,
                "M.SENDCENTERMSG 251 0 命格效果还剩%d秒 0 10");
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                actor.Name,
                target.Name,
                target.Name,
                1,
                1,
                true,
                false,
                target.CurrentLocation.X,
                target.CurrentLocation.Y)
            {
                CurrentTargetObjectId = target.ObjectID
            };

            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(actor));

            ServerPackets.Chat message = Assert.Single(
                target.Packets.OfType<ServerPackets.Chat>());
            Assert.Equal("命格效果还剩%d秒", message.Message);
            Assert.Equal(ChatType.Announcement, message.Type);
            Assert.Empty(actor.Packets);
        }
        finally
        {
            Envir.Main.Players.Remove(target);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明狂暴公告降级时仍按全服受众发送且保留富文本原文()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981702, FileName = "LF-RAGE-LINE-MSG" });
        var actor = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "狂暴公告人物" },
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        var observer = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "狂暴公告观察者" },
            CurrentMap = map,
            CurrentLocation = new Point(20, 20)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(actor);
            Envir.Main.Players.Add(observer);
            var segment = Segment("cool-rage-line-message");
            segment.ParseAct(segment.ActList,
                "SENDNEWLINEMSG 0 254 0 12 300 5 1 玩家{【狂暴公告人物】|250:0:1}开启了狂暴之力！");

            Assert.True(segment.Check(actor));

            foreach (PacketCapturingPlayerObject recipient in new[] { actor, observer })
            {
                ServerPackets.Chat message = Assert.Single(
                    recipient.Packets.OfType<ServerPackets.Chat>());
                Assert.Equal("玩家{【狂暴公告人物】|250:0:1}开启了狂暴之力！", message.Message);
                Assert.Equal(ChatType.Announcement, message.Type);
            }
        }
        finally
        {
            Envir.Main.Players.Remove(actor);
            Envir.Main.Players.Remove(observer);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格同属性不同来源的三十秒与十秒效果独立到期且同一动作只刷新自身()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格分层人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.RefreshStats();
            int original = player.Stats[Stat.MaxDC];

            var longTerm = Segment();
            longTerm.ParseAct(longTerm.ActList, "CHANGEHUMABILITY 6 + 10 0");
            Assert.True(longTerm.Check(player));

            var thirtySeconds = Segment();
            thirtySeconds.ParseAct(thirtySeconds.ActList, "CHANGEHUMABILITY 6 + 20 30");
            Assert.True(thirtySeconds.Check(player));
            Assert.Equal(original + 30, player.Stats[Stat.MaxDC]);

            var tenSeconds = Segment();
            tenSeconds.ParseAct(tenSeconds.ActList, "CHANGEHUMABILITY 6 + 30 10");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(original + 60, player.Stats[Stat.MaxDC]);

            Assert.True(tenSeconds.Check(player));
            Assert.Equal(original + 60, player.Stats[Stat.MaxDC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(original + 30, player.Stats[Stat.MaxDC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(original + 10, player.Stats[Stat.MaxDC]);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格杀怪经验倍率短效到期不清除长效与永久层且保存层可恢复()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new CharacterInfo
            {
                Name = "命格经验倍率人物",
                Class = MirClass.战士,
                Level = 40,
                HP = 1,
                MP = 1
            };
            var player = new SilentPlayerObject
            {
                Info = info,
                Stats = new Stats(),
                MaxExperience = 1_000_000
            };
            info.Mount = new MountInfo(player);
            player.RefreshStats();
            int originalRate = player.Stats[Stat.经验增长数率];

            NPCSegment permanent = Segment("cool-fate-exp-permanent");
            permanent.ParseAct(permanent.ActList, "ADDHUMNEWVALUE 26 + 100 0");
            Assert.True(permanent.Check(player));

            NPCSegment thirtySeconds = Segment("cool-fate-exp-thirty");
            thirtySeconds.ParseAct(thirtySeconds.ActList, "KILLMONEXPRATE 300 30 1 1");
            Assert.True(thirtySeconds.Check(player));

            NPCSegment tenSeconds = Segment("cool-fate-exp-ten");
            tenSeconds.ParseAct(tenSeconds.ActList, "KILLMONEXPRATE 200 10 0 1");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(originalRate + 400, player.Stats[Stat.经验增长数率]);
            Assert.Equal("5|30", permanent.ReplaceValue(
                player, "<$KILLMONEXPRATE>|<$KILLMONEXPRATETIME>"));
            player.MaxExperience = 1_000_000;
            player.GainExp(10);
            Assert.Equal(50U, player.Experience);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(originalRate + 300, player.Stats[Stat.经验增长数率]);
            Assert.Equal("4|19", permanent.ReplaceValue(
                player, "<$KILLMONEXPRATE>|<$KILLMONEXPRATETIME>"));
            player.MaxExperience = 1_000_000;
            player.GainExp(10);
            Assert.Equal(90U, player.Experience);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredInfo = new CharacterInfo
            {
                Name = "命格经验倍率恢复人物",
                Class = MirClass.战士,
                Level = 40,
                HP = 1,
                MP = 1
            };
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredInfo.ScriptVariables.Load(reader);
            var restored = new SilentPlayerObject
            {
                Info = restoredInfo,
                Stats = new Stats(),
                MaxExperience = 1_000_000
            };
            restoredInfo.Mount = new MountInfo(restored);
            restored.RefreshStats();
            Assert.Equal(200, restored.GetLingFengNewValue(26));
            Assert.Equal("3|19", permanent.ReplaceValue(
                restored, "<$KILLMONEXPRATE>|<$KILLMONEXPRATETIME>"));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            restored.RefreshStats();
            Assert.Equal(0, restored.GetLingFengNewValue(26));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格召之即来按人物名设置分身对人物伤害倍率()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldMultithreaded = Settings.Multithreaded;
        long oldTime = Envir.Main.Time;
        int oldMonsterCount = Envir.Main.MonsterCount;
        var map = WalkableMap(981638, "LF-FATE-SELF-CLONE", 10, 10);
        var cloneInfo = new MonsterInfo
        {
            Index = 981639,
            Name = Settings.CloneName,
            Stats = new Stats { [Stat.HP] = 1 }
        };
        SilentPlayerObject owner = null;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.Multithreaded = false;
            Envir.Main.MapList.Add(map);
            Envir.Main.MonsterInfoList.Add(cloneInfo);
            owner = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格召唤者" },
                Stats = new Stats
                {
                    [Stat.HP] = 200,
                    [Stat.MinDC] = 100,
                    [Stat.MaxDC] = 100
                },
                CurrentMap = map,
                CurrentLocation = new Point(5, 5)
            };
            var recall = Segment("cool-fate-recall-self");
            recall.ParseAct(recall.ActList,
                "RECALLSELF 60 1 150 0 0 0 0 0");
            Assert.True(recall.Check(owner));
            MonsterObject clone = Assert.Single(owner.Pets);
            Assert.True(clone.LingFengIsSelfClone);
            Assert.Equal(owner.Name, clone.Name);
            Assert.Equal(150, clone.Stats[Stat.MaxDC]);
            Assert.Equal(300, clone.Stats[Stat.HP]);
            Assert.Equal(oldTime + 60 * Settings.Second,
                clone.LingFengSelfCloneExpireTime);
            var target = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格分身目标", HP = 1000 },
                Stats = new Stats { [Stat.HP] = 1000 },
                ArmourRate = 1,
                DamageRate = 1,
                HP = 1000
            };

            var half = Segment("cool-fate-recall-half");
            half.ParseAct(half.ActList,
                "SETSLAVEATTACKHUMPOWERRATE <$USERNAME> 50");
            Assert.True(half.Check(owner));
            Assert.Equal(50, clone.LingFengAttackHumanPowerRate);
            Assert.Equal(50, target.Attacked(
                clone, 100, (DefenceType)byte.MaxValue));
            Assert.Equal(950, target.HP);

            var disabled = Segment("cool-fate-recall-disabled");
            disabled.ParseAct(disabled.ActList,
                "SETSLAVEATTACKHUMPOWERRATE <$USERNAME> 0");
            Assert.True(disabled.Check(owner));
            Assert.Equal(0, clone.LingFengAttackHumanPowerRate);
            Assert.Equal(0, target.Attacked(
                clone, 100, (DefenceType)byte.MaxValue));
            Assert.Equal(950, target.HP);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 61 * Settings.Second);
            clone.Process();
            Assert.True(clone.Dead);
        }
        finally
        {
            foreach (MonsterObject pet in owner?.Pets.ToArray() ?? Array.Empty<MonsterObject>())
                if (!pet.Dead) pet.Die();
            Envir.Main.MonsterInfoList.Remove(cloneInfo);
            Envir.Main.MapList.Remove(map);
            Envir.Main.MonsterCount = oldMonsterCount;
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.Multithreaded = oldMultithreaded;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格隐藏属性三十秒与十秒来源独立到期且不覆盖长期属性()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格隐藏属性分层人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.RefreshStats();
            int originalHpRate = player.Stats[Stat.生命值数率];

            var permanent = Segment();
            permanent.ParseAct(permanent.ActList, "ADDHUMNEWVALUE 7 + 5");
            Assert.True(permanent.Check(player));

            var thirtySeconds = Segment();
            thirtySeconds.ParseAct(thirtySeconds.ActList, "ADDHUMNEWVALUE 7 + 20 30");
            Assert.True(thirtySeconds.Check(player));

            var tenSeconds = Segment();
            tenSeconds.ParseAct(tenSeconds.ActList, "ADDHUMNEWVALUE 7 + 30 10");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(55, player.GetLingFengNewValue(7));
            Assert.Equal(originalHpRate + 55, player.Stats[Stat.生命值数率]);

            Assert.True(tenSeconds.Check(player));
            Assert.Equal(55, player.GetLingFengNewValue(7));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(25, player.GetLingFengNewValue(7));
            Assert.Equal(originalHpRate + 25, player.Stats[Stat.生命值数率]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(5, player.GetLingFengNewValue(7));
            Assert.Equal(originalHpRate + 5, player.Stats[Stat.生命值数率]);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 装备临时隐藏属性三十秒与十秒效果按动作来源独立到期()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "翎风临时装备属性人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.Info.Equipment[(int)EquipmentSlot.盔甲] = new UserItem(
                new ItemInfo { Name = "分层测试盔甲", Type = ItemType.盔甲 });

            var permanent = Segment("temp-item-permanent");
            permanent.ParseAct(permanent.ActList, "ADDHUMNEWVALUE 1 + 5");
            Assert.True(permanent.Check(player));

            var thirtySeconds = Segment("temp-item-thirty");
            thirtySeconds.ParseAct(thirtySeconds.ActList,
                "SETNEWITEMVALUEEX 0 1 = 20 30");
            Assert.True(thirtySeconds.Check(player));

            var tenSeconds = Segment("temp-item-ten");
            tenSeconds.ParseAct(tenSeconds.ActList,
                "SETNEWITEMVALUEEX 0 1 = 30 10");
            Assert.True(tenSeconds.Check(player));
            Assert.Equal(55, player.GetLingFengNewValue(1));

            Assert.True(tenSeconds.Check(player));
            Assert.Equal(55, player.GetLingFengNewValue(1));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(25, player.GetLingFengNewValue(1));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            player.RefreshStats();
            Assert.Equal(5, player.GetLingFengNewValue(1));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格隐藏攻击增伤经过真实人物攻击怪物伤害链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = WalkableMap(9816061, "LF-FATE-NEW-VALUE-DAMAGE", 3, 1);
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo
            {
                Name = "命格隐藏增伤人物",
                Class = MirClass.战士,
                Level = 40,
                HP = 1,
                MP = 1
            },
            CurrentMap = map,
            CurrentLocation = new Point(0, 0),
            Node = new LinkedListNode<MapObject>(null),
            Stats = new Stats(),
            AMode = AttackMode.All
        };
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 9816062,
            Name = "命格隐藏增伤目标",
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(1, 0),
            Node = new LinkedListNode<MapObject>(null)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            player.Info.Mount = new MountInfo(player);
            player.RefreshStats();
            map.Cells[0, 0].Add(player);
            map.Cells[1, 0].Add(monster);
            monster.RefreshAll();
            monster.Stats[Stat.HP] = 1000;
            monster.HP = 1000;
            monster.ArmourRate = monster.DamageRate = 1;

            var segment = Segment();
            segment.ParseAct(segment.ActList, "ADDHUMNEWVALUE 1 + 50 30");
            Assert.True(segment.Check(player));

            int applied = monster.Attacked(
                player, 100, (DefenceType)byte.MaxValue, damageWeapon: false);
            Assert.Equal(150, applied);
            Assert.Equal(850, monster.HP);
        }
        finally
        {
            map.Cells[0, 0].Remove(player);
            map.Cells[1, 0].Remove(monster);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public async Task 命格倍率效果按来源独立到期且短效不清除长效与永久层()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格倍率分层人物" },
                Stats = new Stats()
            };

            NPCSegment permanent = Segment("cool-fate-rate-permanent");
            permanent.ParseAct(permanent.ActList, "POWERRATE 120 0 0 1 2");
            permanent.ParseAct(permanent.ActList, "KILLMONBURSTRATE 120 0 0 1");
            Assert.True(permanent.Check(player));

            NPCSegment thirtySeconds = Segment("cool-fate-rate-thirty");
            thirtySeconds.ParseAct(thirtySeconds.ActList, "POWERRATE 200 30 1 1 2");
            thirtySeconds.ParseAct(thirtySeconds.ActList, "KILLMONBURSTRATE 200 30 1 1");
            Assert.True(thirtySeconds.Check(player));

            NPCSegment tenSeconds = Segment("cool-fate-rate-ten");
            tenSeconds.ParseAct(tenSeconds.ActList, "POWERRATE 150 10 0 1 2");
            tenSeconds.ParseAct(tenSeconds.ActList, "KILLMONBURSTRATE 150 10 0 1");
            Assert.True(tenSeconds.Check(player));

            Assert.Equal(270, player.GetLingFengPowerRatePercent(targetIsHuman: false));
            Assert.Equal(270, player.GetLingFengDropRatePercent());
            Assert.Equal(2.7F, await Task.Run(() =>
                player.ApplyLingFengDropRate(1F)), 3);
            Assert.Equal("2|0|2|0", permanent.ReplaceValue(player,
                "<$ATTACKMONPOWERRATE>|<$ATTACKMONPOWERRATETIME>|" +
                "<$KILLMONBURSTRATE>|<$KILLMONBURSTRATETIME>"));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                player.Info.ScriptVariables.Save(writer);
            stream.Position = 0;
            var restoredInfo = new CharacterInfo { Name = "命格倍率恢复人物" };
            using (var reader = new BinaryReader(
                       stream, System.Text.Encoding.UTF8, leaveOpen: true))
                restoredInfo.ScriptVariables.Load(reader);
            var restored = new SilentPlayerObject
            {
                Info = restoredInfo,
                Stats = new Stats()
            };
            Assert.Equal(200, restored.GetLingFengPowerRatePercent(targetIsHuman: false));
            Assert.Equal(200, restored.GetLingFengDropRatePercent());
            Assert.Equal(30, restored.GetLingFengPowerRateRemainingSeconds(targetIsHuman: false));
            Assert.Equal(30, restored.GetLingFengDropRateRemainingSeconds());

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            Assert.Equal(2.2F, await Task.Run(() =>
                player.ApplyLingFengDropRate(1F)), 3);
            Assert.Equal(220, player.GetLingFengPowerRatePercent(targetIsHuman: false));
            Assert.Equal(220, player.GetLingFengDropRatePercent());
            Assert.Equal("2|0|2|0", permanent.ReplaceValue(player,
                "<$ATTACKMONPOWERRATE>|<$ATTACKMONPOWERRATETIME>|" +
                "<$KILLMONBURSTRATE>|<$KILLMONBURSTRATETIME>"));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 31 * Settings.Second);
            Assert.Equal(1.2F, await Task.Run(() =>
                player.ApplyLingFengDropRate(1F)), 3);
            Assert.Equal(120, player.GetLingFengPowerRatePercent(targetIsHuman: false));
            Assert.Equal(120, player.GetLingFengDropRatePercent());
            Assert.Equal("1|0|1|0", permanent.ReplaceValue(player,
                "<$ATTACKMONPOWERRATE>|<$ATTACKMONPOWERRATETIME>|" +
                "<$KILLMONBURSTRATE>|<$KILLMONBURSTRATETIME>"));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格攻击暴击与杀怪爆率倍率经过真实伤害和掉落链()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        float oldDropRate = Settings.DropRate;
        var map = WalkableMap(9816063, "LF-FATE-COMBAT-RATES", 5, 5);
        var item = new ItemInfo { Index = 9816064, Name = "命格倍率掉落凭证" };
        var info = new MonsterInfo
        {
            Index = 9816065,
            Name = "命格倍率目标",
            Stats = new Stats { [Stat.HP] = 2000 },
            Drops = new List<DropInfo>
            {
                new() { Chance = 2, Item = item }
            }
        };
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格倍率人物" },
            CurrentMap = map,
            CurrentLocation = new Point(1, 1),
            Node = new LinkedListNode<MapObject>(null),
            Stats = new Stats(),
            AMode = AttackMode.All
        };
        var monster = new FateMonster(info)
        {
            CurrentMap = map,
            CurrentLocation = new Point(2, 1),
            Node = new LinkedListNode<MapObject>(null),
            EXPOwner = player
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = false;
            Settings.DropRate = 1;
            map.Cells[1, 1].Add(player);
            map.Cells[2, 1].Add(monster);
            monster.RefreshAll();
            monster.Stats[Stat.HP] = 2000;
            monster.HP = 2000;
            monster.ArmourRate = monster.DamageRate = 1;

            NPCSegment power = Segment("cool-fate-real-power");
            power.ParseAct(power.ActList, "POWERRATE 200 30 0 1 2");
            Assert.True(power.Check(player));
            Assert.Equal(200, monster.Attacked(
                player, 100, (DefenceType)byte.MaxValue, damageWeapon: false));

            NPCSegment blast = Segment("cool-fate-real-blast");
            blast.ParseAct(blast.ActList, "SETBLASTHITRATE 120 30");
            Assert.True(blast.Check(player));
            player.Stats[Stat.暴击倍率] = 100;
            player.Stats[Stat.暴击伤害] = 0;
            Assert.Equal(240, monster.Attacked(
                player, 100, (DefenceType)byte.MaxValue, damageWeapon: false));

            NPCSegment burst = Segment("cool-fate-real-drop");
            burst.ParseAct(burst.ActList, "KILLMONBURSTRATE 200 30 0 1");
            Assert.True(burst.Check(player));
            monster.DropForTest();
            Assert.Contains(map.Cells.Cast<Cell>().SelectMany(
                    cell => cell.Objects ?? new List<MapObject>()),
                value => value is ItemObject dropped && dropped.Item.Info == item);
        }
        finally
        {
            map.Cells[1, 1].Remove(player);
            map.Cells[2, 1].Remove(monster);
            Settings.DropRate = oldDropRate;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风个人定时器按间隔派发QManage且关闭后不再执行()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-personal-timer-{Guid.NewGuid():N}");
        NPCScript loadedManage = null;
        Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QManage.txt"),
                "[@ONTIMER22]\n#ACT\nGIVEGOLD 1\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            loadedManage = NPCScript.GetOrAdd(
                0, "SystemScripts/QManage", NPCScriptType.Called);

            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格个人定时器人物" },
                Account = new AccountInfo()
            };
            var start = Segment();
            start.ParseAct(start.ActList, "SETONTIMER 22 10");
            Assert.True(start.Check(player));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 9 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            Assert.Equal(0u, player.Account.Gold);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 10 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            Assert.Equal(1u, player.Account.Gold);

            var stop = Segment();
            stop.ParseAct(stop.ActList, "SETOFFTIMER 22");
            Assert.True(stop.Check(player));
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 30 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            Assert.Equal(1u, player.Account.Gold);

            var finite = Segment();
            finite.ParseAct(finite.ActList, "SETONTIMER 22 10 2");
            Assert.True(finite.Check(player));
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 40 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 50 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 60 * Settings.Second);
            player.ProcessLingFengPersonalTimers();
            Assert.Equal(3u, player.Account.Gold);
        }
        finally
        {
            if (loadedManage != null) Envir.Main.Scripts.Remove(loadedManage.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 酷明副本倒计时到期执行原脚本标签且换图取消不误触发()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-delay-message-{Guid.NewGuid():N}");
        NPCScript script = null;
        var sourceMap = new Map(new MapInfo
            { Index = 981706, FileName = "LF-COOL-DELAY-SOURCE" });
        var otherMap = new Map(new MapInfo
            { Index = 981707, FileName = "LF-COOL-DELAY-OTHER" });
        Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@START]\n#ACT\n" +
                "SENDDELAYMSG 你将在%s秒后进入下一层 3 250 1 @NEXT\n" +
                "[@NEXT]\n#ACT\nGIVEGOLD 7\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(
                0, "SystemScripts/QFunction-0", NPCScriptType.Called);

            var player = new CapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明副本倒计时人物" },
                Account = new AccountInfo(),
                CurrentMap = sourceMap,
                NPCScriptID = script.ScriptID
            };
            Assert.True(script.CallSystem(player, "[@START]"));
            Assert.Contains(player.Messages, message =>
                message.Type == ChatType.Announcement &&
                message.Text == "你将在%s秒后进入下一层");
            DelayedAction cancelled = Assert.Single(player.ActionList,
                action => action.Type == DelayedType.LingFengDelayedMessage);
            player.CurrentMap = otherMap;
            player.Process(cancelled);
            Assert.Equal(0u, player.Account.Gold);

            player.ActionList.Clear();
            player.CurrentMap = sourceMap;
            player.NPCScriptID = script.ScriptID;
            Assert.True(script.CallSystem(player, "[@START]"));
            DelayedAction completed = Assert.Single(player.ActionList,
                action => action.Type == DelayedType.LingFengDelayedMessage);
            player.Process(completed);
            Assert.Equal(7u, player.Account.Gold);
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 酷明回城石参数一只清除中央倒计时而不误清脚本跳转()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明回城石人物" }
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var message = new DelayedAction(
                DelayedType.LingFengDelayedMessage, Envir.Main.Time + 5000);
            var navigation = new DelayedAction(
                DelayedType.NPC, Envir.Main.Time + 5000);
            player.ActionList.Add(message);
            player.ActionList.Add(navigation);

            var clearMessage = Segment();
            clearMessage.ParseAct(clearMessage.ActList, "CLEARDELAYGOTO 1");
            Assert.True(clearMessage.Check(player));
            Assert.True(message.FlaggedToRemove);
            Assert.False(navigation.FlaggedToRemove);

            var clearNavigation = Segment();
            clearNavigation.ParseAct(clearNavigation.ActList, "CLEARDELAYGOTO");
            Assert.True(clearNavigation.Check(player));
            Assert.True(navigation.FlaggedToRemove);
        }
        finally
        {
            player.ActionList.Clear();
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明命格当前目标即时与七秒延迟页只在目标人物执行()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-target-page-{Guid.NewGuid():N}");
        NPCScript script = null;
        PlayerObject source = null;
        PlayerObject target = null;
        var map = new Map(new MapInfo { Index = 981708, FileName = "LF-COOL-TARGET-PAGE" });
        Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QFunction-0.txt"),
                "[@START]\n#ACT\n" +
                "<$CURRRTARGETNAME>.GOTO @SHIELD\n" +
                "<$CURRRTARGETNAME>.DELAYGOTO 7000 @RESTORE\n" +
                "[@SHIELD]\n#ACT\nGIVEGOLD 3\n" +
                "[@RESTORE]\n#ACT\nGIVEGOLD 7\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(
                0, "SystemScripts/QFunction-0", NPCScriptType.Called);
            source = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格跳转施法者", HP = 1 },
                Account = new AccountInfo(), CurrentMap = map,
                NPCScriptID = script.ScriptID
            };
            target = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格跳转目标", HP = 1 },
                Account = new AccountInfo(), CurrentMap = map,
                NPCObjectID = 9988, NPCScriptID = 7788,
                NPCPage = new NPCPage("[@OLD]")
            };
            Envir.Main.Players.Add(source);
            Envir.Main.Players.Add(target);
            var damage = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing, source.Name, target.Name, target.Name,
                1, 1, true) { CurrentTargetObjectId = target.ObjectID };
            using (LingFengTxtTriggerContext.Push(damage))
                Assert.True(script.CallSystem(source, "[@START]"));

            DelayedAction immediate = Assert.Single(target.ActionList,
                action => action.Type == DelayedType.LingFengTargetPage &&
                          action.Time == oldTime);
            target.Process(immediate);
            Assert.Equal(3U, target.Account.Gold);
            Assert.Equal(0U, source.Account.Gold);
            Assert.Equal(9988U, target.NPCObjectID);
            Assert.Equal(7788, target.NPCScriptID);
            Assert.Equal("[@OLD]", target.NPCPage.Key);

            DelayedAction delayed = Assert.Single(target.ActionList,
                action => action.Type == DelayedType.LingFengTargetPage &&
                          action.Time == oldTime + 7000);
            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 7000);
            target.Process(delayed);
            Assert.Equal(10U, target.Account.Gold);
        }
        finally
        {
            if (source != null) Envir.Main.Players.Remove(source);
            if (target != null) Envir.Main.Players.Remove(target);
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 翎风LoopGoto同步执行指定次数且EndLoop只终止当前循环()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(),
            $"lfenv16-loop-goto-{Guid.NewGuid():N}");
        NPCScript script = null;
        Directory.CreateDirectory(Path.Combine(root, "SystemScripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "SystemScripts", "QManage.txt"),
                "[@MAIN]\n#ACT\nLOOPGOTO @STEP 5\nGIVEGOLD 10\n" +
                "[@STEP]\n#ACT\nGIVEGOLD 1\n" +
                "[@BREAKTEST]\n#ACT\nLOOPGOTO @BREAKSTEP 5\nGIVEGOLD 20\n" +
                "[@BREAKSTEP]\n#ACT\nGIVEGOLD 1\nENDLOOP\nGIVEGOLD 100\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LyoCrystal;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            script = NPCScript.GetOrAdd(0, "SystemScripts/QManage", NPCScriptType.Called);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格同步循环人物" },
                Account = new AccountInfo()
            };

            Assert.True(script.CallSystem(player, "[@MAIN]"));
            Assert.Equal(15u, player.Account.Gold);
            Assert.True(script.CallSystem(player, "[@BREAKTEST]"));
            Assert.Equal(36u, player.Account.Gold);
        }
        finally
        {
            if (script != null) Envir.Main.Scripts.Remove(script.ScriptID);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 命格M前缀怪物百分比减益按来源独立到期()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var map = new Map(new MapInfo { Index = 981606, FileName = "LF-FATE-DEBUFF" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981607,
            Name = "命格减益目标",
            Stats = new Stats { [Stat.MinAC] = 100, [Stat.MaxAC] = 100 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(12, 12)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            monster.RefreshAll();
            monster.HP = monster.Stats[Stat.HP];
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格减益施法者" },
                CurrentMap = map,
                CurrentLocation = new Point(11, 12)
            };
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                monster.Name,
                monster.Name,
                10,
                10,
                true,
                true,
                monster.CurrentLocation.X,
                monster.CurrentLocation.Y);

            var fixedFiveSeconds = Segment();
            fixedFiveSeconds.ParseAct(fixedFiveSeconds.ActList,
                "M.CHANGEHUMABILITY 2 - 10 5 1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(fixedFiveSeconds.Check(player));
            Assert.Equal(90, monster.Stats[Stat.MaxAC]);

            var sevenSeconds = Segment();
            sevenSeconds.ParseAct(sevenSeconds.ActList,
                "M.CHANGEHUMABILITYPERCENTAGE 1 = 93 7");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(sevenSeconds.Check(player));

            var threeSeconds = Segment();
            threeSeconds.ParseAct(threeSeconds.ActList,
                "M.CHANGEHUMABILITYPERCENTAGE 1 = 90 3");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(threeSeconds.Check(player));
            Assert.Equal(90, monster.Stats[Stat.MinAC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 4 * Settings.Second);
            monster.RefreshAll();
            Assert.Equal(93, monster.Stats[Stat.MinAC]);
            Assert.Equal(90, monster.Stats[Stat.MaxAC]);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 8 * Settings.Second);
            monster.RefreshAll();
            Assert.Equal(100, monster.Stats[Stat.MinAC]);
            Assert.Equal(100, monster.Stats[Stat.MaxAC]);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Envir.Main.Objects.Remove(monster);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明脱战触发按地图怪物名恢复最大当前生命与攻击上下限()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 9816071, FileName = "LF-LEAVE-COMBAT" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 9816072,
            Name = "酷明脱战魔物",
            Stats = new Stats
            {
                [Stat.HP] = 100,
                [Stat.MinDC] = 10,
                [Stat.MaxDC] = 20
            }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(12, 12)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            monster.RefreshAll();
            monster.HP = 25;
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明脱战触发者" },
                CurrentMap = map
            };

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "CHANGEMONABILITY SELF 酷明脱战魔物 1 = 200 0");
            segment.ParseAct(segment.ActList,
                "CHANGEMONABILITY SELF 酷明脱战魔物 0 = 150 0");
            segment.ParseAct(segment.ActList,
                "CHANGEMONABILITY SELF 酷明脱战魔物 8 = 30 0");
            segment.ParseAct(segment.ActList,
                "CHANGEMONABILITY SELF 酷明脱战魔物 9 = 40 0");
            Assert.True(segment.Check(player));

            Assert.Equal(200, monster.Stats[Stat.HP]);
            Assert.Equal(150, monster.HP);
            Assert.Equal(30, monster.Stats[Stat.MinDC]);
            Assert.Equal(40, monster.Stats[Stat.MaxDC]);

            monster.Stats[Stat.MaxDC] = 1;
            var recalc = Segment("cool-leave-combat-recalc");
            recalc.ParseAct(recalc.ActList,
                "RECALCMONABILITY SELF 酷明脱战魔物");
            Assert.True(recalc.Check(player));
            Assert.Equal(40, monster.Stats[Stat.MaxDC]);
        }
        finally
        {
            Envir.Main.Objects.Remove(monster);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明副本进入触发按动态地图清理全部真实地面物品()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 9816073, FileName = "LF-CLEAR-ITEM" })
        {
            Width = 2,
            Height = 1,
            Cells = new Cell[2, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        map.Cells[1, 0] = new Cell { Attribute = CellAttribute.Walk };
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明副本清理人物" },
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        var first = new ItemObject(player,
            new UserItem(new ItemInfo { Name = "副本遗留甲", Type = ItemType.杂物 }),
            Point.Empty);
        var second = new ItemObject(player,
            new UserItem(new ItemInfo { Name = "副本遗留乙", Type = ItemType.杂物 }),
            new Point(1, 0));
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            map.GetCell(first.CurrentLocation).Add(first);
            map.GetCell(second.CurrentLocation).Add(second);
            first.Spawned();
            second.Spawned();

            var named = Segment();
            named.ParseAct(named.ActList,
                "CLEARITEMMAP SELF 0 0 0 副本遗留甲");
            Assert.True(named.Check(player));
            Assert.Null(first.Node);
            Assert.NotNull(second.Node);

            var all = Segment();
            all.ParseAct(all.ActList, "CLEARITEMMAP SELF");
            Assert.True(all.Check(player));
            Assert.Null(second.Node);
            Assert.DoesNotContain(
                Envir.Main.Objects, value => value is ItemObject item && item.CurrentMap == map);
        }
        finally
        {
            if (first.Node != null)
            {
                map.RemoveObject(first);
                first.Despawn();
            }
            if (second.Node != null)
            {
                map.RemoveObject(second);
                second.Despawn();
            }
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明战场清图让野怪进入真实死亡链且保留人物宝宝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldMultithreaded = Settings.Multithreaded;
        var map = new Map(new MapInfo { Index = 9816074, FileName = "02SMZC" })
        {
            Width = 2,
            Height = 1,
            Cells = new Cell[2, 1]
        };
        map.Cells[0, 0] = new Cell { Attribute = CellAttribute.Walk };
        map.Cells[1, 0] = new Cell { Attribute = CellAttribute.Walk };
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "酷明清图人物" },
            CurrentMap = map,
            CurrentLocation = Point.Empty
        };
        var wild = new FateMonster(new MonsterInfo
        {
            Name = "酷明战场野怪",
            Stats = new Stats { [Stat.HP] = 100 }
        });
        var pet = new FateMonster(new MonsterInfo
        {
            Name = "酷明战场宝宝",
            Stats = new Stats { [Stat.HP] = 100 }
        }) { Master = player };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.Multithreaded = false;
            Envir.Main.MapList.Add(map);
            Assert.True(wild.Spawn(map, Point.Empty));
            Assert.True(pet.Spawn(map, new Point(1, 0)));

            var segment = Segment();
            segment.ParseAct(segment.ActList, "CLEARMAPMON 02SMZC");
            Assert.True(segment.Check(player));
            Assert.True(wild.Dead);
            Assert.False(pet.Dead);
        }
        finally
        {
            foreach (MonsterObject monster in new MonsterObject[] { wild, pet })
            {
                if (monster.Node == null) continue;
                map.RemoveObject(monster);
                monster.Despawn();
            }
            Envir.Main.MapList.Remove(map);
            Settings.Multithreaded = oldMultithreaded;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格M前缀中毒只作用当前事件怪物并保留固定与千分比威力()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        var map = new Map(new MapInfo { Index = 981608, FileName = "LF-FATE-POISON" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981609,
            Name = "命格中毒目标",
            Stats = new Stats
            {
                [Stat.HP] = 1000,
                [Stat.MinAC] = 100,
                [Stat.MaxAC] = 100
            }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(22, 22)
        };
        var lastAttacker = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格真实攻击来源" },
            Stats = new Stats { [Stat.HP] = 500, [Stat.MaxDC] = 77 },
            CurrentMap = map,
            CurrentLocation = new Point(20, 22)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            Envir.Main.Objects.AddLast(lastAttacker);
            Envir.Main.Players.Add(lastAttacker);
            monster.Info.AI = 144;
            Envir.Main.MonsterInfoList.Add(monster.Info);
            monster.RefreshAll();
            monster.HP = monster.Stats[Stat.HP];
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格中毒施法者" },
                CurrentMap = map,
                CurrentLocation = new Point(21, 22)
            };
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                monster.Name,
                monster.Name,
                10,
                10,
                true,
                true,
                monster.CurrentLocation.X,
                monster.CurrentLocation.Y);

            var red = Segment();
            red.ParseAct(red.ActList, "M.MakePosion 1 300 10 1 0");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(red.Check(player));
            Poison redPoison = Assert.Single(monster.PoisonList);
            Assert.Equal(PoisonType.Red, redPoison.PType);
            Assert.Equal(300, redPoison.Duration);
            Assert.Equal(10, redPoison.Value);
            Assert.Equal(90, monster.ApplyLingFengRedArmourForTest(100));

            var ability = Segment();
            ability.ParseAct(ability.ActList, "M.GetObjectAbilityEx 5 N1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(ability.Check(player));
            Assert.Equal("100", ability.FindVariable(player, "%N1"));

            var incoming = payload with
            {
                Perspective = PlayerDamagePerspective.Incoming,
                AttackerName = lastAttacker.Name,
                ActorKind = LingFengCombatActorKind.Player,
                ActorObjectId = lastAttacker.ObjectID
            };
            var lastAbility = Segment();
            lastAbility.ParseAct(lastAbility.ActList, "L.GetObjectAbilityEx 9 N3");
            using (LingFengTxtTriggerContext.Push(incoming))
                Assert.True(lastAbility.Check(player));
            Assert.Equal("77", lastAbility.FindVariable(player, "%N3"));

            var lastPoison = Segment();
            lastPoison.ParseAct(lastPoison.ActList, "L.MakePosion 0 30 10 0");
            using (LingFengTxtTriggerContext.Push(incoming))
                Assert.True(lastPoison.Check(player));
            Assert.Contains(lastAttacker.PoisonList,
                poison => poison.PType == PoisonType.Green && poison.Value == 10);

            var databaseField = Segment();
            databaseField.ParseAct(databaseField.ActList,
                "GetDBMonsterFieldValue <$CURRRTARGETNAME> Race N2");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(databaseField.Check(player));
            Assert.Equal("144", databaseField.FindVariable(player, "%N2"));

            var green = Segment();
            green.ParseAct(green.ActList, "M.MakePosion 0 30 10 1 1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(green.Check(player));
            Poison greenPoison = Assert.Single(monster.PoisonList,
                poison => poison.PType == PoisonType.Green);
            Assert.Equal(10, greenPoison.Value);
            Assert.Equal(Settings.Second, greenPoison.TickSpeed);

            var paralysis = Segment();
            paralysis.ParseAct(paralysis.ActList, "M.MakePosion 5 2 0 1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(paralysis.Check(player));
            Assert.Contains(monster.PoisonList,
                poison => poison.PType == PoisonType.Paralysis && poison.Duration == 2);

            var timedHp = Segment();
            timedHp.ParseAct(timedHp.ActList,
                "M.HumanHP - 20 1000 3 1 0 0 1");
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(timedHp.Check(player));
            Assert.Equal(1000, monster.HP);
            Assert.Single(monster.ActionList,
                action => action.Type == DelayedType.LingFengResource);

            for (int tick = 1; tick <= 3; tick++)
            {
                typeof(Envir).GetProperty(nameof(Envir.Time))!
                    .SetValue(Envir.Main, oldTime + tick * Settings.Second);
                monster.RunNextLingFengResourceForTest();
                Assert.Equal(1000 - tick * 20, monster.HP);
                Assert.Equal(LingFengCombatActorKind.Player,
                    monster.LingFengLastDamageActorKind);
            }
            Assert.DoesNotContain(monster.ActionList,
                action => action.Type == DelayedType.LingFengResource);

            int poisonCount = monster.PoisonList.Count;
            Assert.True(red.Check(player));
            Assert.Equal(poisonCount, monster.PoisonList.Count);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Envir.Main.MonsterInfoList.Remove(monster.Info);
            Envir.Main.Players.Remove(lastAttacker);
            Envir.Main.Objects.Remove(lastAttacker);
            Envir.Main.Objects.Remove(monster);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格M前缀等级血量比例与安全区检测读取真实当前对象状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981630, FileName = "LF-FATE-TARGET-CHECK" });
        var monster = new FateMonster(new MonsterInfo
        {
            Index = 981631,
            Name = "命格检测目标",
            Level = 99,
            Stats = new Stats { [Stat.HP] = 1000 }
        })
        {
            CurrentMap = map,
            CurrentLocation = new Point(32, 32)
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(monster);
            monster.RefreshAll();
            monster.HP = 300;
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格检测人物" },
                CurrentMap = map,
                CurrentLocation = new Point(31, 32),
                InSafeZone = true
            };
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing,
                player.Name,
                monster.Name,
                monster.Name,
                10,
                10,
                true,
                true,
                monster.CurrentLocation.X,
                monster.CurrentLocation.Y);
            var segment = Segment();
            segment.ParseCheck("M.CHECKLEVELEX = 99");
            segment.ParseCheck("M.CHECKHPPER < 35");
            segment.ParseCheck("INSAFEZONE");

            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(player));

            monster.HP = 500;
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.False(segment.Check(player));
            monster.HP = 300;
            player.InSafeZone = false;
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.False(segment.Check(player));
            Assert.False(segment.Check(player));
        }
        finally
        {
            Envir.Main.Objects.Remove(monster);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风地图范围与同名怪物数量检查使用真实存活对象()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo { Index = 981632, FileName = "LF-MONSTER-COUNT" });
        var exact = new FateMonster(new MonsterInfo
        {
            Index = 981633,
            Name = "圣兽A",
            Stats = new Stats { [Stat.HP] = 100 }
        }) { CurrentMap = map, CurrentLocation = new Point(30, 30) };
        var numbered = new FateMonster(new MonsterInfo
        {
            Index = 981634,
            Name = "圣兽A2",
            Stats = new Stats { [Stat.HP] = 100 }
        }) { CurrentMap = map, CurrentLocation = new Point(35, 35) };
        var outside = new FateMonster(new MonsterInfo
        {
            Index = 981635,
            Name = "范围外怪物",
            Stats = new Stats { [Stat.HP] = 100 }
        }) { CurrentMap = map, CurrentLocation = new Point(80, 80) };
        var petOwner = new SilentPlayerObject { Info = new CharacterInfo { Name = "计数宝宝主人" } };
        var pet = new FateMonster(new MonsterInfo
        {
            Index = 981636,
            Name = "计数宝宝",
            Stats = new Stats { [Stat.HP] = 100 }
        }) { CurrentMap = map, CurrentLocation = new Point(60, 60), Master = petOwner };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(exact);
            Envir.Main.Objects.AddLast(numbered);
            Envir.Main.Objects.AddLast(outside);
            Envir.Main.Objects.AddLast(pet);
            exact.RefreshAll();
            numbered.RefreshAll();
            outside.RefreshAll();
            pet.RefreshAll();
            exact.HP = exact.MaxHealth;
            numbered.HP = numbered.MaxHealth;
            outside.HP = outside.MaxHealth;
            pet.HP = pet.MaxHealth;
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "地图计数人物" },
                NPCObjectID = 981637,
                CurrentMap = map,
                CurrentLocation = new Point(30, 30)
            };
            var range = Segment();
            range.ParseCheck("CHECKRANGEMONCOUNT LF-MONSTER-COUNT 30 30 10 = 2");
            var exactName = Segment();
            exactName.ParseCheck("CHECKMAPSAMEMONCOUNT LF-MONSTER-COUNT 圣兽A = 1");
            var ignoreSuffix = Segment();
            ignoreSuffix.ParseCheck("CHECKMAPSAMEMONCOUNT LF-MONSTER-COUNT 圣兽A = 2 1");
            var excludePets = Segment();
            excludePets.ParseCheck("CHECKMAPMONCOUNT LF-MONSTER-COUNT = 3 1");
            var includePets = Segment();
            includePets.ParseCheck("CHECKMAPMONCOUNT LF-MONSTER-COUNT = 4 0");

            Assert.True(range.Check(player));
            Assert.True(exactName.Check(player));
            Assert.True(ignoreSuffix.Check(player));
            Assert.True(excludePets.Check(player));
            Assert.True(includePets.Check(player));
            var getMapCount = Segment();
            getMapCount.ParseAct(getMapCount.ActList,
                "GETMAPMONCOUNT LF-MONSTER-COUNT 1 N$地图怪物数量");
            Assert.True(getMapCount.Check(player));
            var variableContext = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            Assert.Equal("3", Envir.Main.CSharpScripts.VariableCommands
                .Format(variableContext, "N$地图怪物数量").Text);

            numbered.Dead = true;
            Assert.False(range.Check(player));
            Assert.True(exactName.Check(player));
            Assert.False(ignoreSuffix.Check(player));
        }
        finally
        {
            Envir.Main.Objects.Remove(exact);
            Envir.Main.Objects.Remove(numbered);
            Envir.Main.Objects.Remove(outside);
            Envir.Main.Objects.Remove(pet);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格CSV按候选快照读取引号字段并返回首行与末行索引()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-csv-{Guid.NewGuid():N}");
        string csvDirectory = Path.Combine(root, "QuestDiary", "11命格系统", "11命格配置");
        Directory.CreateDirectory(csvDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(csvDirectory, "Tf.Csv"),
                "破军,\"攻击,防御\",70\r\n七杀,生命,60\r\n破军,末行,50\r\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            Assert.NotNull(Envir.Main.PhysicalCsvContentProvider);
            Assert.Contains(Envir.Main.TextFileProvider.GetAll(), definition =>
                definition.SourcePath.EndsWith("Tf.Csv", StringComparison.OrdinalIgnoreCase));

            var player = new PlayerObject();
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                @"CSVOPENCACHE ..\QuestDiary\11命格系统\11命格配置\Tf.Csv");
            segment.ParseAct(segment.ActList,
                @"CSVFINDTEXTROW ..\QuestDiary\11命格系统\11命格配置\Tf.Csv 破军 0~2 0 0 N1");
            segment.ParseAct(segment.ActList,
                @"CSVFINDTEXTROW ..\QuestDiary\11命格系统\11命格配置\Tf.Csv 破军 0~2 0 1 N2");

            Assert.True(segment.Check(player));
            Assert.Equal("0", segment.FindVariable(player, "%N1"));
            Assert.Equal("2", segment.FindVariable(player, "%N2"));

            LingFengCsvContentProvider published = Envir.Main.PhysicalCsvContentProvider!;
            File.WriteAllText(
                Path.Combine(csvDirectory, "Tf.Csv"),
                "破军,\"未闭合\r\n");

            InvalidDataException invalid = Assert.Throws<InvalidDataException>(
                Envir.Main.ApplyPhysicalTextFileDefinitions);
            Assert.Contains("LFENV16-CSV-001", invalid.Message, StringComparison.Ordinal);
            Assert.Same(published, Envir.Main.PhysicalCsvContentProvider);

            var afterRejectedReload = Segment();
            afterRejectedReload.ParseAct(afterRejectedReload.ActList,
                @"CSVFINDTEXTROW ..\QuestDiary\11命格系统\11命格配置\Tf.Csv 破军 0~2 0 1 N3");
            Assert.True(afterRejectedReload.Check(player));
            Assert.Equal("2", afterRejectedReload.FindVariable(player, "%N3"));
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 命格文本名单随机行拆分与权重抽取只读取已发布候选()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-text-{Guid.NewGuid():N}");
        string dataDirectory = Path.Combine(root, "QuestDiary", "命格数据");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            File.WriteAllText(Path.Combine(dataDirectory, "首领分档.txt"),
                ";候选名单\r\n首领甲\tM03\r\n首领乙\tM04\r\n");
            File.WriteAllText(Path.Combine(dataDirectory, "候选.txt"), "七杀命格");
            File.WriteAllText(Path.Combine(dataDirectory, "逐行读取.txt"),
                "完整一行\r\n破军:42\r\n");
            File.WriteAllText(Path.Combine(dataDirectory, "命格配置.ini"),
                "[破军]\r\n伤害倍率=135\r\n说明=旧配置\r\n" +
                "[破军]\r\n伤害倍率=140\r\n说明=命格独立配置\r\n");
            File.WriteAllText(Path.Combine(root, "EffectImageList.Txt"),
                "NewopUi.Pak\r\nTianFu.Pak\r\nBuffIcon.Pak\r\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Assert.NotNull(Envir.Main.PhysicalTextDataProvider);
            Assert.True(Envir.Main.PhysicalTextDataProvider!.TryGet(
                @"..\EffectImageList.Txt", out TextFileDefinition effectImages));
            Assert.Contains("TianFu.Pak", effectImages.Lines);

            var player = new PlayerObject { NPCObjectID = 981646 };
            var segment = Segment();
            segment.ParseCheck(
                @"CHECKTEXTLIST ..\QuestDiary\命格数据\首领分档.txt 首领甲 M03");
            segment.ParseCheck(
                @"GETSTRINGPOSEX ..\EffectImageList.Txt TianFu.Pak N5 S5 0 1");
            segment.ParseAct(segment.ActList,
                @"GETSTRINGPOSEX ..\EffectImageList.Txt BuffIcon.Pak N$资源编号 S$资源名字 0 1");
            segment.ParseAct(segment.ActList,
                @"GETRANDOMLINETEXT ..\QuestDiary\命格数据\候选.txt <$STR(S1)>");
            segment.ParseAct(segment.ActList,
                "EXTRACTSTRING | 天魁|地煞|紫微 S36 S37 S38");
            segment.ParseAct(segment.ActList,
                "RANDOMSPLIT 蓝#1 0 S2 2 S3");
            segment.ParseAct(segment.ActList,
                @"READCONFIGFILEITEM ..\QuestDiary\命格数据\命格配置.ini 破军 伤害倍率 S4");
            segment.ParseAct(segment.ActList,
                @"READCACHECONFIGFILEITEM ..\QuestDiary\命格数据\命格配置.ini 破军 说明 S6");
            segment.ParseAct(segment.ActList,
                @"GETLISTSTRING ..\QuestDiary\命格数据\逐行读取.txt 0 S7");
            segment.ParseAct(segment.ActList,
                @"GETLISTSTRING ..\QuestDiary\命格数据\逐行读取.txt 1 S8 N8");

            Assert.True(segment.Check(player));
            Assert.Equal("七杀命格", segment.FindVariable(player, "%S1"));
            Assert.Equal("140", segment.FindVariable(player, "%S4"));
            Assert.Equal("命格独立配置", segment.FindVariable(player, "%S6"));
            Assert.Equal("完整一行", segment.FindVariable(player, "%S7"));
            Assert.Equal("破军", segment.FindVariable(player, "%S8"));
            Assert.Equal("42", segment.FindVariable(player, "%N8"));
            Assert.Equal("天魁", segment.FindVariable(player, "%S36"));
            Assert.Equal("地煞", segment.FindVariable(player, "%S37"));
            Assert.Equal("紫微", segment.FindVariable(player, "%S38"));
            Assert.Equal("蓝", segment.FindVariable(player, "%S2"));
            Assert.Equal(string.Empty, segment.FindVariable(player, "%S3"));
            Assert.Equal("1", segment.FindVariable(player, "%N5"));
            Assert.Equal("TianFu.Pak", segment.FindVariable(player, "%S5"));
            var context = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            Assert.Equal("2", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "N$资源编号").Text);
            Assert.Equal("BuffIcon.Pak", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "S$资源名字").Text);

            var missing = Segment();
            missing.ParseCheck(
                @"CHECKTEXTLIST ..\QuestDiary\命格数据\首领分档.txt 首领甲 M09");
            Assert.False(missing.Check(player));
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 酷明命格缓存配置独立更新热重载保持并在停服保存()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-cacheini-{Guid.NewGuid():N}");
        string dataDirectory = Path.Combine(root, "QuestDiary", "命格数据");
        string iniPath = Path.Combine(dataDirectory, "运行配置.ini");
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        System.Text.Encoding cp936 = System.Text.Encoding.GetEncoding(936,
            System.Text.EncoderFallback.ExceptionFallback,
            System.Text.DecoderFallback.ExceptionFallback);
        const string original = ";命格缓存\r\n[破军]\r\n当前层数=1\r\n说明=长期属性\r\n";
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(iniPath, original, cp936);
        try
        {
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();

            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "酷明命格缓存人物" },
                NPCObjectID = 981647
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                @"WRITECACHECONFIGFILEITEM ..\QuestDiary\命格数据\运行配置.ini 破军 当前层数 9");
            segment.ParseAct(segment.ActList,
                @"READCACHECONFIGFILEITEM ..\QuestDiary\命格数据\运行配置.ini 破军 当前层数 S1");
            Assert.True(segment.Check(player));
            Assert.Equal("9", segment.FindVariable(player, "%S1"));
            Assert.Equal(original, File.ReadAllText(iniPath, cp936));

            // Cache 文件在引擎运行期只认内存写入；普通 TXT 热重载不能冲掉该值。
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            var afterReload = Segment();
            afterReload.ParseAct(afterReload.ActList,
                @"READCACHECONFIGFILEITEM ..\QuestDiary\命格数据\运行配置.ini 破军 当前层数 S2");
            Assert.True(afterReload.Check(player));
            Assert.Equal("9", afterReload.FindVariable(player, "%S2"));

            Assert.False(Envir.Main.PhysicalTextDataProvider!.TryWriteCachedConfigValue(
                @"..\..\..\..\Mir通用配置\越界.ini", "命格", "值", "1"));
            Assert.True(Envir.Main.FlushLingFengCachedConfigWrites());
            string persisted = File.ReadAllText(iniPath, cp936);
            Assert.Contains(";命格缓存\r\n", persisted, StringComparison.Ordinal);
            Assert.Contains("当前层数=9\r\n", persisted, StringComparison.Ordinal);
            Assert.Contains("说明=长期属性\r\n", persisted, StringComparison.Ordinal);
            Assert.False(File.Exists(iniPath + ".lfcache.tmp"));
        }
        finally
        {
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 酷明账号周印通过AddTextList持久化增量并被CheckTextList读取()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string uniqueDirectory = $"命格周印-{Guid.NewGuid():N}";
        string scriptPath = $@"..\QuestDiary\{uniqueDirectory}\账号周印.txt";
        string runtimeDirectory = Path.GetFullPath(Path.Combine(
            Settings.ConfigPath, "LingFengRuntime", "TextLists", "questdiary",
            uniqueDirectory.ToLowerInvariant()));
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PlayerObject
            {
                Info = new CharacterInfo { Name = "酷明命格周印人物" }
            };
            var writer = Segment();
            writer.ParseAct(writer.ActList,
                $"ADDTEXTLIST {scriptPath} 账号甲 已领取");
            writer.ParseAct(writer.ActList,
                $"ADDTEXTLIST {scriptPath} 账号甲 已领取");
            Assert.True(writer.Check(player));

            var persisted = Segment();
            persisted.ParseCheck($"CHECKTEXTLIST {scriptPath} 账号甲 已领取");
            Assert.True(persisted.Check(player));

            var cached = Segment();
            cached.ParseCheck($"CHECKCACHETEXTLIST {scriptPath} 账号甲 已领取");
            Assert.True(cached.Check(player));

            var missing = Segment();
            missing.ParseCheck($"CHECKCACHETEXTLIST {scriptPath} 账号乙 已领取");
            Assert.False(missing.Check(player));

            var absoluteRejected = Segment();
            absoluteRejected.ParseAct(absoluteRejected.ActList,
                $"ADDTEXTLIST {scriptPath} 账号乙 已领取 1");
            Assert.True(absoluteRejected.Check(player));
            Assert.False(missing.Check(player));

            string jsonPath = Path.Combine(runtimeDirectory, "账号周印.json");
            Assert.True(File.Exists(jsonPath));
            Assert.Single(System.Text.Json.JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(jsonPath))!);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true);
        }
    }

    [Fact]
    public void 翎风AddTextListEx按指定行写入隔离存储且不访问宿主绝对路径()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string externalPath = $@"Z:\宿主禁区\命格记录-{Guid.NewGuid():N}.txt";
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(externalPath))).ToLowerInvariant();
        string runtimeFile = Path.Combine(Settings.ConfigPath, "LingFengRuntime",
            "TextLists", "external", hash + ".json");
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject { Info = new CharacterInfo { Name = "命格文本人物" } };
            var segment = Segment();
            segment.ParseAct(segment.ActList, $"ADDTEXTLISTEX {externalPath} 第一行 0 1");
            segment.ParseAct(segment.ActList, $"ADDTEXTLISTEX {externalPath} 覆盖行 0 1");
            segment.ParseAct(segment.ActList, $"ADDTEXTLISTEX {externalPath} 第三行 2 1");
            Assert.True(segment.Check(player));

            Assert.Equal(new[] { "覆盖行", string.Empty, "第三行" },
                Envir.Main.GetLingFengRuntimeTextListValues(externalPath));
            Assert.False(File.Exists(externalPath));
            Assert.True(File.Exists(runtimeFile));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            if (File.Exists(runtimeFile)) File.Delete(runtimeFile);
        }
    }

    [Fact]
    public void 翎风地图特效从已发布资源表解析并广播完整播放参数()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        LingFengDependencyLevel oldDependency = Settings.TxtScriptsDependencyLevel;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-mapeffect-{Guid.NewGuid():N}");
        var map = new Map(new MapInfo { Index = 981643, FileName = "LF-MAPEFFECT" })
        {
            Width = 100,
            Height = 100
        };
        var player = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格地图特效人物" },
            CurrentMap = map,
            CurrentLocation = new Point(12, 15)
        };
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "EffectImageList.txt"),
                "NewopUi.Pak\r\nTianFu.Pak\r\nBuffIcon.Pak\r\n");
            Settings.CSharpScriptsEnabled = false;
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsDependencyLevel = LingFengDependencyLevel.None;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.MapList.Add(map);
            map.Players.Add(player);

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "MAPEFFECT LF-MAPEFFECT 12 15 1 360 12 -1 300 1 4 87 2");
            Assert.True(segment.Check(player));

            ServerPackets.LingFengMapEffect packet = Assert.Single(
                player.Packets.OfType<ServerPackets.LingFengMapEffect>());
            Assert.Equal(new Point(12, 15), packet.Location);
            Assert.Equal("TianFu", packet.LibraryName);
            Assert.Equal(360, packet.StartIndex);
            Assert.Equal(12, packet.FrameCount);
            Assert.Equal(-1, packet.RepeatCount);
            Assert.Equal(300, packet.FrameDelay);
            Assert.True(packet.Blend);
            Assert.Equal((byte)4, packet.Light);
            Assert.Equal(87, packet.EffectId);
            Assert.Equal((byte)2, packet.Layer);
            Assert.NotEmpty(packet.GetPacketBytes());
        }
        finally
        {
            map.Players.Remove(player);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsDependencyLevel = oldDependency;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 人物数值检测支持别名与双边界并拒绝非法参数()
    {
        var segment = Segment();
        segment.ParseCheck("CHECKLEVEL >= 40");
        segment.ParseCheck("CHECKJOB 战士");
        segment.ParseCheck("CHECKPKPOINTEX < 100");
        segment.ParseCheck("CHECKEXP >= 1000 < 2000");
        segment.ParseCheck("CHECKHP > 0 <= 500");
        segment.ParseCheck("CHECKMP = 25");

        Assert.Equal(
            new[]
            {
                CheckType.Level, CheckType.CheckClass, CheckType.CheckPkPoint,
                CheckType.CheckExperience, CheckType.CheckHP, CheckType.CheckMP
            },
            segment.CheckList.Select(check => check.Type));
        Assert.Equal(new[] { ">=", "1000", "<", "2000" }, segment.CheckList[3].Params);

        Assert.True(LingFengNumericCommandExecutor.TryCheck(
            1500, segment.CheckList[3].Params, out bool matched, out _));
        Assert.True(matched);
        Assert.True(LingFengNumericCommandExecutor.TryCheck(
            2000, segment.CheckList[3].Params, out matched, out _));
        Assert.False(matched);
        Assert.False(LingFengNumericCommandExecutor.TryCheck(
            1, new[] { "??", "1" }, out _, out string invalidOperator));
        Assert.Contains("操作符", invalidOperator);
    }

    [Fact]
    public void GoldCount和ChangePkPoint形成类型化动作()
    {
        var segment = Segment();
        segment.ParseAct(segment.ActList, "GOLDCOUNT + 100");
        segment.ParseAct(segment.ActList, "GOLDCOUNT = 200");
        segment.ParseAct(segment.ActList, "CHANGEPKPOINT - 50");

        Assert.Collection(segment.ActList,
            action =>
            {
                Assert.Equal(ActionType.ChangeGold, action.Type);
                Assert.Equal(new[] { "+", "100" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.ChangeGold, action.Type);
                Assert.Equal(new[] { "=", "200" }, action.Params);
            },
            action =>
            {
                Assert.Equal(ActionType.ChangePkPoint, action.Type);
                Assert.Equal(new[] { "-", "50" }, action.Params);
            });
    }

    [Fact]
    public void 真实语料高频金币和基础物品命令保持既有路由()
    {
        var segment = Segment();
        segment.ParseCheck("CHECKGOLD >= 100");
        segment.ParseCheck("CHECKITEM 回城卷 2");
        segment.ParseAct(segment.ActList, "GIVEGOLD 50");
        segment.ParseAct(segment.ActList, "TAKEGOLD 25");
        segment.ParseAct(segment.ActList, "GIVEITEM 回城卷 2");
        segment.ParseAct(segment.ActList, "TAKEITEM 回城卷 1");

        Assert.Equal(new[] { CheckType.CheckGold, CheckType.CheckItem },
            segment.CheckList.Select(check => check.Type));
        Assert.Equal(new[]
        {
            ActionType.GiveGold, ActionType.TakeGold, ActionType.GiveItem, ActionType.TakeItem
        }, segment.ActList.Select(action => action.Type));
        Assert.Equal(5u, LingFengNumericCommandExecutor.PlanGoldGain(uint.MaxValue - 5, 20));
        Assert.Equal(10u, LingFengNumericCommandExecutor.PlanGoldTake(10, 20));
    }

    [Fact]
    public void 翎风状态物品按实例保存七项限制且不会并入普通物品堆叠()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        ItemInfo[] oldItems = Envir.Main.ItemInfoList.ToArray();
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new ItemInfo
            {
                Index = 981698,
                Name = "命格状态试炼石",
                Type = ItemType.CraftingMaterial,
                StackSize = 20,
                Durability = 1000
            };
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.Add(info);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格状态物品人物" },
                Stats = new Stats()
            };
            player.Report = new Reporting(player);
            var connection = (Server.MirNetwork.MirConnection)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                    typeof(Server.MirNetwork.MirConnection));
            connection.SentItemInfo = [info];
            connection.SentHeroInfo = [];
            player.Connection = connection;
            player.Info.Inventory[0] = new UserItem(info) { Count = 2 };

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "GIVESTATEITEM 命格状态试炼石 1 1 1 1 1 1 1 5");
            Assert.True(segment.Check(player));

            UserItem stateItem = Assert.Single(player.Info.Inventory,
                item => item != null && item.LingFengBindingFlags != BindMode.None);
            Assert.Equal((ushort)5, stateItem.Count);
            Assert.Equal((ushort)2, player.Info.Inventory[0].Count);
            Assert.True(stateItem.HasBindingFlag(BindMode.DontDrop));
            Assert.True(stateItem.HasBindingFlag(BindMode.DontTrade));
            Assert.True(stateItem.HasBindingFlag(BindMode.DontStore));
            Assert.True(stateItem.HasBindingFlag(BindMode.DontRepair));
            Assert.True(stateItem.HasBindingFlag(BindMode.DontSell));
            Assert.True(stateItem.HasBindingFlag(BindMode.DontDeathdrop));
            Assert.True(stateItem.HasBindingFlag(BindMode.DestroyOnDrop));

            UserItem ordinaryItem = player.Info.Inventory[0];
            player.MergeItem(MirGridType.Inventory, MirGridType.Inventory,
                stateItem.UniqueID, ordinaryItem.UniqueID);
            Assert.Equal((ushort)5, stateItem.Count);
            Assert.Equal((ushort)2, ordinaryItem.Count);
            Assert.Contains(stateItem, player.Info.Inventory);

            var customized = new UserItem(info)
            {
                UniqueID = 98169801,
                Count = 3
            };
            var plain = new UserItem(info)
            {
                UniqueID = 98169802,
                Count = 4
            };
            Assert.True(customized.TrySetLingFengCustomText("不可丢失的实例说明", 7));
            Assert.True(customized.TryChangeLingFengUpgradeCount("=", 5));
            Assert.True(customized.TrySetLingFengItemEffect(0, 88));
            player.Info.Inventory[2] = customized;
            player.Info.Inventory[3] = plain;

            player.MergeItem(MirGridType.Inventory, MirGridType.Inventory,
                customized.UniqueID, plain.UniqueID);

            Assert.Equal((ushort)3, customized.Count);
            Assert.Equal((ushort)4, plain.Count);
            Assert.Same(customized, player.Info.Inventory[2]);
            Assert.Same(plain, player.Info.Inventory[3]);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                stateItem.Save(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = new UserItem(reader, Envir.Version, Envir.CustomVersion)
            {
                Info = info
            };
            Assert.Equal(stateItem.LingFengBindingFlags, restored.LingFengBindingFlags);
        }
        finally
        {
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.AddRange(oldItems);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风刚发物品链接支持负一位置并在清除后立即失效()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        ItemInfo[] oldItems = Envir.Main.ItemInfoList.ToArray();
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new ItemInfo
            {
                Index = 981699,
                Name = "命格链接试炼石",
                Type = ItemType.CraftingMaterial,
                StackSize = 1,
                Durability = 1000
            };
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.Add(info);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格链接人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            var connection = (Server.MirNetwork.MirConnection)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                    typeof(Server.MirNetwork.MirConnection));
            connection.SentItemInfo = [info];
            connection.SentHeroInfo = [];
            player.Connection = connection;

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "GIVESTATEITEM 命格链接试炼石 1 0 0 1 1 1 1 1");
            segment.ParseAct(segment.ActList, "LINKGIVEITEM");
            segment.ParseAct(segment.ActList, "SETITEMEFFECT -1 218 2");
            segment.ParseAct(segment.ActList, "UPDATEITEM -1");
            segment.ParseAct(segment.ActList, "CLEARLINKITEM");
            segment.ParseAct(segment.ActList, "SETITEMEFFECT -1 219 1");

            Assert.True(segment.Check(player));
            UserItem linkedItem = Assert.Single(player.Info.Inventory,
                item => item?.Info == info);
            Assert.Equal((ushort)218, linkedItem.GetLingFengItemEffect(2));
            Assert.Equal((ushort)0, linkedItem.GetLingFengItemEffect(1));
        }
        finally
        {
            Envir.Main.ItemInfoList.Clear();
            Envir.Main.ItemInfoList.AddRange(oldItems);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void TakeBagItem按真实背包批量回收并按实际数量结算金币()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var firstInfo = new ItemInfo { Name = "命格回收甲", StackSize = 20 };
            var secondInfo = new ItemInfo { Name = "命格回收乙", StackSize = 20 };
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格回收人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Account = new AccountInfo { Gold = 50 },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.Info.Inventory[0] = new UserItem(firstInfo) { UniqueID = 981700, Count = 2 };
            player.Info.Inventory[1] = new UserItem(secondInfo) { UniqueID = 981701, Count = 3 };

            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "TAKEBAGITEM 命格回收甲|命格回收乙 4 0 100 0 0 N50 0 1 0");
            Assert.True(segment.Check(player));

            Assert.Null(player.Info.Inventory[0]);
            Assert.Equal((ushort)1, player.Info.Inventory[1].Count);
            Assert.Equal("4", segment.FindVariable(player, "%N50"));
            Assert.Equal(450u, player.Account.Gold);

            var unsupported = Segment();
            unsupported.ParseAct(unsupported.ActList,
                "TAKEBAGITEM 命格回收乙 1 1 0 0 0 N51 0");
            Assert.True(unsupported.Check(player));
            Assert.Equal((ushort)1, player.Info.Inventory[1].Count);
            Assert.Equal(450u, player.Account.Gold);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明异兽孵化按数据库Idx范围回收背包并写入实际数量()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var common = new ItemInfo { Index = 712, Name = "异兽普通蛋", StackSize = 20 };
            var rare = new ItemInfo { Index = 718, Name = "异兽稀有蛋", StackSize = 20 };
            var excluded = new ItemInfo { Index = 720, Name = "异兽另一组蛋", StackSize = 20 };
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "酷明异兽孵化人物" },
                Account = new AccountInfo(),
                Stats = new Stats()
            };
            player.Info.Inventory[0] = new UserItem(common) { UniqueID = 981703, Count = 2 };
            player.Info.Inventory[1] = new UserItem(rare) { UniqueID = 981704, Count = 1 };
            player.Info.Inventory[2] = new UserItem(excluded) { UniqueID = 981705, Count = 4 };

            var segment = Segment("cool-beast-hatch-take-by-index");
            segment.ParseAct(segment.ActList,
                "TAKEBAGITEMEX 712-718 999 0 0 0 0 N52 0 0 * 0");

            Assert.True(segment.Check(player));
            Assert.Null(player.Info.Inventory[0]);
            Assert.Null(player.Info.Inventory[1]);
            Assert.Equal((ushort)4, player.Info.Inventory[2].Count);
            Assert.Equal("3", segment.FindVariable(player, "%N52"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 酷明脱战触发临时修改当前地图显示名并刷新同图人物()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = new Map(new MapInfo
        {
            Index = 981703,
            FileName = "LF-COMBAT-DESC",
            Title = "原地图名称"
        });
        var actor = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "脱战提示人物" },
            CurrentMap = map
        };
        var observer = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "脱战提示观察者" },
            CurrentMap = map
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.Players.Add(actor);
            Envir.Main.Players.Add(observer);
            var segment = Segment("cool-combat-map-description");
            segment.ParseAct(segment.ActList, "CHANGEMAPDESC 脱战状态还剩30秒 0");

            Assert.True(segment.Check(actor));
            Assert.Equal("原地图名称", map.Info.Title);
            Assert.Equal("脱战状态还剩30秒", map.Info.GetDisplayTitle());
            Assert.All(new[] { actor, observer }, recipient =>
            {
                ServerPackets.MapInformation packet = Assert.Single(
                    recipient.Packets.OfType<ServerPackets.MapInformation>());
                Assert.Equal("脱战状态还剩30秒", packet.Title);
            });

            var unsupportedPersistence = Segment("cool-combat-map-description-save");
            unsupportedPersistence.ParseAct(
                unsupportedPersistence.ActList, "CHANGEMAPDESC 不应保存 1");
            Assert.True(unsupportedPersistence.Check(actor));
            Assert.Equal("脱战状态还剩30秒", map.Info.GetDisplayTitle());
        }
        finally
        {
            Envir.Main.Players.Remove(actor);
            Envir.Main.Players.Remove(observer);
            map.Info.LingFengRuntimeTitle = string.Empty;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 账号名单按登录账号持久登记并与角色名隔离()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldCSharpEnabled = Settings.CSharpScriptsEnabled;
        string sourcePath = $@"..\QuestDiary\命格账号名单\{Guid.NewGuid():N}.txt";
        Assert.True(LingFengScriptReferenceResolver.TryResolveCandidateTextKey(
            sourcePath, out string key));
        string storedPath = Path.Combine(
            Settings.NameListPath, "LingFengAccountLists",
            key.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.CSharpScriptsEnabled = false;
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "同账号角色甲" },
                Account = new AccountInfo { AccountID = "命格账号-981702" }
            };
            var before = Segment();
            before.ParseCheck($"CHECKACCOUNTLIST {sourcePath}");
            Assert.False(before.Check(player));

            var add = Segment();
            add.ParseAct(add.ActList, $"ADDACCOUNTLIST {sourcePath}");
            Assert.True(add.Check(player));

            var after = Segment();
            after.ParseCheck($"CHECKACCOUNTLIST {sourcePath}");
            Assert.True(after.Check(player));

            var otherAccount = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "同账号角色甲" },
                Account = new AccountInfo { AccountID = "命格账号-其他" }
            };
            Assert.False(after.Check(otherAccount));

            var remove = Segment();
            remove.ParseAct(remove.ActList, $"DELACCOUNTLIST {sourcePath}");
            Assert.True(remove.Check(player));
            Assert.False(after.Check(player));
        }
        finally
        {
            if (File.Exists(storedPath)) File.Delete(storedPath);
            Settings.CSharpScriptsEnabled = oldCSharpEnabled;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 带时效人物名单按独立路径续期并输出剩余日时分()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            string sourcePath = @"..\QuestDiary\命格会员\会员名单.txt";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格会员人物" }
            };
            var add = Segment();
            add.ParseAct(add.ActList,
                $"ADDNAMEDATETIMELIST {sourcePath} 0 1 30");
            Assert.True(add.Check(player));

            var check = Segment();
            check.ParseCheck(
                $"CHECKNAMEDATETIMELIST {sourcePath} 1 S61 N61 N62 N63");
            Assert.True(check.Check(player));
            Assert.False(string.IsNullOrWhiteSpace(check.FindVariable(player, "%S61")));
            Assert.Equal("0", check.FindVariable(player, "%N61"));
            Assert.Equal("1", check.FindVariable(player, "%N62"));
            Assert.Equal("30", check.FindVariable(player, "%N63"));

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 91L * 60L * 1000L);
            Assert.False(check.Check(player));
            Assert.Equal(string.Empty, check.FindVariable(player, "%S61"));
            Assert.Equal("0", check.FindVariable(player, "%N61"));
            Assert.Equal("0", check.FindVariable(player, "%N62"));
            Assert.Equal("0", check.FindVariable(player, "%N63"));
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风逐技能威力区分人物怪物和防御并从人物持久变量恢复()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        long oldTime = Envir.Main.Time;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var info = new CharacterInfo { Name = "命格技能威力人物" };
            var player = new SilentPlayerObject { Info = info };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "SETSKILLPOWER 12 = 30 2 40 3 50 4 0 1");
            Assert.True(segment.Check(player));

            int playerDamage = 100;
            int playerArmour = 20;
            Assert.True(player.TryApplyLingFengSkillPower(
                12, false, ref playerDamage, ref playerArmour, true, false));
            Assert.Equal(132, playerDamage);
            Assert.Equal(20, playerArmour);

            int monsterDamage = 100;
            int monsterArmour = 20;
            Assert.True(player.TryApplyLingFengSkillPower(
                12, true, ref monsterDamage, ref monsterArmour, true, false));
            Assert.Equal(143, monsterDamage);

            var restoredPlayer = new SilentPlayerObject { Info = info };
            int incomingDamage = 100;
            int defence = 20;
            Assert.True(restoredPlayer.TryApplyLingFengSkillPower(
                12, false, ref incomingDamage, ref defence, false, true));
            Assert.Equal(34, defence);

            var add = Segment();
            add.ParseAct(add.ActList, "SETSKILLPOWER 12 + 5 1 6 2 7 3 10 0");
            Assert.True(add.Check(restoredPlayer));
            int adjusted = 100;
            defence = 20;
            Assert.True(restoredPlayer.TryApplyLingFengSkillPower(
                12, false, ref adjusted, ref defence, true, true));
            Assert.Equal(138, adjusted);
            Assert.Equal(38, defence);

            typeof(Envir).GetProperty(nameof(Envir.Time))!
                .SetValue(Envir.Main, oldTime + 11 * Settings.Second);
            adjusted = 100;
            defence = 20;
            Assert.True(restoredPlayer.TryApplyLingFengSkillPower(
                12, false, ref adjusted, ref defence, true, true));
            Assert.Equal(132, adjusted);
            Assert.Equal(34, defence);
        }
        finally
        {
            typeof(Envir).GetProperty(nameof(Envir.Time))!.SetValue(Envir.Main, oldTime);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 翎风While在同一动作段循环且死循环受硬预算终止()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格循环人物" },
                NPCObjectID = 981616
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "MOV N$循环次数 0");
            segment.ParseAct(segment.ActList, "WHILE N$循环次数 < 3");
            segment.ParseAct(segment.ActList, "INC N$循环次数 1");
            segment.ParseAct(segment.ActList, "ENDWHILE");
            segment.ParseAct(segment.ActList, "MOV N$循环完成 1");

            Assert.True(segment.Check(player));
            var context = ScriptVariableContext.ForConversation(
                player, player.NPCObjectID, player.CurrentMap);
            Assert.Equal("3", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "N$循环次数").Text);
            Assert.Equal("1", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "N$循环完成").Text);

            var endless = Segment();
            endless.ParseAct(endless.ActList, "MOV N$死循环 0");
            endless.ParseAct(endless.ActList, "WHILE N$死循环 = 0");
            endless.ParseAct(endless.ActList, "ENDWHILE");
            endless.ParseAct(endless.ActList, "MOV N$预算后动作 1");
            Assert.True(endless.Check(player));
            Assert.Equal("0", Envir.Main.CSharpScripts.VariableCommands
                .Format(context, "N$预算后动作").Text);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 金币调整越界失败且Pk减少不会低于零()
    {
        Assert.True(LingFengNumericCommandExecutor.TryAdjust(
            100, "+", "50", 0, 2_100_000_000, false, out long gold, out _));
        Assert.Equal(150, gold);

        Assert.False(LingFengNumericCommandExecutor.TryAdjust(
            2_100_000_000, "+", "1", 0, 2_100_000_000, false, out gold, out _));
        Assert.Equal(2_100_000_000, gold);
        Assert.False(LingFengNumericCommandExecutor.TryAdjust(
            20, "-", "21", 0, 2_100_000_000, false, out gold, out _));
        Assert.Equal(20, gold);

        Assert.True(LingFengNumericCommandExecutor.TryAdjust(
            20, "-", "21", 0, int.MaxValue, true, out long pk, out _));
        Assert.Equal(0, pk);
    }

    [Fact]
    public void 翎风物品参数只在显式兼容版本下路由且保留扩展语义()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var segment = Segment();
            segment.ParseCheck("CHECKITEM 麻痹 1 1 1");
            segment.ParseAct(segment.ActList, "GIVE 回城卷 2");
            segment.ParseAct(segment.ActList, "TAKE 麻痹 1 0 1 1 -1");

            Assert.Equal(CheckType.CheckItemLingFeng, Assert.Single(segment.CheckList).Type);
            Assert.Equal(new[] { "麻痹", "1", "1", "1" }, segment.CheckList[0].Params);
            Assert.Equal(ActionType.GiveItem, segment.ActList[0].Type);
            Assert.Equal(ActionType.TakeItemLingFeng, segment.ActList[1].Type);
            Assert.Equal(new[] { "麻痹", "1", "0", "1", "1", "-1" }, segment.ActList[1].Params);

            var shortTake = Segment();
            shortTake.ParseAct(shortTake.ActList, "TAKE 命格融合材料 1 1");
            Assert.Equal(new[] { "命格融合材料", "1", "1", "0", "1", "0" },
                Assert.Single(shortTake.ActList).Params);
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格材料人物",
                    Class = MirClass.战士,
                    Level = 40,
                    HP = 1,
                    MP = 1
                },
                Stats = new Stats()
            };
            player.Info.Mount = new MountInfo(player);
            player.Info.Inventory[0] = new UserItem(new ItemInfo
                { Name = "命格融合材料", StackSize = 10 });
            Assert.True(shortTake.Check(player));
            Assert.Null(player.Info.Inventory[0]);

            Assert.Throws<InvalidDataException>(() =>
                segment.ParseAct(segment.ActList, "GIVE 屠龙 1 0 0 0 0 0 0 -1"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 物品名称与持久过滤具有稳定边界()
    {
        Assert.True(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹", true));
        Assert.False(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹", false));
        Assert.True(LingFengItemCommandExecutor.NameMatches("麻痹戒指", "麻痹戒指", false));
        Assert.True(LingFengItemCommandExecutor.DurabilityMatches(1000, 1000, -1));
        Assert.False(LingFengItemCommandExecutor.DurabilityMatches(999, 1000, -1));
        Assert.True(LingFengItemCommandExecutor.DurabilityMatches(999, 1000, -2));
        Assert.False(LingFengItemCommandExecutor.DurabilityMatches(1, 1, 2));
    }

    [Fact]
    public void CheckItem改名兼容标志在当前无改名模型时仍按数据库原名计数()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格异兽人物",
                    Inventory = new UserItem[2]
                }
            };
            player.Info.Inventory[0] = new UserItem(new ItemInfo { Name = "雪龙马" }) { Count = 1 };
            var segment = Segment();
            segment.ParseCheck("CHECKITEM 雪龙马 1 0 1");

            Assert.True(segment.Check(player));

            var missing = Segment();
            missing.ParseCheck("CHECKITEM 七彩鸟 1 0 1");
            Assert.False(missing.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void ReclaimItem无托管会话时幂等保留背包且非法参数在发布前拒绝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var item = new UserItem(new ItemInfo { Name = "命格托管探针" }) { Count = 1 };
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo
                {
                    Name = "命格托管人物",
                    Inventory = new UserItem[] { item }
                }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "RECLAIMITEM");

            Assert.Equal(ActionType.LingFengReclaimItem, Assert.Single(segment.ActList).Type);
            Assert.True(segment.Check(player));
            Assert.Same(item, Assert.Single(player.Info.Inventory));

            var invalid = new TextFileDefinition("NPCs/非法退回参数")
                .AddLines(new[] { "[@MAIN]", "#ACT", "RECLAIMITEM 1" });
            Assert.Contains(TxtScriptSnapshotValidator.Validate(new SingleProvider(invalid)), error =>
                error.Contains("TXT-SNAPSHOT-015", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void SetArrBuff保留官方参数并在缺少客户端契约时不修改服务端Buff()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格按钮人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "SETARRBUFF 1 124 39 381 30 1 381 1 攻击波动");

            NPCActions action = Assert.Single(segment.ActList);
            Assert.Equal(ActionType.LingFengSetArrBuff, action.Type);
            Assert.Equal(
                new[] { "1", "124", "39", "381", "30", "1", "381", "1", "攻击波动" },
                action.Params);
            Assert.True(segment.Check(player));
            Assert.Empty(player.Buffs);

            var targetSegment = Segment();
            targetSegment.ParseAct(targetSegment.ActList,
                "<$CURRRTARGETNAME>.SETARRBUFF 1 113 39 445 10 1 445 1 禁止状态");
            NPCActions targetAction = Assert.Single(targetSegment.ActList);
            Assert.Equal(ActionType.LingFengSetArrBuff, targetAction.Type);
            Assert.Equal("TARGET", targetAction.Params[0]);
            Assert.True(targetSegment.Check(player));
            Assert.Empty(player.Buffs);

            var button = Segment();
            button.ParseAct(button.ActList,
                "ADDBUTTON 3 1 283 284 285 10 200 1 -1");
            NPCActions buttonAction = Assert.Single(button.ActList);
            Assert.Equal(ActionType.LingFengAddButton, buttonAction.Type);
            Assert.Equal(new[] { "3", "1", "283", "284", "285", "10", "200", "1", "-1" },
                buttonAction.Params);
            Assert.True(button.Check(player));
            Assert.Empty(player.Buffs);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 首饰盒状态保留参数且自定义框物品操作在无托管会话时阻止后续奖励()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "翎风容器人物" },
                Account = new AccountInfo()
            };

            var casket = Segment();
            casket.ParseAct(casket.ActList, "ACTIVATIONCASKET");
            casket.ParseAct(casket.ActList, "SETSNDACASKET 1");
            Assert.True(casket.Check(player));
            Assert.Empty(player.Packets);
            Assert.Empty(player.Buffs);

            var boxContext = Segment();
            boxContext.ParseAct(boxContext.ActList, "SETUPGRADEITEM BoxItem1");
            boxContext.ParseAct(boxContext.ActList,
                "OPENITEMBOXEX 91 1 放入一枚需要分解的物品");
            Assert.Collection(boxContext.ActList,
                action =>
                {
                    Assert.Equal(ActionType.LingFengSetUpgradeItemContext, action.Type);
                    Assert.Equal("BoxItem1", Assert.Single(action.Params));
                },
                action =>
                {
                    Assert.Equal(ActionType.LingFengOpenItemBoxEx, action.Type);
                    Assert.Equal(new[] { "91", "1", "放入一枚需要分解的物品" }, action.Params);
                });
            Assert.True(boxContext.Check(player));
            Assert.Empty(player.Packets);

            var itemInfo = new ItemInfo { Name = "原始装备名", Type = ItemType.武器 };
            player.Info.Equipment[(int)EquipmentSlot.武器] = new UserItem(itemInfo);
            var visualIdentity = Segment();
            visualIdentity.ParseAct(visualIdentity.ActList,
                "CHANGEITEMNAME 1 新的 实例装备名");
            visualIdentity.ParseAct(visualIdentity.ActList, "SETBODYCOLOR 151 120 1");
            Assert.Collection(visualIdentity.ActList,
                action =>
                {
                    Assert.Equal(ActionType.LingFengChangeItemName, action.Type);
                    Assert.Equal(new[] { "1", "新的 实例装备名" }, action.Params);
                },
                action =>
                {
                    Assert.Equal(ActionType.LingFengSetBodyColor, action.Type);
                    Assert.Equal(new[] { "151", "120", "1" }, action.Params);
                });
            Assert.True(visualIdentity.Check(player));
            Assert.Equal("原始装备名", itemInfo.Name);
            Assert.Empty(player.Packets);

            var reject = Segment();
            reject.ParseAct(reject.ActList, "UNALLOWITEMINTOBOX");
            reject.ParseAct(reject.ActList, "GIVEGOLD 7");
            Assert.True(reject.Check(player));
            Assert.Equal(0u, player.Account.Gold);

            var returnItem = Segment();
            returnItem.ParseAct(returnItem.ActList, "RETURNBOXITEM 0");
            returnItem.ParseAct(returnItem.ActList, "GIVEGOLD 9");
            Assert.True(returnItem.Check(player));
            Assert.Equal(0u, player.Account.Gold);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 扩展界面与外部事务命令保留参数且不伪造服务端成功状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new PacketCapturingPlayerObject
            {
                Info = new CharacterInfo { Name = "翎风E1依赖人物" },
                Account = new AccountInfo()
            };
            var segment = Segment();
            string[] commands =
            {
                "EXTBAGPAGECOUNT + 5",
                "EXTBAGOPENITEMCOUNT + 20",
                "SETBIGSTORAGECOUNT + 49",
                "OPENAUTOPICKITEM 1 0 5 1 0 0 1000",
                "CLOSEAUTOPICKITEM",
                "OPENBIGDIALOGBOX 3 216 1 4 0 -65 1 720 10",
                "OPENITEMBOX 稻草人",
                "BREAKADDSELLPLAYER",
                "STOPTAKEON",
                "SETITEMFROM -1 0 2",
                "ADDATTACKSABUKALL 0",
                "AUTOTAKEONITEM 命格装备 2",
                "CHANGEHUMNAME 新名字",
                "CREATEMYSHOP 命格商店",
                "OPENGODBLESS 0",
                "PLAYSOUNDEXT WAV\\8200-6.wav 1 0",
                "SETOFFLINEPLAY ON",
                "SETRANKLEVELNAME 命格榜首",
                "SHOWGODBLESS 1",
                "STARTAUTOPLAYGAME",
                "STOPAUTOPLAYGAME",
                "STOPBUYUSER",
                "STOPTAKEOFF",
                "SUPERMOVEMSG 0 9 0 16 200 1 命格公告",
                "SENDMOVEHINTMSG 成功转换 250 0",
                "TAKEPOSW 17"
            };
            foreach (string command in commands)
                segment.ParseAct(segment.ActList, command);

            Assert.Equal(commands.Length, segment.ActList.Count);
            Assert.All(segment.ActList, action =>
                Assert.Equal(ActionType.LingFengDeferredCompatibilityCommand, action.Type));
            Assert.Equal("EXTBAGPAGECOUNT", segment.ActList[0].Params[0]);
            Assert.Equal(new[] { "OPENAUTOPICKITEM", "1", "0", "5", "1", "0", "0", "1000" },
                segment.ActList[3].Params);
            Assert.True(segment.Check(player));
            Assert.Empty(player.Packets);
            Assert.Equal(0u, player.Account.Gold);

            var deferredChecks = Segment();
            deferredChecks.ParseCheck("CHECKMYSHOP");
            deferredChecks.ParseCheck("CHECKSHOPNAME 命格商店");
            deferredChecks.ParseCheck("CHECKBOXITEMCOUNT 18");
            Assert.Equal(3, deferredChecks.CheckList.Count);
            Assert.All(deferredChecks.CheckList, check =>
                Assert.Equal(CheckType.LingFengDeferredCompatibilityCheck, check.Type));
            Assert.False(deferredChecks.Check(player));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void CloseArrBuff保留动态按钮序号并在缺少客户端契约时不清除服务端Buff()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格药剂人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CLOSEARRBUFF <$Str(S$Buff编号)>");

            NPCActions action = Assert.Single(segment.ActList);
            Assert.Equal(ActionType.LingFengCloseArrBuff, action.Type);
            Assert.Equal(new[] { "<$Str(S$Buff编号)>" }, action.Params);
            Assert.True(segment.Check(player));
            Assert.Empty(player.Buffs);

            Assert.Throws<InvalidDataException>(() =>
                segment.ParseAct(new List<NPCActions>(), "CLOSEARRBUFF"));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void ScatterMonItems按怪物真实掉落表在指定地图坐标生成归属物品()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        var map = WalkableMap(981617, "LF-SCATTER-DROP", 5, 5);
        var itemInfo = new ItemInfo { Index = 981617, Name = "命格散落探针" };
        var monsterInfo = new MonsterInfo
        {
            Index = 981617,
            Name = "命格散落怪物",
            Drops = new List<DropInfo>
            {
                new() { Chance = 1, Item = itemInfo }
            }
        };
        var player = new SilentPlayerObject
        {
            Info = new CharacterInfo { Name = "命格散落人物" },
            CurrentMap = map,
            CurrentLocation = new Point(0, 0),
            Stats = new Stats()
        };
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.MapList.Add(map);
            Envir.Main.ItemInfoList.Add(itemInfo);
            Envir.Main.MonsterInfoList.Add(monsterInfo);
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "SCATTERMONITEMS 命格散落怪物 LF-SCATTER-DROP 2 3");

            Assert.True(segment.Check(player));

            ItemObject dropped = Enumerable.Range(0, map.Width)
                .SelectMany(x => Enumerable.Range(0, map.Height)
                    .SelectMany(y => map.GetCell(x, y).Objects ?? new List<MapObject>()))
                .OfType<ItemObject>()
                .Single(item => item.Item.Info == itemInfo);
            Assert.Same(player, dropped.Owner);
            Assert.True(Functions.InRange(new Point(2, 3), dropped.CurrentLocation, Settings.DropRange));
            dropped.Despawn();
        }
        finally
        {
            Envir.Main.MonsterInfoList.Remove(monsterInfo);
            Envir.Main.ItemInfoList.Remove(itemInfo);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void AddArrButton与DelArrButton保留法宝自动排列按钮参数且不伪造服务端状态()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "封神按钮人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                @"ADDARRBUTTON 2 92 39 965 965 965 0 \{五帝|251} 五帝之力\-永久提升30%最终伤害");
            segment.ParseAct(segment.ActList, "DELARRBUTTON 92");

            Assert.Equal(ActionType.LingFengAddArrButton, segment.ActList[0].Type);
            Assert.Equal(new[] { "2", "92", "39", "965", "965", "965", "0",
                @"\{五帝|251}", @"五帝之力\-永久提升30%最终伤害" },
                segment.ActList[0].Params);
            Assert.Equal(ActionType.LingFengDeleteArrButton, segment.ActList[1].Type);
            Assert.Equal(new[] { "92" }, segment.ActList[1].Params);
            Assert.True(segment.Check(player));
            Assert.Empty(player.Buffs);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void DelBoxItem缺少自定义框托管契约时阻止后续奖励且不删除背包物品()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var item = new UserItem(new ItemInfo { Name = "命格增幅材料" });
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格增幅人物" },
                Account = new AccountInfo { Gold = 100 }
            };
            player.Info.Inventory[0] = item;
            var segment = Segment();
            segment.ParseAct(segment.ActList, "DELBOXITEM 2");
            segment.ParseAct(segment.ActList, "GOLDCOUNT + 1000");

            Assert.True(segment.Check(player));
            Assert.Same(item, player.Info.Inventory[0]);
            Assert.Equal(100U, player.Account.Gold);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void OpenStorageView普通模式发送真实仓库面板而无限模式失败关闭()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            PacketCapturingPlayerObject CreatePlayer(string name)
            {
                var player = new PacketCapturingPlayerObject
                {
                    Info = new CharacterInfo { Name = name },
                    Account = new AccountInfo()
                };
                player.Connection = (Server.MirNetwork.MirConnection)
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                        typeof(Server.MirNetwork.MirConnection));
                return player;
            }

            PacketCapturingPlayerObject ordinary = CreatePlayer("命格普通仓库人物");
            var ordinarySegment = Segment();
            ordinarySegment.ParseAct(ordinarySegment.ActList, "OPENSTORAGEVIEW 0 0 175");
            Assert.True(ordinarySegment.Check(ordinary));
            Assert.Contains(ordinary.Packets, packet => packet is ServerPackets.UserStorage);
            Assert.Contains(ordinary.Packets, packet => packet is ServerPackets.NPCStorage);

            PacketCapturingPlayerObject infinite = CreatePlayer("命格无限仓库人物");
            var infiniteSegment = Segment();
            infiniteSegment.ParseAct(infiniteSegment.ActList, "OPENSTORAGEVIEW 1 0 175");
            infiniteSegment.ParseAct(infiniteSegment.ActList, "GOLDCOUNT + 1000");
            Assert.True(infiniteSegment.Check(infinite));
            Assert.Empty(infinite.Packets);
            Assert.Equal(0U, infinite.Account.Gold);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 命格与封神对象特效分别锚定当前怪物和真实宠物并保留像素偏移()
    {
        bool oldTxtEnabled = Settings.TxtScriptsEnabled;
        string oldPath = Settings.TxtScriptsPath;
        TxtScriptLayout oldLayout = Settings.TxtScriptsLayout;
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        string root = Path.Combine(Path.GetTempPath(), $"lfenv16-playeffect-{Guid.NewGuid():N}");
        var map = WalkableMap(981618, "LF-PLAYEFFECT", 5, 5);
        var player = new PacketCapturingPlayerObject
        {
            Info = new CharacterInfo { Name = "命格特效观察者" },
            CurrentMap = map,
            CurrentLocation = new Point(2, 2),
            Stats = new Stats()
        };
        var target = new FateMonster(new MonsterInfo { Index = 981618, Name = "命格特效目标" })
        {
            CurrentMap = map,
            CurrentLocation = new Point(2, 1),
            Node = new LinkedListNode<MapObject>(null)
        };
        var pet = new FateMonster(new MonsterInfo { Index = 981619, Name = "封神法宝宠物" })
        {
            CurrentMap = map,
            CurrentLocation = new Point(3, 2),
            Master = player,
            Node = new LinkedListNode<MapObject>(null)
        };
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllLines(Path.Combine(root, "EffectImageList.txt"),
                Enumerable.Range(0, 84).Select(index => $"Effect{index}.Pak"));
            Settings.TxtScriptsEnabled = true;
            Settings.TxtScriptsPath = root;
            Settings.TxtScriptsLayout = TxtScriptLayout.LingFeng;
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Envir.Main.MapList.Add(map);
            Envir.Main.Objects.AddLast(target);
            player.Pets.Add(pet);
            map.Players.Add(player);

            var segment = Segment();
            segment.ParseAct(segment.ActList, "M.PLAYEFFECT 83 3570 12 1 30 0 -85 -220 1");
            segment.ParseAct(segment.ActList, "PET.PLAYEFFECT 38 2400 10 1 80 0 * * 1");
            var payload = new LingFengDamageEvent(
                PlayerDamagePerspective.Outgoing, player.Name, target.Name, target.Name,
                1, 1, true) { CurrentTargetObjectId = target.ObjectID };
            using (LingFengTxtTriggerContext.Push(payload))
                Assert.True(segment.Check(player));

            ServerPackets.LingFengMapEffect[] packets = player.Packets
                .OfType<ServerPackets.LingFengMapEffect>().ToArray();
            Assert.Contains(packets, packet => packet.AnchorObjectId == target.ObjectID &&
                packet.LibraryName == "Effect83" && packet.PixelOffset == new Point(-85, -220));
            Assert.Contains(packets, packet => packet.AnchorObjectId == pet.ObjectID &&
                packet.LibraryName == "Effect38" && packet.PixelOffset == Point.Empty);
        }
        finally
        {
            map.Players.Remove(player);
            player.Pets.Remove(pet);
            Envir.Main.Objects.Remove(target);
            Envir.Main.MapList.Remove(map);
            Settings.TxtScriptsEnabled = false;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Settings.TxtScriptsPath = oldPath;
            Settings.TxtScriptsLayout = oldLayout;
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsEnabled = oldTxtEnabled;
            Envir.Main.ApplyPhysicalTextFileDefinitions();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SetIcon保留酷明顶戴参数并在缺少客户端契约时不伪造服务端Buff()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格顶戴人物" }
            };
            var segment = Segment();
            segment.ParseAct(segment.ActList,
                "SETICON 2 39 199 0 -30 10 0 0 250");
            segment.ParseAct(segment.ActList, "SETICON 2 -1");

            Assert.Collection(segment.ActList,
                action =>
                {
                    Assert.Equal(ActionType.LingFengSetIcon, action.Type);
                    Assert.Equal(new[] { "2", "39", "199", "0", "-30", "10", "0", "0", "250" },
                        action.Params);
                },
                action =>
                {
                    Assert.Equal(ActionType.LingFengSetIcon, action.Type);
                    Assert.Equal(new[] { "2", "-1" }, action.Params);
                });
            Assert.True(segment.Check(player));
            Assert.Empty(player.Buffs);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 宝宝属性批处理命令保留暂存与重算边界且缺适配器时不产生半组修改()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var player = new SilentPlayerObject
            {
                Info = new CharacterInfo { Name = "命格宝宝批处理人物" }
            };
            var pet = new FateMonster(new MonsterInfo
            {
                Name = "命格守卫",
                Stats = new Stats { [Stat.MaxDC] = 12 }
            }) { Master = player };
            pet.RefreshAll();
            player.Pets.Add(pet);
            var segment = Segment();
            segment.ParseAct(segment.ActList, "CHANGESLAVEABILITY 9 99 命格守卫");
            segment.ParseAct(segment.ActList, "CHANGESLAVEABILITY 30 10 命格守卫");
            segment.ParseAct(segment.ActList, "RECALCSLAVEABILITY 命格守卫");

            Assert.Equal(ActionType.LingFengChangeSlaveAbility, segment.ActList[0].Type);
            Assert.Equal(ActionType.LingFengRecalcSlaveAbility, segment.ActList[2].Type);
            Assert.True(segment.Check(player));
            Assert.Equal(12, pet.Stats[Stat.MaxDC]);
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 不可映射的扩展物品动作参数在候选快照发布前拒绝且CheckItem标志可发布()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var definition = new TextFileDefinition("NPCs/扩展物品")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "CHECKITEM 雪龙马 1 0 1",
                    "#ACT", "GIVE 屠龙 1 0 0 0 0 0 0 -1",
                    "TAKE 麻痹 1 0 1 0 -1", "TAKE 天书 1"
                });
            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(new SingleProvider(definition));

            Assert.Single(errors, error =>
                error.Contains("TXT-SNAPSHOT-013", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void LoopGoto目标标签缺失时在候选发布前拒绝()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            var definition = new TextFileDefinition("SystemScripts/QManage")
                .AddLines(new[] { "[@MAIN]", "#ACT", "LOOPGOTO @MISSING 2" });

            IReadOnlyList<string> errors =
                TxtScriptSnapshotValidator.Validate(new SingleProvider(definition));

            Assert.Contains(errors, error =>
                error.Contains("TXT-SNAPSHOT-010", StringComparison.Ordinal) &&
                error.Contains("[@MISSING]", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
        }
    }

    [Fact]
    public void 严格翎风版本在发布前拒绝未知检测和动作命令()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            var definition = new TextFileDefinition("NPCs/未知命令")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "NOT_A_CHECK 1", "NOT UNKNOWN_CHECK 1",
                    "#ACT", "NOT_AN_ACTION 2"
                });
            var provider = new SingleProvider(definition);

            Settings.TxtScriptsCompatibilityVersion = string.Empty;
            Settings.TxtScriptsStrictCompatibility = true;
            Assert.DoesNotContain(TxtScriptSnapshotValidator.Validate(provider),
                error => error.Contains("TXT-SNAPSHOT-014", StringComparison.Ordinal));

            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            IReadOnlyList<string> errors = TxtScriptSnapshotValidator.Validate(provider);
            Assert.Equal(3, errors.Count(error => error.Contains("TXT-SNAPSHOT-014", StringComparison.Ordinal)));
            Assert.Contains(errors, error => error.Contains("NOT_A_CHECK", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("UNKNOWN_CHECK", StringComparison.Ordinal));
            Assert.Contains(errors, error => error.Contains("NOT_AN_ACTION", StringComparison.Ordinal));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    [Fact]
    public void 高频人物物品金币闭环快照严格预检无未知命令()
    {
        string oldVersion = Settings.TxtScriptsCompatibilityVersion;
        bool oldStrict = Settings.TxtScriptsStrictCompatibility;
        try
        {
            Settings.TxtScriptsCompatibilityVersion = "LFM2-2026-08-15-snapshot";
            Settings.TxtScriptsStrictCompatibility = true;
            var definition = new TextFileDefinition("NPCs/人物物品金币闭环")
                .AddLines(new[]
                {
                    "[@MAIN]", "#IF", "CHECKLEVEL >= 40", "CHECKGOLD >= 1000",
                    "CHECKITEM 回城卷 1 0 0", "#ACT", "TAKEGOLD 1000",
                    "GIVEITEM 奖励卷 1", "GOLDCOUNT + 50", "CHANGEPKPOINT - 10",
                    "TAKE 回城卷 1 0 0 1 0", "#SAY", "兑换完成"
                });

            Assert.Empty(TxtScriptSnapshotValidator.Validate(new SingleProvider(definition)));
        }
        finally
        {
            Settings.TxtScriptsCompatibilityVersion = oldVersion;
            Settings.TxtScriptsStrictCompatibility = oldStrict;
        }
    }

    private static Map WalkableMap(int index, string fileName, int width, int height)
    {
        var map = new Map(new MapInfo { Index = index, FileName = fileName })
        {
            Width = width,
            Height = height,
            Cells = new Cell[width, height]
        };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        map.GetWalkableCells();
        return map;
    }

    private static NPCSegment Segment(string sourceKey = null) => new(
        new NPCPage("[@MAIN]"), new List<string>(), new List<string>(),
        new List<string>(), new List<string>(), new List<string>(), sourceKey);

    private sealed class CapturingPlayerObject : PlayerObject
    {
        public List<(string Text, ChatType Type)> Messages { get; } = new();

        public override void ReceiveChat(string text, ChatType type) => Messages.Add((text, type));
    }

    private sealed class FateMonster(MonsterInfo info) : MonsterObject(info)
    {
        public void DropForTest() => Drop();

        public int ApplyLingFengRedArmourForTest(int armour) =>
            ApplyLingFengRedPoisonArmour(armour);

        public void RunNextLingFengResourceForTest()
        {
            DelayedAction action = ActionList.First(entry =>
                entry.Type == DelayedType.LingFengResource);
            Process(action);
            ActionList.Remove(action);
        }
    }

    private sealed class SilentPlayerObject : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }

        public void RunNextNpcForTest()
        {
            DelayedAction action = ActionList.First(entry =>
                entry.Type == DelayedType.NPC && entry.Time == -1);
            Process(action);
            ActionList.Remove(action);
        }
    }

    private sealed class PacketCapturingPlayerObject : PlayerObject
    {
        public List<Packet> Packets { get; } = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void Broadcast(Packet packet) => Packets.Add(packet);
    }

    private sealed class SingleProvider : ITextFileProvider
    {
        private readonly TextFileDefinition _definition;

        public SingleProvider(TextFileDefinition definition) => _definition = definition;

        public IReadOnlyCollection<TextFileDefinition> GetAll() => new[] { _definition };

        public TextFileDefinition GetByKey(string key) =>
            LogicKey.NormalizeOrThrow(key) == _definition.Key ? _definition : null;
    }
}
