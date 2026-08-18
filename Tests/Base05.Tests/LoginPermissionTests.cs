using System.Collections.Generic;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Xunit;

namespace Base05.Tests;

/// <summary>
/// 服务端权限等级模型：管理员权限一律来自账号权限等级（AdminLevel > 0），
/// 聊天口令提权链路已删除（SEC-05 门禁不再要求游戏 GM 口令）。
/// </summary>
public sealed class LoginPermissionTests
{
    [Fact]
    public void AdminLevel为0的账号不构成管理员标记()
    {
        var account = new AccountInfo { AdminLevel = 0 };
        Assert.False(account.AdminAccount);
    }

    [Fact]
    public void AdminLevel大于0的账号构成管理员标记()
    {
        Assert.True(new AccountInfo { AdminLevel = 1 }.AdminAccount);
        Assert.True(new AccountInfo { AdminLevel = 2 }.AdminAccount);
    }

    [Fact]
    public void 普通账号聊天口令无法再升级为管理员()
    {
        var player = new ChatProbePlayer(new AccountInfo { AdminLevel = 0 });
        player.Chat("LOGIN");
        player.Chat("随便什么口令");

        Assert.False(player.IsGM, "普通账号不得通过聊天口令获得管理员身份");
        Assert.Empty(player.Messages.FindAll(m => m.text.Contains("请输入管理员密码")));
        Assert.Empty(player.Messages.FindAll(m => m.text.Contains("升级为游戏管理员")));
    }

    [Fact]
    public void 管理员账号聊天不出现登录提示()
    {
        var player = new ChatProbePlayer(new AccountInfo { AdminLevel = 1 })
        {
            IsGM = false,
        };
        player.Chat("LOGIN");
        player.Chat("口令");

        Assert.Empty(player.Messages.FindAll(m => m.text.Contains("请输入管理员密码")));
    }

    private sealed class ChatProbePlayer : PlayerObject
    {
        public ChatProbePlayer(AccountInfo account)
        {
            Account = account;
            Info = new CharacterInfo { Name = "权限探针" };
            CurrentMap = new Map(new MapInfo { FileName = "0" });
        }

        public List<(string text, ChatType type)> Messages { get; } = new();

        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
        public override void ReceiveChat(string text, ChatType type) => Messages.Add((text, type));
    }
}