extern alias ShareProtocol;

using System.Text.Json;
using Xunit;
using ShareFishingCast = ShareProtocol::ClientPackets.FishingCast;
using ShareFishingChangeAutocast = ShareProtocol::ClientPackets.FishingChangeAutocast;
using ShareLogin = ShareProtocol::ClientPackets.Login;
using SharePacket = ShareProtocol::Packet;
using ShareLevelEffects = ShareProtocol::LevelEffects;
using ShareSpell = ShareProtocol::Spell;
using ShareMonster = ShareProtocol::Monster;
using ShareBuffType = ShareProtocol::BuffType;
using ShareSpellEffect = ShareProtocol::SpellEffect;
using ShareServerPackets = ShareProtocol::ServerPackets;
using ShareFishingUpdate = ShareProtocol::ServerPackets.FishingUpdate;

namespace Base05.Tests;

[Collection("ProtocolWireCodec")]
public sealed class ProtocolGoldenTests
{
    [Fact]
    public void Manifest_is_machine_readable_and_covers_packet_and_enum_metadata()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "protocol-wire-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var packets = root.GetProperty("packets").EnumerateArray().ToArray();

        Assert.Equal("PROTO-01.wire-manifest.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(420, packets.Length);
        Assert.Equal(145, root.GetProperty("coverage").GetProperty("clientPacketCount").GetInt32());
        Assert.Equal(275, root.GetProperty("coverage").GetProperty("serverPacketCount").GetInt32());

        Assert.Equal(Enumerable.Range(0, 145), packets
            .Where(packet => packet.GetProperty("direction").GetString() == "clientToServer")
            .Select(packet => packet.GetProperty("id").GetInt32()).OrderBy(id => id));
        Assert.Equal(Enumerable.Range(0, 275), packets
            .Where(packet => packet.GetProperty("direction").GetString() == "serverToClient")
            .Select(packet => packet.GetProperty("id").GetInt32()).OrderBy(id => id));
        Assert.Equal(145, packets.Count(packet => packet.GetProperty("direction").GetString() == "clientToServer"));
        Assert.Equal(275, packets.Count(packet => packet.GetProperty("direction").GetString() == "serverToClient"));
        Assert.Equal("ClientPackets.Login", FindPacket(root, "clientToServer", "Login").GetProperty("type").GetString());
        Assert.Equal("ServerPackets.FishingUpdate", FindPacket(root, "serverToClient", "FishingUpdate").GetProperty("type").GetString());

        var ranges = root.GetProperty("versionRanges");
        Assert.Equal(0, ranges.GetProperty("v1").GetProperty("clientToServer").GetProperty("minId").GetInt32());
        Assert.Equal(144, ranges.GetProperty("v1").GetProperty("clientToServer").GetProperty("maxId").GetInt32());
        Assert.Equal(0, ranges.GetProperty("v1").GetProperty("serverToClient").GetProperty("minId").GetInt32());
        Assert.Equal(274, ranges.GetProperty("v1").GetProperty("serverToClient").GetProperty("maxId").GetInt32());
        Assert.Equal("absent", ranges.GetProperty("v2").GetProperty("status").GetString());

        var levelEffects = root.GetProperty("enums").GetProperty("LevelEffects");
        Assert.Equal("ushort", levelEffects.GetProperty("underlyingType").GetString());
        Assert.Equal(256, levelEffects.GetProperty("values").GetProperty("Phoenix").GetInt32());
        Assert.Equal(17, root.GetProperty("enums").GetProperty("ChatType").GetProperty("values").GetProperty("LineMessage").GetInt32() + 1);
        Assert.Equal("byte", root.GetProperty("enums").GetProperty("Stat").GetProperty("underlyingType").GetString());

        var pointFields = packets.SelectMany(packet => packet.GetProperty("fields").EnumerateArray())
            .Where(field => field.TryGetProperty("compositeType", out var composite) && composite.GetString() == "Point")
            .ToArray();
        Assert.Equal(44, pointFields.Length);
        Assert.All(pointFields, field =>
        {
            Assert.False(field.TryGetProperty("arrayEncoding", out _));
            Assert.Equal(2, field.GetProperty("components").GetArrayLength());
            Assert.Equal("Int32", field.GetProperty("components")[0].GetProperty("wireType").GetString());
            Assert.Equal("Int32", field.GetProperty("components")[1].GetProperty("wireType").GetString());
        });

        Assert.Equal(new[] { "Int32", "ByteArray" }, Field(FindPacket(root, "clientToServer", "ClientVersion"), "VersionHash")
            .GetProperty("wireTypes").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "Int32", "ByteArray" }, Field(FindPacket(root, "clientToServer", "ReportIssue"), "Image")
            .GetProperty("wireTypes").EnumerateArray().Select(item => item.GetString()).ToArray());

        foreach (var field in packets.SelectMany(packet => packet.GetProperty("fields").EnumerateArray()))
        {
            if (field.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean())
            {
                Assert.True(field.TryGetProperty("presenceFor", out var presenceFor));
                Assert.False(string.IsNullOrWhiteSpace(presenceFor.GetString()));
                Assert.Equal("Boolean", field.GetProperty("presenceFlagType").GetString());
            }

            if (field.TryGetProperty("arrayEncoding", out var encoding) && encoding.GetProperty("kind").GetString() == "countPrefixed")
            {
                Assert.True(encoding.TryGetProperty("countField", out var countField) || encoding.TryGetProperty("countExpression", out _));
                Assert.False(string.IsNullOrWhiteSpace(encoding.GetProperty("elementType").GetString()));
            }
        }

        foreach (var packet in packets)
        {
            var fields = packet.GetProperty("fields").EnumerateArray().ToArray();
            var fieldKeys = fields.Select(field => field.GetProperty("fieldKey").GetString()!)
                .ToArray();
            Assert.Equal(fieldKeys.Length, fieldKeys.Distinct(StringComparer.Ordinal).Count());

            var countKeys = fields
                .Where(field => field.TryGetProperty("role", out var role) && role.GetString() == "count")
                .Select(field => field.GetProperty("fieldKey").GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            if (packet.TryGetProperty("countFields", out var declaredCounts))
            {
                var declaredKeys = declaredCounts.EnumerateArray()
                    .Select(count => count.GetProperty("fieldKey").GetString()!)
                    .ToArray();
                Assert.Equal(declaredKeys.Length, declaredKeys.Distinct(StringComparer.Ordinal).Count());
                countKeys.UnionWith(declaredKeys);
            }

            foreach (var field in fields)
            {
                var expression = field.GetProperty("expression").GetString() ?? string.Empty;
                Assert.DoesNotContain("new List<", expression, StringComparison.Ordinal);
                var allocationIndex = expression.IndexOf("new ", StringComparison.Ordinal);
                if (allocationIndex >= 0 &&
                    expression.IndexOf("[", allocationIndex, StringComparison.Ordinal) >= 0 &&
                    !expression.Contains("Read", StringComparison.Ordinal))
                {
                    Assert.Fail($"发现未读取 wire 的数组分配伪字段: {packet.GetProperty("name").GetString()}.{field.GetProperty("name").GetString()}");
                }

                if (field.TryGetProperty("arrayEncoding", out var encoding) &&
                    encoding.GetProperty("kind").GetString() == "countPrefixed")
                {
                    var countField = encoding.GetProperty("countField").GetString();
                    Assert.False(string.IsNullOrWhiteSpace(countField));
                    Assert.Contains(countField, countKeys);
                }
            }
        }

        foreach (var packetName in new[] { "SendMail", "MailCost" })
        {
            var packet = FindPacket(root, "clientToServer", packetName);
            var items = Field(packet, "ItemsIdx");
            Assert.Equal("fixed", items.GetProperty("arrayEncoding").GetProperty("kind").GetString());
            Assert.Equal(5, items.GetProperty("arrayEncoding").GetProperty("fixed").GetInt32());
            Assert.Equal("UInt64", items.GetProperty("arrayEncoding").GetProperty("elementType").GetString());
        }

        Assert.True(FindPacket(root, "serverToClient", "NPCGoods").GetProperty("compressed").GetBoolean());
        Assert.Equal("baseExpansion", FindPacket(root, "serverToClient", "ObjectHero").GetProperty("wireLayout").GetProperty("kind").GetString());
        Assert.Equal("layoutOverride", FindPacket(root, "serverToClient", "HeroInformation").GetProperty("wireLayout").GetProperty("kind").GetString());

        var awakeningMaterials = FindPacket(root, "serverToClient", "AwakeningNeedMaterials");
        var materialsCount = Field(awakeningMaterials, "MaterialsCount");
        Assert.Equal("pairedElement", materialsCount.GetProperty("arrayEncoding").GetProperty("kind").GetString());
        Assert.Equal("Materials", materialsCount.GetProperty("arrayEncoding").GetProperty("pairedWith").GetString());
        Assert.Equal("Materials[i]", materialsCount.GetProperty("arrayEncoding").GetProperty("presenceField").GetString());
        Assert.Equal("countPrefixed", Field(awakeningMaterials, "Materials").GetProperty("arrayEncoding").GetProperty("kind").GetString());

        var guildNotice = FindPacket(root, "serverToClient", "GuildNoticeChange");
        var updateField = Field(guildNotice, "update");
        Assert.Equal("any value < 0", updateField.GetProperty("countSemantics").GetProperty("sentinel").GetString());
        Assert.Equal("write Int32 update and stop", guildNotice.GetProperty("wireControlFlow").GetProperty("negativeUpdate").GetString());
        Assert.Equal("write notice.Count followed by that many String7BitUtf8 values",
            guildNotice.GetProperty("wireControlFlow").GetProperty("nonNegativeUpdate").GetString());

        var mirClass = root.GetProperty("enums").GetProperty("MirClass").GetProperty("values");
        Assert.Equal(10, mirClass.EnumerateObject().Count());
        foreach (var expected in new Dictionary<string, int>
        {
            ["战士"] = 0, ["Warrior"] = 0, ["法师"] = 1, ["Wizard"] = 1,
            ["道士"] = 2, ["Taoist"] = 2, ["刺客"] = 3, ["Assassin"] = 3,
            ["弓箭"] = 4, ["Archer"] = 4,
        })
        {
            Assert.Equal(expected.Value, mirClass.GetProperty(expected.Key).GetInt32());
        }

        var forks = root.GetProperty("knownForks").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Spell", "Monster", "BuffType", "SpellEffect", "LevelEffects" }, forks.Select(item => item.GetProperty("name").GetString()));
        var spellFork = forks.Single(item => item.GetProperty("name").GetString() == "Spell");
        Assert.Equal(51, spellFork.GetProperty("criticalValues").GetProperty("FireBall").GetProperty("shared").GetInt32());
        Assert.Equal(31, spellFork.GetProperty("criticalValues").GetProperty("FireBall").GetProperty("share").GetInt32());
        var monsterFork = forks.Single(item => item.GetProperty("name").GetString() == "Monster");
        Assert.Equal(329, monsterFork.GetProperty("criticalValues").GetProperty("AncientBringer").GetProperty("shared").GetInt32());
        Assert.Equal(272, monsterFork.GetProperty("criticalValues").GetProperty("AncientBringer").GetProperty("share").GetInt32());
        var fork = forks.Single(item => item.GetProperty("name").GetString() == "LevelEffects");
        Assert.False(fork.GetProperty("wireCompatible").GetBoolean());
        Assert.Equal(0, fork.GetProperty("truncationRisk").GetProperty("shareValueAfterByteCast").GetInt32());
        Assert.Contains(fork.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "ObjectLevelEffects");
        Assert.Contains(fork.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "ObjectPlayer");
        Assert.Contains(fork.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "UserInformation");
        foreach (var forkEntry in forks)
        {
            foreach (var side in new[] { "Shared", "Share" })
            {
                var coverage = forkEntry.GetProperty("operationCoverage").GetProperty(side);
                Assert.True(coverage.GetProperty("read").GetInt32() > 0);
                Assert.True(coverage.GetProperty("write").GetInt32() > 0);
            }
        }
        var runtimeUses = fork.GetProperty("runtimeUses").EnumerateArray().ToArray();
        Assert.Equal(46, runtimeUses.Length);
        var runtimePolicy = fork.GetProperty("runtimeUsesPolicy");
        Assert.Equal("all LevelEffects identifier occurrences in non-protocol C# source", runtimePolicy.GetProperty("scope").GetString());
        Assert.Equal(new[] { "field", "state", "call", "method", "packetRoute" },
            runtimePolicy.GetProperty("includeKinds").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "Shared/", "Client_MonoGame.Shared/Share/" },
            runtimePolicy.GetProperty("excludedRoots").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal("Client_VorticeDX11/", runtimePolicy.GetProperty("platforms").GetProperty("pc").GetString());
        Assert.Equal("Client_MonoGame.Shared/ (excluding Share/)", runtimePolicy.GetProperty("platforms").GetProperty("mobile").GetString());
        Assert.Equal("Server/", runtimePolicy.GetProperty("platforms").GetProperty("server").GetString());
        Assert.Equal(17, runtimeUses.Count(item => item.GetProperty("side").GetString() == "pc"));
        Assert.Equal(11, runtimeUses.Count(item => item.GetProperty("side").GetString() == "mobile"));
        Assert.Equal(18, runtimeUses.Count(item => item.GetProperty("side").GetString() == "server"));
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Client_VorticeDX11/MirScenes/GameScene.cs:1938");
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Client_MonoGame.Shared/MirScenes/GameScene.cs:3332");
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Client_MonoGame.Shared/MirScenes/GameScene.cs:7110");
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Server/MirObjects/PlayerObject.cs:1283");
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Server/MirObjects/HumanObject.cs:1684");
        Assert.Contains(runtimeUses, item => item.GetProperty("source").GetString() == "Server/MirObjects/NPC/NPCSegment.cs:3822");
    }

    [Fact]
    public void Enum_forks_match_manifest_and_source_declarations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "protocol-wire-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var forks = root.GetProperty("knownForks").EnumerateArray().ToArray();

        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(global::Spell)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ShareSpell)));
        Assert.Equal(51, (int)global::Spell.FireBall);
        Assert.Equal(31, (int)ShareSpell.FireBall);
        var spell = forks.Single(item => item.GetProperty("name").GetString() == "Spell");
        Assert.Equal(51, spell.GetProperty("criticalValues").GetProperty("FireBall").GetProperty("shared").GetInt32());
        Assert.Equal(31, spell.GetProperty("criticalValues").GetProperty("FireBall").GetProperty("share").GetInt32());
        Assert.Contains(spell.GetProperty("differences").EnumerateArray(), item => item.GetProperty("member").GetString() == "FireBall");
        Assert.Contains(spell.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "Data.ClientMagic");
        Assert.True(spell.GetProperty("operationCoverage").GetProperty("Shared").GetProperty("read").GetInt32() >= 16);
        Assert.True(spell.GetProperty("operationCoverage").GetProperty("Shared").GetProperty("write").GetInt32() >= 16);
        Assert.True(spell.GetProperty("operationCoverage").GetProperty("Share").GetProperty("read").GetInt32() >= 16);
        Assert.True(spell.GetProperty("operationCoverage").GetProperty("Share").GetProperty("write").GetInt32() >= 16);
        var spellPackets = spell.GetProperty("wireUses").EnumerateArray()
            .Select(item => item.GetProperty("packet").GetString()).ToArray();
        Assert.DoesNotContain("ObjectPlayer", spellPackets);
        Assert.DoesNotContain("ObjectEffect", spellPackets);
        Assert.DoesNotContain("MapEffect", spellPackets);

        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(global::Monster)));
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(ShareMonster)));
        Assert.Equal(329, (int)global::Monster.AncientBringer);
        Assert.Equal(272, (int)ShareMonster.AncientBringer);
        var monster = forks.Single(item => item.GetProperty("name").GetString() == "Monster");
        Assert.Equal(329, monster.GetProperty("criticalValues").GetProperty("AncientBringer").GetProperty("shared").GetInt32());
        Assert.Equal(272, monster.GetProperty("criticalValues").GetProperty("AncientBringer").GetProperty("share").GetInt32());
        Assert.Contains(monster.GetProperty("differences").EnumerateArray(), item => item.GetProperty("member").GetString() == "AncientBringer");

        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(global::BuffType)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ShareBuffType)));
        Assert.Equal(25, (int)global::BuffType.MagicShield);
        Assert.Equal(24, (int)ShareBuffType.MagicShield);
        var buffType = forks.Single(item => item.GetProperty("name").GetString() == "BuffType");
        Assert.Contains(buffType.GetProperty("differences").EnumerateArray(), item => item.GetProperty("member").GetString() == "MagicShield");
        Assert.Contains(buffType.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "Data.ClientBuff");
        foreach (var side in new[] { "Shared", "Share" })
        {
            var packetPath = side == "Shared" ? "Shared/ServerPackets.cs:" : "Client_MonoGame.Shared/Share/ServerPackets.cs:";
            var removeBuff = buffType.GetProperty("wireUses").EnumerateArray()
                .Single(item => item.GetProperty("side").GetString() == side && item.GetProperty("packet").GetString() == "RemoveBuff");
            Assert.Contains(removeBuff.GetProperty("occurrences").EnumerateArray(), occurrence =>
                occurrence.GetProperty("operation").GetString() == "write" &&
                occurrence.GetProperty("source").GetString() == packetPath + "3898" &&
                occurrence.GetProperty("expression").GetString() == "writer.Write((byte)Type);");
            var pauseBuff = buffType.GetProperty("wireUses").EnumerateArray()
                .Single(item => item.GetProperty("side").GetString() == side && item.GetProperty("packet").GetString() == "PauseBuff");
            Assert.Contains(pauseBuff.GetProperty("occurrences").EnumerateArray(), occurrence =>
                occurrence.GetProperty("operation").GetString() == "write" &&
                occurrence.GetProperty("source").GetString() == packetPath + "3920" &&
                occurrence.GetProperty("expression").GetString() == "writer.Write((byte)Type);");
        }

        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(global::SpellEffect)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ShareSpellEffect)));
        Assert.Equal(5, (int)global::SpellEffect.RedMoonEvil);
        Assert.Equal(4, (int)ShareSpellEffect.RedMoonEvil);
        var spellEffect = forks.Single(item => item.GetProperty("name").GetString() == "SpellEffect");
        Assert.Contains(spellEffect.GetProperty("differences").EnumerateArray(), item => item.GetProperty("member").GetString() == "RedMoonEvil");
        Assert.Contains(spellEffect.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "ObjectPlayer");
        Assert.Contains(spellEffect.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "ObjectEffect");
        Assert.Contains(spellEffect.GetProperty("wireUses").EnumerateArray(), item => item.GetProperty("packet").GetString() == "MapEffect");

        var levelEffects = forks.Single(item => item.GetProperty("name").GetString() == "LevelEffects");
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(global::LevelEffects)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ShareLevelEffects)));
        Assert.Equal(256, (int)global::LevelEffects.Phoenix);
        Assert.Equal(256, levelEffects.GetProperty("criticalValues").GetProperty("Phoenix").GetProperty("shared").GetInt32());
        Assert.Equal(0, levelEffects.GetProperty("criticalValues").GetProperty("Phoenix").GetProperty("shareValueAfterByteCast").GetInt32());
        var expectedRuntimeUses = new[]
        {
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/UserObject.cs:93", Expression: "LevelEffects = info.LevelEffects;"),
            (Side: "pc", Source: "Client_VorticeDX11/MirScenes/GameScene.cs:1938", Expression: "case (short)ServerPacketIds.ObjectLevelEffects:"),
            (Side: "pc", Source: "Client_VorticeDX11/MirScenes/GameScene.cs:1939", Expression: "ObjectLevelEffects((S.ObjectLevelEffects)p);"),
            (Side: "pc", Source: "Client_VorticeDX11/MirScenes/GameScene.cs:5542", Expression: "private void ObjectLevelEffects(S.ObjectLevelEffects p)"),
            (Side: "pc", Source: "Client_VorticeDX11/MirScenes/GameScene.cs:5551", Expression: "temp.LevelEffects = p.LevelEffects;"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:106", Expression: "public LevelEffects LevelEffects;"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:162", Expression: "LevelEffects = info.LevelEffects;"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:723", Expression: "if (LevelEffects == LevelEffects.None) return;"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:726", Expression: "if (LevelEffects.HasFlag(LevelEffects.BlueDragon))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:734", Expression: "if (LevelEffects.HasFlag(LevelEffects.RedDragon))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:742", Expression: "if (LevelEffects.HasFlag(LevelEffects.Mist))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:747", Expression: "if (LevelEffects.HasFlag(LevelEffects.Rebirth1))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:754", Expression: "if (LevelEffects.HasFlag(LevelEffects.Rebirth2))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:762", Expression: "if (LevelEffects.HasFlag(LevelEffects.Rebirth3))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:770", Expression: "if (LevelEffects.HasFlag(LevelEffects.NewBlue))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:778", Expression: "if (LevelEffects.HasFlag(LevelEffects.YellowDragon))"),
            (Side: "pc", Source: "Client_VorticeDX11/MirObjects/PlayerObject.cs:786", Expression: "if (LevelEffects.HasFlag(LevelEffects.Phoenix))"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/UserObject.cs:94", Expression: "LevelEffects = info.LevelEffects;"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirScenes/GameScene.cs:3332", Expression: "case (short)ServerPacketIds.ObjectLevelEffects:"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirScenes/GameScene.cs:3333", Expression: "ObjectLevelEffects((S.ObjectLevelEffects)p);"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirScenes/GameScene.cs:7110", Expression: "private void ObjectLevelEffects(S.ObjectLevelEffects p)"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirScenes/GameScene.cs:7119", Expression: "temp.LevelEffects = p.LevelEffects;"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:114", Expression: "public LevelEffects LevelEffects;"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:170", Expression: "LevelEffects = info.LevelEffects;"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:731", Expression: "if (LevelEffects == LevelEffects.None) return;"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:734", Expression: "if (LevelEffects.HasFlag(LevelEffects.BlueDragon))"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:741", Expression: "if (LevelEffects.HasFlag(LevelEffects.RedDragon))"),
            (Side: "mobile", Source: "Client_MonoGame.Shared/MirObjects/PlayerObject.cs:748", Expression: "if (LevelEffects.HasFlag(LevelEffects.Mist))"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:183", Expression: "public LevelEffects LevelEffects = LevelEffects.None;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1684", Expression: "public void SetLevelEffects()"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1686", Expression: "LevelEffects = LevelEffects.None;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1688", Expression: "if (Info.Flags[990]) LevelEffects |= LevelEffects.Mist;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1689", Expression: "if (Info.Flags[991]) LevelEffects |= LevelEffects.RedDragon;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1690", Expression: "if (Info.Flags[992]) LevelEffects |= LevelEffects.BlueDragon;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1691", Expression: "if (Info.Flags[993]) LevelEffects |= LevelEffects.Rebirth1;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1692", Expression: "if (Info.Flags[994]) LevelEffects |= LevelEffects.Rebirth2;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1693", Expression: "if (Info.Flags[995]) LevelEffects |= LevelEffects.Rebirth3;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1694", Expression: "if (Info.Flags[996]) LevelEffects |= LevelEffects.NewBlue;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1695", Expression: "if (Info.Flags[997]) LevelEffects |= LevelEffects.YellowDragon;"),
            (Side: "server", Source: "Server/MirObjects/HumanObject.cs:1696", Expression: "if (Info.Flags[998]) LevelEffects |= LevelEffects.Phoenix;"),
            (Side: "server", Source: "Server/MirObjects/HeroObject.cs:1330", Expression: "LevelEffects = LevelEffects,"),
            (Side: "server", Source: "Server/MirObjects/PlayerObject.cs:1283", Expression: "SetLevelEffects();"),
            (Side: "server", Source: "Server/MirObjects/PlayerObject.cs:1981", Expression: "LevelEffects = LevelEffects,"),
            (Side: "server", Source: "Server/MirObjects/PlayerObject.cs:5978", Expression: "LevelEffects = LevelEffects"),
            (Side: "server", Source: "Server/MirObjects/NPC/NPCSegment.cs:3822", Expression: "player.SetLevelEffects();"),
            (Side: "server", Source: "Server/MirObjects/NPC/NPCSegment.cs:3823", Expression: "var p = new S.ObjectLevelEffects { ObjectID = player.ObjectID, LevelEffects = player.LevelEffects };"),
        };
        var actualRuntimeUses = levelEffects.GetProperty("runtimeUses").EnumerateArray()
            .Select(item => (
                Side: item.GetProperty("side").GetString()!,
                Source: item.GetProperty("source").GetString()!,
                Expression: item.GetProperty("expression").GetString()!))
            .ToArray();
        var expectedSorted = expectedRuntimeUses
            .OrderBy(item => item.Side, StringComparer.Ordinal)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Expression, StringComparer.Ordinal)
            .ToArray();
        var actualSorted = actualRuntimeUses
            .OrderBy(item => item.Side, StringComparer.Ordinal)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Expression, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSorted, actualSorted);
        Assert.Equal(expectedRuntimeUses.Length, expectedRuntimeUses.Distinct().Count());
        Assert.Equal(actualRuntimeUses.Length, actualRuntimeUses.Distinct().Count());
        Assert.False(levelEffects.GetProperty("wireCompatible").GetBoolean());

        var mirClassNames = Enum.GetNames(typeof(global::MirClass));
        Assert.Equal(10, mirClassNames.Length);
        Assert.Contains("Warrior", mirClassNames);
        Assert.Contains("Wizard", mirClassNames);
        Assert.Contains("Taoist", mirClassNames);
        Assert.Contains("Assassin", mirClassNames);
        Assert.Contains("Archer", mirClassNames);
    }

    [Fact]
    public void Spell_packet_reader_and_writer_exposes_value_fork()
    {
        var sharedGolden = Convert.FromHexString("1400930001000000020000000300000033000101");
        var shareGolden = Convert.FromHexString("140093000100000002000000030000001F000101");
        var sharedPacket = new global::ServerPackets.ObjectSpell
        {
            ObjectID = 1,
            Location = new System.Drawing.Point(2, 3),
            Spell = global::Spell.FireBall,
            Direction = (global::MirDirection)1,
            Param = true,
        };
        var sharePacket = new ShareServerPackets.ObjectSpell
        {
            ObjectID = 1,
            Location = new System.Drawing.Point(2, 3),
            Spell = ShareSpell.FireBall,
            Direction = (ShareProtocol::MirDirection)1,
            Param = true,
        };
        Assert.Equal(sharedGolden, sharedPacket.GetPacketBytes().ToArray());
        Assert.Equal(shareGolden, sharePacket.GetPacketBytes().ToArray());

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = false;
            SharePacket.IsServer = false;
            var parsedShared = Assert.IsType<global::ServerPackets.ObjectSpell>(
                global::Packet.ReceivePacket(sharedGolden, out byte[] sharedExtra));
            var parsedShare = Assert.IsType<ShareServerPackets.ObjectSpell>(
                SharePacket.ReceivePacket(sharedGolden, out byte[] shareExtra));
            Assert.Equal(global::Spell.FireBall, parsedShared.Spell);
            Assert.Equal((ShareSpell)51, parsedShare.Spell);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
            parsedShared = Assert.IsType<global::ServerPackets.ObjectSpell>(
                global::Packet.ReceivePacket(shareGolden, out sharedExtra));
            parsedShare = Assert.IsType<ShareServerPackets.ObjectSpell>(
                SharePacket.ReceivePacket(shareGolden, out shareExtra));
            Assert.Equal((global::Spell)31, parsedShared.Spell);
            Assert.Equal(ShareSpell.FireBall, parsedShare.Spell);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void Monster_packet_reader_and_writer_exposes_value_fork()
    {
        var sharedGolden = Convert.FromHexString("300045000100000000000000000000000000000000490100000000000000000000000000000000000000000000000000");
        var shareGolden = Convert.FromHexString("300045000100000000000000000000000000000000100100000000000000000000000000000000000000000000000000");
        var sharedPacket = new global::ServerPackets.ObjectMonster
        {
            ObjectID = 1,
            Name = string.Empty,
            NameColour = System.Drawing.Color.FromArgb(0),
            Location = new System.Drawing.Point(0, 0),
            Image = global::Monster.AncientBringer,
        };
        var sharePacket = new ShareServerPackets.ObjectMonster
        {
            ObjectID = 1,
            Name = string.Empty,
            NameColour = System.Drawing.Color.FromArgb(0),
            Location = new System.Drawing.Point(0, 0),
            Image = ShareMonster.AncientBringer,
        };
        Assert.Equal(sharedGolden, sharedPacket.GetPacketBytes().ToArray());
        Assert.Equal(shareGolden, sharePacket.GetPacketBytes().ToArray());

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = false;
            SharePacket.IsServer = false;
            var parsedShared = Assert.IsType<global::ServerPackets.ObjectMonster>(
                global::Packet.ReceivePacket(sharedGolden, out byte[] sharedExtra));
            var parsedShare = Assert.IsType<ShareServerPackets.ObjectMonster>(
                SharePacket.ReceivePacket(sharedGolden, out byte[] shareExtra));
            Assert.Equal(global::Monster.AncientBringer, parsedShared.Image);
            Assert.Equal((ShareMonster)329, parsedShare.Image);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
            parsedShared = Assert.IsType<global::ServerPackets.ObjectMonster>(
                global::Packet.ReceivePacket(shareGolden, out sharedExtra));
            parsedShare = Assert.IsType<ShareServerPackets.ObjectMonster>(
                SharePacket.ReceivePacket(shareGolden, out shareExtra));
            Assert.Equal((global::Monster)272, parsedShared.Image);
            Assert.Equal(ShareMonster.AncientBringer, parsedShare.Image);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void BuffType_packet_reader_and_writer_exposes_value_fork()
    {
        var sharedGolden = Convert.FromHexString("09008F001901000000");
        var shareGolden = Convert.FromHexString("09008F001801000000");
        var sharedPacket = new global::ServerPackets.RemoveBuff
        {
            Type = global::BuffType.MagicShield,
            ObjectID = 1,
        };
        var sharePacket = new ShareServerPackets.RemoveBuff
        {
            Type = ShareBuffType.MagicShield,
            ObjectID = 1,
        };
        Assert.Equal(sharedGolden, sharedPacket.GetPacketBytes().ToArray());
        Assert.Equal(shareGolden, sharePacket.GetPacketBytes().ToArray());

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = false;
            SharePacket.IsServer = false;
            var parsedShared = Assert.IsType<global::ServerPackets.RemoveBuff>(
                global::Packet.ReceivePacket(sharedGolden, out byte[] sharedExtra));
            var parsedShare = Assert.IsType<ShareServerPackets.RemoveBuff>(
                SharePacket.ReceivePacket(sharedGolden, out byte[] shareExtra));
            Assert.Equal(global::BuffType.MagicShield, parsedShared.Type);
            Assert.Equal((ShareBuffType)25, parsedShare.Type);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
            parsedShared = Assert.IsType<global::ServerPackets.RemoveBuff>(
                global::Packet.ReceivePacket(shareGolden, out sharedExtra));
            parsedShare = Assert.IsType<ShareServerPackets.RemoveBuff>(
                SharePacket.ReceivePacket(shareGolden, out shareExtra));
            Assert.Equal((global::BuffType)24, parsedShared.Type);
            Assert.Equal(ShareBuffType.MagicShield, parsedShare.Type);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void SpellEffect_packet_reader_and_writer_exposes_value_fork()
    {
        var sharedGolden = Convert.FromHexString("15007A000100000005000000000000000000000000");
        var shareGolden = Convert.FromHexString("15007A000100000004000000000000000000000000");
        var sharedPacket = new global::ServerPackets.ObjectEffect
        {
            ObjectID = 1,
            Effect = global::SpellEffect.RedMoonEvil,
        };
        var sharePacket = new ShareServerPackets.ObjectEffect
        {
            ObjectID = 1,
            Effect = ShareSpellEffect.RedMoonEvil,
        };
        Assert.Equal(sharedGolden, sharedPacket.GetPacketBytes().ToArray());
        Assert.Equal(shareGolden, sharePacket.GetPacketBytes().ToArray());

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = false;
            SharePacket.IsServer = false;
            var parsedShared = Assert.IsType<global::ServerPackets.ObjectEffect>(
                global::Packet.ReceivePacket(sharedGolden, out byte[] sharedExtra));
            var parsedShare = Assert.IsType<ShareServerPackets.ObjectEffect>(
                SharePacket.ReceivePacket(sharedGolden, out byte[] shareExtra));
            Assert.Equal(global::SpellEffect.RedMoonEvil, parsedShared.Effect);
            Assert.Equal((ShareSpellEffect)5, parsedShare.Effect);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
            parsedShared = Assert.IsType<global::ServerPackets.ObjectEffect>(
                global::Packet.ReceivePacket(shareGolden, out sharedExtra));
            parsedShare = Assert.IsType<ShareServerPackets.ObjectEffect>(
                SharePacket.ReceivePacket(shareGolden, out shareExtra));
            Assert.Equal((global::SpellEffect)4, parsedShared.Effect);
            Assert.Equal(ShareSpellEffect.RedMoonEvil, parsedShare.Effect);
            Assert.Empty(sharedExtra);
            Assert.Empty(shareExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void LevelEffects_wire_width_matches_shared_and_share_packets()
    {
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(global::LevelEffects)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ShareLevelEffects)));

        var sharedEffect = new global::ServerPackets.ObjectLevelEffects
        {
            ObjectID = 1,
            LevelEffects = (global::LevelEffects)256,
        };
        var shareEffect = new ShareServerPackets.ObjectLevelEffects
        {
            ObjectID = 1,
            LevelEffects = (ShareLevelEffects)unchecked((byte)256),
        };

        var phoenixVector = new byte[] { 0x0A, 0x00, 0xDB, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var sharePhoenixVector = new byte[] { 0x0A, 0x00, 0xDB, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(phoenixVector, sharedEffect.GetPacketBytes().ToArray());
        Assert.Equal(sharePhoenixVector, shareEffect.GetPacketBytes().ToArray());

        var sharedPlayerVector = Convert.FromHexString("43001800010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001");
        var sharePlayerVector = Convert.FromHexString("43001800010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        var sharedUserVector = Convert.FromHexString("5D0015000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000000000000000000000000000000000000000000000000000000063000000");
        var shareUserVector = Convert.FromHexString("5D0015000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000063000000");
        var sharedPlayer = new global::ServerPackets.ObjectPlayer { ObjectID = 1, LevelEffects = (global::LevelEffects)256 };
        var sharePlayer = new ShareServerPackets.ObjectPlayer { ObjectID = 1, LevelEffects = (ShareLevelEffects)unchecked((byte)256) };
        var sharedUser = new global::ServerPackets.UserInformation { ObjectID = 1, LevelEffects = (global::LevelEffects)256 };
        var shareUser = new ShareServerPackets.UserInformation { ObjectID = 1, LevelEffects = (ShareLevelEffects)unchecked((byte)256) };
        Assert.Equal(sharedPlayerVector, sharedPlayer.GetPacketBytes().ToArray());
        Assert.Equal(sharePlayerVector, sharePlayer.GetPacketBytes().ToArray());
        Assert.Equal(sharedUserVector, sharedUser.GetPacketBytes().ToArray());
        Assert.Equal(shareUserVector, shareUser.GetPacketBytes().ToArray());
        Assert.Equal(new byte[] { 0x00, 0x01 }, sharedUserVector[57..59]);
        Assert.Equal(new byte[] { 0x00, 0x00 }, shareUserVector[57..59]);

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = false;
            SharePacket.IsServer = false;

            var parsedSharedEffect = Assert.IsType<global::ServerPackets.ObjectLevelEffects>(
                global::Packet.ReceivePacket(phoenixVector, out byte[] sharedEffectExtra));
            var parsedShareEffect = Assert.IsType<ShareServerPackets.ObjectLevelEffects>(
                SharePacket.ReceivePacket(phoenixVector, out byte[] shareEffectExtra));
            Assert.Equal((global::LevelEffects)256, parsedSharedEffect.LevelEffects);
            Assert.Equal((ShareLevelEffects)0, parsedShareEffect.LevelEffects);
            Assert.Empty(sharedEffectExtra);
            Assert.Empty(shareEffectExtra);

            var parsedSharedPlayer = Assert.IsType<global::ServerPackets.ObjectPlayer>(
                global::Packet.ReceivePacket(sharedPlayerVector, out byte[] sharedPlayerExtra));
            var parsedSharePlayer = Assert.IsType<ShareServerPackets.ObjectPlayer>(
                SharePacket.ReceivePacket(sharedPlayerVector, out byte[] sharePlayerExtra));
            Assert.Equal((global::LevelEffects)256, parsedSharedPlayer.LevelEffects);
            Assert.Equal((ShareLevelEffects)0, parsedSharePlayer.LevelEffects);
            Assert.Empty(sharedPlayerExtra);
            Assert.Empty(sharePlayerExtra);

            var parsedSharedUser = Assert.IsType<global::ServerPackets.UserInformation>(
                global::Packet.ReceivePacket(sharedUserVector, out byte[] sharedUserExtra));
            var parsedShareUser = Assert.IsType<ShareServerPackets.UserInformation>(
                SharePacket.ReceivePacket(sharedUserVector, out byte[] shareUserExtra));
            Assert.Equal((global::LevelEffects)256, parsedSharedUser.LevelEffects);
            Assert.Equal((ShareLevelEffects)0, parsedShareUser.LevelEffects);
            Assert.Empty(sharedUserExtra);
            Assert.Empty(shareUserExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void GuildNoticeChange_negative_update_is_a_wire_sentinel()
    {
        var negative = new global::ServerPackets.GuildNoticeChange { update = -1 };
        var positive = new global::ServerPackets.GuildNoticeChange();
        positive.notice.Add("a");
        positive.notice.Add("b");
        var negativeGolden = new byte[]
        {
            0x08, 0x00, (byte)ServerPacketIds.GuildNoticeChange, 0x00,
            0xFF, 0xFF, 0xFF, 0xFF,
        };
        var positiveGolden = new byte[]
        {
            0x0C, 0x00, (byte)ServerPacketIds.GuildNoticeChange, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x01, 0x61, 0x01, 0x62,
        };
        Assert.Equal(negativeGolden, negative.GetPacketBytes().ToArray());
        Assert.Equal(positiveGolden, positive.GetPacketBytes().ToArray());

        bool previousIsServer = global::Packet.IsServer;
        try
        {
            global::Packet.IsServer = false;
            var parsedNegative = Assert.IsType<global::ServerPackets.GuildNoticeChange>(
                global::Packet.ReceivePacket(negativeGolden, out byte[] negativeExtra));
            var parsedPositive = Assert.IsType<global::ServerPackets.GuildNoticeChange>(
                global::Packet.ReceivePacket(positiveGolden, out byte[] positiveExtra));
            Assert.Equal(-1, parsedNegative.update);
            Assert.Empty(parsedNegative.notice);
            Assert.Equal(2, parsedPositive.update);
            Assert.Equal(new[] { "a", "b" }, parsedPositive.notice);
            Assert.Empty(negativeExtra);
            Assert.Empty(positiveExtra);
        }
        finally
        {
            global::Packet.IsServer = previousIsServer;
        }
    }

    private static JsonElement FindPacket(JsonElement root, string direction, string name)
    {
        return root.GetProperty("packets").EnumerateArray().Single(packet =>
            packet.GetProperty("direction").GetString() == direction && packet.GetProperty("name").GetString() == name);
    }

    private static JsonElement Field(JsonElement packet, string name)
    {
        return packet.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("name").GetString() == name);
    }

    [Fact]
    public void Login_packet_round_trips_fixed_golden_vector()
    {
        // Length and payload bytes are fixed independently of the serializer under test:
        // ushort length (13), short packet id (Login = 5), then BinaryWriter strings.
        var golden = new byte[]
        {
            0x0D, 0x00, 0x05, 0x00,
            0x05, 0x61, 0x6C, 0x70, 0x68, 0x61,
            0x02, 0x70, 0x77,
        };
        var wireBytes = golden.Concat(new byte[] { 0xAA, 0xBB }).ToArray();
        var previousSharedIsServer = global::Packet.IsServer;
        var previousShareIsServer = SharePacket.IsServer;

        try
        {
            global::Packet.IsServer = true;
            var sharedPacket = global::Packet.ReceivePacket(wireBytes, out var sharedExtra);

            var sharedLogin = Assert.IsType<global::ClientPackets.Login>(sharedPacket);
            Assert.Equal("alpha", sharedLogin.AccountID);
            Assert.Equal("pw", sharedLogin.Password);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, sharedExtra);
            Assert.Equal(golden, sharedPacket.GetPacketBytes().ToArray());

            SharePacket.IsServer = true;
            var sharePacket = SharePacket.ReceivePacket(wireBytes, out var shareExtra);

            var login = Assert.IsType<ShareLogin>(sharePacket);
            Assert.Equal("alpha", login.AccountID);
            Assert.Equal("pw", login.Password);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, shareExtra);
            Assert.Equal(golden, sharePacket.GetPacketBytes().ToArray());
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }

    [Fact]
    public void Rental_slot_responses_round_trip_through_shared_wire_codec()
    {
        var deposit = new global::ServerPackets.DepositRentalItem { From = 12, To = 0, Success = true };
        var retrieve = new global::ServerPackets.RetrieveRentalItem { From = 0, To = 7, Success = false };
        bool previousIsServer = global::Packet.IsServer;
        try
        {
            global::Packet.IsServer = false;
            var parsedDeposit = Assert.IsType<global::ServerPackets.DepositRentalItem>(
                global::Packet.ReceivePacket(deposit.GetPacketBytes().ToArray(), out byte[] depositExtra));
            var parsedRetrieve = Assert.IsType<global::ServerPackets.RetrieveRentalItem>(
                global::Packet.ReceivePacket(retrieve.GetPacketBytes().ToArray(), out byte[] retrieveExtra));
            Assert.Equal(12, parsedDeposit.From);
            Assert.Equal(0, parsedDeposit.To);
            Assert.True(parsedDeposit.Success);
            Assert.Equal(0, parsedRetrieve.From);
            Assert.Equal(7, parsedRetrieve.To);
            Assert.False(parsedRetrieve.Success);
            Assert.Empty(depositExtra);
            Assert.Empty(retrieveExtra);
        }
        finally
        {
            global::Packet.IsServer = previousIsServer;
        }
    }

    [Fact]
    public void Fishing_packets_keep_fixed_ids_and_field_order_on_wire()
    {
        Assert.Equal(99, (int)ClientPacketIds.FishingCast);
        Assert.Equal(100, (int)ClientPacketIds.FishingChangeAutocast);
        Assert.Equal(198, (int)ServerPacketIds.FishingUpdate);

        var castGolden = new byte[] { 0x05, 0x00, 0x63, 0x00, 0x01 };
        var autoGolden = new byte[] { 0x05, 0x00, 0x64, 0x00, 0x00 };
        var updateGolden = new byte[]
        {
            0x1E, 0x00, 0xC6, 0x00,
            0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01,
            0x7B, 0x00, 0x00, 0x00,
            0x2D, 0x00, 0x00, 0x00,
            0xFE, 0xFF, 0xFF, 0xFF,
            0x03, 0x00, 0x00, 0x00,
            0x01,
        };

        bool previousSharedIsServer = global::Packet.IsServer;
        bool previousShareIsServer = SharePacket.IsServer;
        try
        {
            global::Packet.IsServer = true;
            SharePacket.IsServer = true;

            var sharedCast = Assert.IsType<global::ClientPackets.FishingCast>(
                global::Packet.ReceivePacket(castGolden, out byte[] sharedCastExtra));
            var shareCast = Assert.IsType<ShareFishingCast>(
                SharePacket.ReceivePacket(castGolden, out byte[] shareCastExtra));
            Assert.True(sharedCast.CastOut);
            Assert.True(shareCast.CastOut);
            Assert.Equal(castGolden, sharedCast.GetPacketBytes().ToArray());
            Assert.Equal(castGolden, shareCast.GetPacketBytes().ToArray());
            Assert.Empty(sharedCastExtra);
            Assert.Empty(shareCastExtra);

            var sharedAuto = Assert.IsType<global::ClientPackets.FishingChangeAutocast>(
                global::Packet.ReceivePacket(autoGolden, out byte[] sharedAutoExtra));
            var shareAuto = Assert.IsType<ShareFishingChangeAutocast>(
                SharePacket.ReceivePacket(autoGolden, out byte[] shareAutoExtra));
            Assert.False(sharedAuto.AutoCast);
            Assert.False(shareAuto.AutoCast);
            Assert.Equal(autoGolden, sharedAuto.GetPacketBytes().ToArray());
            Assert.Equal(autoGolden, shareAuto.GetPacketBytes().ToArray());
            Assert.Empty(sharedAutoExtra);
            Assert.Empty(shareAutoExtra);

            global::Packet.IsServer = false;
            SharePacket.IsServer = false;

            var sharedUpdate = Assert.IsType<global::ServerPackets.FishingUpdate>(
                global::Packet.ReceivePacket(updateGolden, out byte[] sharedUpdateExtra));
            var shareUpdate = Assert.IsType<ShareFishingUpdate>(
                SharePacket.ReceivePacket(updateGolden, out byte[] shareUpdateExtra));
            Assert.Equal(42, sharedUpdate.ObjectID);
            Assert.Equal(42, shareUpdate.ObjectID);
            Assert.True(sharedUpdate.Fishing);
            Assert.True(shareUpdate.Fishing);
            Assert.Equal(123, sharedUpdate.ProgressPercent);
            Assert.Equal(123, shareUpdate.ProgressPercent);
            Assert.Equal(45, sharedUpdate.ChancePercent);
            Assert.Equal(45, shareUpdate.ChancePercent);
            Assert.Equal(new System.Drawing.Point(-2, 3), sharedUpdate.FishingPoint);
            Assert.Equal(new System.Drawing.Point(-2, 3), shareUpdate.FishingPoint);
            Assert.True(sharedUpdate.FoundFish);
            Assert.True(shareUpdate.FoundFish);
            Assert.Equal(updateGolden, sharedUpdate.GetPacketBytes().ToArray());
            Assert.Equal(updateGolden, shareUpdate.GetPacketBytes().ToArray());
            Assert.Empty(sharedUpdateExtra);
            Assert.Empty(shareUpdateExtra);
        }
        finally
        {
            global::Packet.IsServer = previousSharedIsServer;
            SharePacket.IsServer = previousShareIsServer;
        }
    }
}

[CollectionDefinition("ProtocolWireCodec", DisableParallelization = true)]
public sealed class ProtocolWireCodecCollection
{
}
