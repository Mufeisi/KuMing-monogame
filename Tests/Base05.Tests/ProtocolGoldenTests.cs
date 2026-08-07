extern alias ShareProtocol;

using Xunit;
using ShareFishingCast = ShareProtocol::ClientPackets.FishingCast;
using ShareFishingChangeAutocast = ShareProtocol::ClientPackets.FishingChangeAutocast;
using ShareLogin = ShareProtocol::ClientPackets.Login;
using SharePacket = ShareProtocol::Packet;
using ShareFishingUpdate = ShareProtocol::ServerPackets.FishingUpdate;

namespace Base05.Tests;

[Collection("ProtocolWireCodec")]
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
