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
}
