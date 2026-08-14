using Shared.CustomGui;
using Xunit;

public sealed class CustomGuiProtocolTests
{
    [Fact]
    public void PacketIdsAreAppendOnlyAndStable()
    {
        Assert.Equal(145, (short)ClientPacketIds.CustomGuiAction);
        Assert.Equal(275, (short)ServerPacketIds.CustomGuiOpen);
        Assert.Equal(276, (short)ServerPacketIds.CustomGuiStateDelta);
        Assert.Equal(277, (short)ServerPacketIds.CustomGuiActionResult);
        Assert.Equal(278, (short)ServerPacketIds.CustomGuiClose);
    }

    [Fact]
    public void ClientActionRoundTripsIdentityAndBoundedIntentOnly()
    {
        Guid nonce = Guid.NewGuid();
        var source = new ClientPackets.CustomGuiAction
        {
            WindowInstanceId = 42,
            DocumentId = "starter-event",
            DocumentRevision = 7,
            PackageSequence = 91,
            SessionNonce = nonce,
            RequestSequence = 3,
            Action = CustomGuiActionKind.SubmitItems,
            ActionId = "exchange-starter-token",
            TextValue = string.Empty,
            SelectionIds = [],
            ItemIds = [10001, 10002],
        };

        ClientPackets.CustomGuiAction parsed = ReceiveClient<ClientPackets.CustomGuiAction>(source);

        Assert.Equal(source.WindowInstanceId, parsed.WindowInstanceId);
        Assert.Equal(source.DocumentId, parsed.DocumentId);
        Assert.Equal(source.DocumentRevision, parsed.DocumentRevision);
        Assert.Equal(source.PackageSequence, parsed.PackageSequence);
        Assert.Equal(nonce, parsed.SessionNonce);
        Assert.Equal(source.RequestSequence, parsed.RequestSequence);
        Assert.Equal(CustomGuiActionKind.SubmitItems, parsed.Action);
        Assert.Equal(source.ActionId, parsed.ActionId);
        Assert.Equal(source.TextValue, parsed.TextValue);
        Assert.Equal(source.SelectionIds, parsed.SelectionIds);
        Assert.Equal(source.ItemIds, parsed.ItemIds);
    }

    [Fact]
    public void ServerPacketsRoundTripAllBoundedStateKindsAndLifecycleResults()
    {
        Guid nonce = Guid.NewGuid();
        List<CustomGuiStateEntry> state =
        [
            CustomGuiStateEntry.Text("title", "新手兑换"),
            CustomGuiStateEntry.Boolean("event.active", true),
            CustomGuiStateEntry.Integer("remaining", 2),
            CustomGuiStateEntry.Progress("progress", 3, 7),
            CustomGuiStateEntry.List("rewards", [new("reward-1", "新手武器", "绑定", "starter-sword")]),
            CustomGuiStateEntry.ForItemSlots("items", [new("slot-1", 10001, "starter-sword", "新手武器", 1, true)]),
            CustomGuiStateEntry.ButtonVisible("claim.visible", true),
            CustomGuiStateEntry.ButtonEnabled("claim.enabled", false),
        ];
        var open = new ServerPackets.CustomGuiOpen
        {
            WindowInstanceId = 42,
            DocumentId = "starter-event",
            DocumentRevision = 7,
            PackageSequence = 91,
            SessionNonce = nonce,
            ExpiresAtUnixMilliseconds = 2_000_000_000_000,
            StateRevision = 1,
            State = state,
        };
        ServerPackets.CustomGuiOpen parsedOpen = ReceiveServer<ServerPackets.CustomGuiOpen>(open);
        Assert.Equal(8, parsedOpen.State.Count);
        Assert.Equal(CustomGuiStateKind.Text, parsedOpen.State[0].Kind);
        Assert.Equal("新手兑换", parsedOpen.State[0].TextValue);
        Assert.Equal(7, parsedOpen.State[3].MaximumValue);
        Assert.Equal("reward-1", parsedOpen.State[4].ListItems[0].Id);
        Assert.Equal(10001, parsedOpen.State[5].ItemSlots[0].ItemId);
        Assert.False(parsedOpen.State[7].BooleanValue);

        var delta = new ServerPackets.CustomGuiStateDelta
        {
            WindowInstanceId = 42, DocumentId = "starter-event", DocumentRevision = 7,
            PackageSequence = 91, SessionNonce = nonce, StateRevision = 2,
            State = [CustomGuiStateEntry.Integer("remaining", 1)],
        };
        Assert.Equal(1, ReceiveServer<ServerPackets.CustomGuiStateDelta>(delta).State[0].IntegerValue);

        var result = new ServerPackets.CustomGuiActionResult
        {
            WindowInstanceId = 42, RequestSequence = 3, StateRevision = 2,
            Result = CustomGuiActionResultKind.Rejected, Message = "物品已变化",
        };
        Assert.Equal("物品已变化", ReceiveServer<ServerPackets.CustomGuiActionResult>(result).Message);

        var close = new ServerPackets.CustomGuiClose
        {
            WindowInstanceId = 42, Reason = CustomGuiCloseReason.Expired, Message = "窗口已过期",
        };
        Assert.Equal(CustomGuiCloseReason.Expired, ReceiveServer<ServerPackets.CustomGuiClose>(close).Reason);
    }

    [Fact]
    public void OversizedUnknownAndMalformedPayloadsFailClosed()
    {
        var oversized = ValidAction();
        oversized.TextValue = new string('界', CustomGuiProtocolLimits.MaximumInputCharacters + 1);
        Assert.Throws<InvalidDataException>(() => oversized.GetPacketBytes().ToArray());

        var unknown = ValidAction();
        unknown.Action = (CustomGuiActionKind)255;
        Assert.Throws<InvalidDataException>(() => unknown.GetPacketBytes().ToArray());

        var tooManySelections = ValidAction();
        tooManySelections.SelectionIds = Enumerable.Range(0, CustomGuiProtocolLimits.MaximumSelectionCount + 1).Select(i => i.ToString()).ToList();
        Assert.Throws<InvalidDataException>(() => tooManySelections.GetPacketBytes().ToArray());

        byte[] truncated = ValidAction().GetPacketBytes().ToArray();
        Array.Resize(ref truncated, truncated.Length - 1);
        Assert.Null(ReceiveClientRaw(truncated));

        byte[] trailing = ValidAction().GetPacketBytes().Append((byte)0x7F).ToArray();
        BitConverter.GetBytes((ushort)trailing.Length).CopyTo(trailing, 0);
        Assert.Throws<InvalidDataException>(() => ReceiveClientRaw(trailing));

        var ambiguous = ValidAction();
        ambiguous.Action = CustomGuiActionKind.SubmitItems;
        ambiguous.TextValue = "客户端伪造的附加结果";
        ambiguous.ItemIds = [10001];
        Assert.Throws<InvalidDataException>(() => ambiguous.GetPacketBytes().ToArray());

        byte[] unknownOnWire = ValidAction().GetPacketBytes().ToArray();
        int actionOffset = ActionKindOffset(unknownOnWire);
        unknownOnWire[actionOffset] = byte.MaxValue;
        Assert.Throws<InvalidDataException>(() => ReceiveClientRaw(unknownOnWire));

        byte[] invalidUtf8 = ValidAction().GetPacketBytes().ToArray();
        invalidUtf8[4 + 1 + 8 + 2] = byte.MaxValue;
        Assert.Throws<InvalidDataException>(() => ReceiveClientRaw(invalidUtf8));
    }

    [Fact]
    public void AggregateStateAndPacketByteLimitsAreEnforced()
    {
        var aggregate = ValidOpen();
        aggregate.State = Enumerable.Range(0, 5)
            .Select(group => CustomGuiStateEntry.List("list-" + group,
                Enumerable.Range(0, CustomGuiProtocolLimits.MaximumListItemsPerBinding)
                    .Select(item => new CustomGuiStateListItem($"{group}-{item}", "奖励", string.Empty, string.Empty))
                    .ToList()))
            .ToList();
        Assert.Throws<InvalidDataException>(() => aggregate.GetPacketBytes().ToArray());

        var packetBytes = ValidOpen();
        packetBytes.State = Enumerable.Range(0, 32)
            .Select(index => CustomGuiStateEntry.Text("text-" + index, new string('界', CustomGuiProtocolLimits.MaximumStateTextCharacters)))
            .ToList();
        Assert.Throws<InvalidDataException>(() => packetBytes.GetPacketBytes().ToArray());
    }

    private static ClientPackets.CustomGuiAction ValidAction() => new()
    {
        WindowInstanceId = 1, DocumentId = "starter-event", DocumentRevision = 1,
        PackageSequence = 1, SessionNonce = Guid.NewGuid(), RequestSequence = 1,
        Action = CustomGuiActionKind.RequestAction, ActionId = "claim",
    };

    private static ServerPackets.CustomGuiOpen ValidOpen() => new()
    {
        WindowInstanceId = 1, DocumentId = "starter-event", DocumentRevision = 1,
        PackageSequence = 1, SessionNonce = Guid.NewGuid(), ExpiresAtUnixMilliseconds = 2_000_000_000_000,
        StateRevision = 1,
    };

    private static int ActionKindOffset(byte[] bytes)
    {
        int offset = 4 + 1 + 8;
        int documentBytes = BitConverter.ToUInt16(bytes, offset);
        return offset + 2 + documentBytes + 4 + 8 + 16 + 4;
    }

    private static T ReceiveClient<T>(Packet packet) where T : Packet => (T)Receive(packet, isServer: true);
    private static T ReceiveServer<T>(Packet packet) where T : Packet => (T)Receive(packet, isServer: false);

    private static Packet Receive(Packet packet, bool isServer)
    {
        bool previous = Packet.IsServer;
        try
        {
            Packet.IsServer = isServer;
            Packet parsed = Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out byte[] extra);
            Assert.Empty(extra);
            return parsed;
        }
        finally { Packet.IsServer = previous; }
    }

    private static Packet ReceiveClientRaw(byte[] bytes)
    {
        bool previous = Packet.IsServer;
        try
        {
            Packet.IsServer = true;
            return Packet.ReceivePacket(bytes, out _);
        }
        finally { Packet.IsServer = previous; }
    }
}
