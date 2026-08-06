using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class MobileHeroStateTests
{
    [Fact]
    public void ManageHeroes_snapshot_populates_current_and_storage()
    {
        var state = new MobileHeroState();
        var packet = new ServerPackets.ManageHeroes
        {
            MaximumCount = 3,
            CurrentHero = Hero(10, "当前英雄", 42),
            Heroes = new[] { Hero(11, "备用一", 20), null, Hero(13, "备用三", 8) },
        };

        Assert.True(state.ApplyManageHeroes(packet));
        Assert.True(state.IsOpen);
        Assert.Equal(3, state.MaximumCount);
        Assert.Equal("当前英雄", state.CurrentHero.Name);
        Assert.Equal("备用一", state.Heroes[0].Name);
        Assert.Null(state.Heroes[1]);
        Assert.Equal("备用三", state.Heroes[2].Name);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Empty_snapshot_is_open_but_has_no_stale_heroes()
    {
        var state = new MobileHeroState();
        state.ApplyManageHeroes(new ServerPackets.ManageHeroes
        {
            MaximumCount = 1,
            CurrentHero = Hero(10, "旧英雄", 2),
            Heroes = new[] { Hero(11, "旧备用", 1) },
        });

        Assert.True(state.ApplyManageHeroes(new ServerPackets.ManageHeroes { MaximumCount = 0, Heroes = System.Array.Empty<ClientHeroInformation>() }));
        Assert.True(state.IsOpen);
        Assert.Null(state.CurrentHero);
        Assert.Empty(state.Heroes);
        Assert.Null(state.Error);
    }

    [Fact]
    public void ChangeHero_swaps_selected_slot_with_current_hero()
    {
        var state = new MobileHeroState();
        state.ApplyManageHeroes(new ServerPackets.ManageHeroes
        {
            MaximumCount = 2,
            CurrentHero = Hero(10, "当前英雄", 42),
            Heroes = new[] { Hero(11, "备用一", 20) },
        });

        Assert.True(state.ApplyChangeHero(new ServerPackets.ChangeHero { FromIndex = 0 }));
        Assert.Equal("备用一", state.CurrentHero.Name);
        Assert.Equal("当前英雄", state.Heroes[0].Name);
        Assert.Null(state.Error);
    }

    [Fact]
    public void Invalid_change_response_preserves_snapshot_and_surfaces_failure()
    {
        var state = new MobileHeroState();
        state.ApplyManageHeroes(new ServerPackets.ManageHeroes
        {
            MaximumCount = 2,
            CurrentHero = Hero(10, "当前英雄", 42),
            Heroes = new[] { Hero(11, "备用一", 20) },
        });

        Assert.False(state.ApplyChangeHero(new ServerPackets.ChangeHero { FromIndex = 9 }));
        Assert.True(state.IsOpen);
        Assert.Equal("当前英雄", state.CurrentHero.Name);
        Assert.Equal("备用一", state.Heroes[0].Name);
        Assert.Contains("索引", state.Error);
    }

    [Fact]
    public void Null_change_response_preserves_snapshot_and_surfaces_failure()
    {
        var state = new MobileHeroState();
        state.ApplyManageHeroes(new ServerPackets.ManageHeroes
        {
            MaximumCount = 2,
            CurrentHero = Hero(10, "当前英雄", 42),
            Heroes = new[] { Hero(11, "备用一", 20) },
        });

        Assert.False(state.ApplyChangeHero(null));
        Assert.Equal("当前英雄", state.CurrentHero.Name);
        Assert.Equal("备用一", state.Heroes[0].Name);
        Assert.Contains("为空", state.Error);
    }

    [Fact]
    public void Session_reset_discards_previous_snapshot_and_error()
    {
        var state = new MobileHeroState();
        state.ApplyManageHeroes(new ServerPackets.ManageHeroes
        {
            MaximumCount = 1,
            CurrentHero = Hero(10, "当前英雄", 42),
            Heroes = new[] { Hero(11, "备用一", 20) },
        });
        state.ApplyChangeHero(new ServerPackets.ChangeHero { FromIndex = 3 });

        state.ResetForSession();

        Assert.False(state.IsOpen);
        Assert.Equal(0, state.MaximumCount);
        Assert.Null(state.CurrentHero);
        Assert.Empty(state.Heroes);
        Assert.Null(state.Error);
    }

    private static ClientHeroInformation Hero(int index, string name, ushort level)
    {
        return new ClientHeroInformation
        {
            Index = index,
            Name = name,
            Level = level,
            Class = MirClass.战士,
            Gender = MirGender.Male,
        };
    }
}
