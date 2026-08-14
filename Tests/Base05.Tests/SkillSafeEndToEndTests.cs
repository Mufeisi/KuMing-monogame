using Server.Authoring;
using Server.MirDatabase;
using Server.MirEnvir;
using Xunit;

namespace Base05.Tests;

[Collection("TLS环境")]
public sealed class SkillSafeEndToEndTests
{
    [Fact]
    public void 火球术_安全保存重载客户端投影与战斗结果保持一致()
    {
        string path = Path.Combine(Path.GetTempPath(), $"LEG08-FireBall-{Guid.NewGuid():N}.bin");
        var source = CreateFireBall();
        SkillInspectionSnapshot before = SkillInspector.Build(source, "火球术技能书");
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft("火球术·已审核", 42));

        try
        {
            SkillEditCommitResult commit = session.TryCommit(() =>
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);
                source.Save(writer);
            });

            Assert.True(commit.Completed, commit.Error);
            Assert.True(File.Exists(path));

            MagicInfo reloaded;
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
                reloaded = new MagicInfo(reader, version: 71);

            Assert.Equal("火球术·已审核", reloaded.Name);
            Assert.Equal((byte)42, reloaded.Icon);
            AssertCombatFieldsEqual(CreateFireBall(), reloaded);

            SkillInspectionSnapshot after = SkillInspector.Build(reloaded, "火球术技能书");
            Assert.Equal(before.Levels, after.Levels);
            Assert.Equal(before.Range, after.Range);

            var serverMagic = new UserMagic(Spell.FireBall)
            {
                Info = reloaded,
                Level = 3,
                Key = 6,
                Experience = 700,
                CastTime = 1234
            };
            ClientMagic approved = serverMagic.CreateClientMagic();
            ClientMagic roundTrip = RoundTrip(approved);

            Assert.Equal(reloaded.Name, roundTrip.Name);
            Assert.Equal(reloaded.Icon, roundTrip.Icon);
            Assert.Equal(reloaded.Range, roundTrip.Range);
            Assert.Equal(reloaded.BaseCost, roundTrip.BaseCost);
            Assert.Equal(reloaded.LevelCost, roundTrip.LevelCost);
            Assert.Equal(serverMagic.GetDelay(), roundTrip.Delay);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)255)]
    public void 图标白名单边界值_由服务端类型与会话重验接受(byte icon)
    {
        var source = CreateFireBall();
        source.Icon = 1;
        var session = new SkillEditingSession(source);
        session.Observe(new SkillSafeDraft(source.Name, icon));

        SkillEditCommitResult result = session.TryCommit(() => { });

        Assert.True(result.Completed, result.Error);
        Assert.Equal(icon, source.Icon);
    }

    [Fact]
    public void 隐藏测试服务器_可启动并消费火球术只读空间时间与服务端投影()
    {
        var server = new Envir();
        server.Start(new EnvirStartOptions
        {
            EnforceProductionSecurity = false,
            LoadResources = false,
            BindNetwork = false,
            StartScripts = false,
            StartHttp = false,
            SaveOnStop = false,
            Multithreaded = false,
        });

        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => server.StartState is EnvirStartState.Ready or EnvirStartState.Failed,
                TimeSpan.FromSeconds(2)), "隐藏测试服务器启动超时。");
            Assert.Equal(EnvirStartState.Ready, server.StartState);

            var fireBall = CreateFireBall();
            SkillSpatialProfile spatial = SkillSpatialInspector.Build(fireBall.Spell, 3);
            SkillTimelineProfile timeline = SkillTimelineInspector.Build(fireBall.Spell, 5);
            SkillInspectionSnapshot approved = SkillInspector.Build(fireBall, "火球术技能书");

            Assert.True(spatial.IsModeled);
            Assert.True(timeline.IsModeled);
            Assert.Contains(timeline.Events, item => item.ServerAuthoritative && item.Timing.Contains("750 ms"));
            Assert.Empty(approved.Diagnostics);
        }
        finally
        {
            server.Stop();
        }

        Assert.Equal(EnvirStartState.Stopped, server.StartState);
    }

    private static ClientMagic RoundTrip(ClientMagic source)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            source.Save(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        return new ClientMagic(reader);
    }

    private static void AssertCombatFieldsEqual(MagicInfo expected, MagicInfo actual)
    {
        Assert.Equal(expected.Spell, actual.Spell);
        Assert.Equal(expected.BaseCost, actual.BaseCost);
        Assert.Equal(expected.LevelCost, actual.LevelCost);
        Assert.Equal(expected.Level1, actual.Level1);
        Assert.Equal(expected.Level2, actual.Level2);
        Assert.Equal(expected.Level3, actual.Level3);
        Assert.Equal(expected.Need1, actual.Need1);
        Assert.Equal(expected.Need2, actual.Need2);
        Assert.Equal(expected.Need3, actual.Need3);
        Assert.Equal(expected.DelayBase, actual.DelayBase);
        Assert.Equal(expected.DelayReduction, actual.DelayReduction);
        Assert.Equal(expected.PowerBase, actual.PowerBase);
        Assert.Equal(expected.PowerBonus, actual.PowerBonus);
        Assert.Equal(expected.MPowerBase, actual.MPowerBase);
        Assert.Equal(expected.MPowerBonus, actual.MPowerBonus);
        Assert.Equal(expected.MultiplierBase, actual.MultiplierBase);
        Assert.Equal(expected.MultiplierBonus, actual.MultiplierBonus);
        Assert.Equal(expected.Range, actual.Range);
    }

    private static MagicInfo CreateFireBall() => new()
    {
        Name = "火球术",
        Spell = Spell.FireBall,
        Icon = 0,
        Level1 = 7,
        Level2 = 9,
        Level3 = 11,
        Need1 = 200,
        Need2 = 350,
        Need3 = 700,
        BaseCost = 3,
        LevelCost = 2,
        DelayBase = 1800,
        DelayReduction = 0,
        MPowerBase = 8,
        MPowerBonus = 0,
        PowerBase = 2,
        PowerBonus = 0,
        MultiplierBase = 1F,
        MultiplierBonus = 0,
        Range = 9
    };
}
