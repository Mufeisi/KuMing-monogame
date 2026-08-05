extern alias ShareProtocol;

using Xunit;
using ShareLogin = ShareProtocol::ClientPackets.Login;
using SharePacket = ShareProtocol::Packet;

namespace Base05.Tests;

public sealed class ProtocolGoldenTests
{
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
}
