using Server.Scripting;
using Xunit;
using S = ServerPackets;

namespace Base05.Tests;

public sealed class LingFengNpcUiCompatibilityTests
{
    [Fact]
    public void 服务端对话构建器保留中文按钮与安全页面白名单()
    {
        var dialog = new NpcDialog();
        dialog.Say("欢迎来到比奇省");
        dialog.Button("领取奖励", "@REWARD");
        dialog.Close();

        Assert.Equal("欢迎来到比奇省", dialog.Lines[0]);
        Assert.Equal("<领取奖励/@REWARD>", dialog.Lines[1]);
        Assert.Equal("<关闭/@Exit>", dialog.Lines[2]);
        Assert.Contains("[@REWARD]", dialog.AllowedPageKeys);
        Assert.Contains("[@Exit]", dialog.AllowedPageKeys);
    }

    [Fact]
    public void NpcResponse协议往返保持中文行与按钮语法()
    {
        bool oldIsServer = Packet.IsServer;
        try
        {
            Packet.IsServer = false;
            var packet = new S.NPCResponse
            {
                Page = new List<string>
                {
                    "欢迎来到比奇省",
                    "<领取奖励/@REWARD>",
                    "(说明/https://example.invalid/help)"
                }
            };

            var parsed = Assert.IsType<S.NPCResponse>(
                Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out byte[] extra));
            Assert.Empty(extra);
            Assert.Equal(packet.Page, parsed.Page);
        }
        finally
        {
            Packet.IsServer = oldIsServer;
        }
    }

}
