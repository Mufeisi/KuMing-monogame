using Shared.CustomGui;

namespace ClientPackets
{
    public sealed class CustomGuiAction : Packet
    {
        public override short Index => (short)ClientPacketIds.CustomGuiAction;

        public ulong WindowInstanceId;
        public string DocumentId = string.Empty;
        public uint DocumentRevision;
        public long PackageSequence;
        public Guid SessionNonce;
        public uint RequestSequence;
        public CustomGuiActionKind Action;
        public string ActionId = string.Empty;
        public string TextValue = string.Empty;
        public List<string> SelectionIds = new();
        public List<long> ItemIds = new();

        protected override void ReadPacket(BinaryReader reader)
        {
            CustomGuiProtocolCodec.BeginRead(reader, CustomGuiProtocolLimits.MaximumActionPayloadBytes);
            CustomGuiProtocolCodec.ReadSessionIdentity(reader, out WindowInstanceId, out DocumentId, out DocumentRevision, out PackageSequence, out SessionNonce);
            RequestSequence = reader.ReadUInt32();
            if (RequestSequence == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：动作序号无效");
            Action = CustomGuiProtocolCodec.ReadEnum<CustomGuiActionKind>(reader, "动作类型");
            ActionId = CustomGuiProtocolCodec.ReadString(reader, CustomGuiProtocolLimits.MaximumActionIdCharacters, "动作标识");
            TextValue = CustomGuiProtocolCodec.ReadString(reader, CustomGuiProtocolLimits.MaximumInputCharacters, "动作文本", allowEmpty: true);
            SelectionIds = CustomGuiProtocolCodec.ReadStringList(reader, CustomGuiProtocolLimits.MaximumSelectionCount, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "选择标识");
            ItemIds = CustomGuiProtocolCodec.ReadInt64List(reader, CustomGuiProtocolLimits.MaximumSubmittedItemCount, "物品标识");
            CustomGuiProtocolCodec.ValidateActionPayload(Action, TextValue, SelectionIds, ItemIds);
            CustomGuiProtocolCodec.EndRead(reader);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            CustomGuiProtocolCodec.EnsureEnum(Action, "动作类型");
            CustomGuiProtocolCodec.ValidateActionPayload(Action, TextValue, SelectionIds, ItemIds);
            CustomGuiProtocolCodec.BeginWrite(writer);
            CustomGuiProtocolCodec.WriteSessionIdentity(writer, WindowInstanceId, DocumentId, DocumentRevision, PackageSequence, SessionNonce);
            if (RequestSequence == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：动作序号无效");
            writer.Write(RequestSequence);
            writer.Write((byte)Action);
            CustomGuiProtocolCodec.WriteString(writer, ActionId, CustomGuiProtocolLimits.MaximumActionIdCharacters, "动作标识");
            CustomGuiProtocolCodec.WriteString(writer, TextValue, CustomGuiProtocolLimits.MaximumInputCharacters, "动作文本", allowEmpty: true);
            CustomGuiProtocolCodec.WriteStringList(writer, SelectionIds, CustomGuiProtocolLimits.MaximumSelectionCount, CustomGuiProtocolLimits.MaximumIdentifierCharacters, "选择标识");
            CustomGuiProtocolCodec.WriteInt64List(writer, ItemIds, CustomGuiProtocolLimits.MaximumSubmittedItemCount, "物品标识");
            CustomGuiProtocolCodec.EndWrite(writer, CustomGuiProtocolLimits.MaximumActionPayloadBytes);
        }
    }
}

namespace ServerPackets
{
    public sealed class CustomGuiOpen : Packet
    {
        public override short Index => (short)ServerPacketIds.CustomGuiOpen;

        public ulong WindowInstanceId;
        public string DocumentId = string.Empty;
        public uint DocumentRevision;
        public long PackageSequence;
        public Guid SessionNonce;
        public long ExpiresAtUnixMilliseconds;
        public uint StateRevision;
        public List<CustomGuiStateEntry> State = new();

        protected override void ReadPacket(BinaryReader reader)
        {
            CustomGuiProtocolCodec.BeginRead(reader, CustomGuiProtocolLimits.MaximumOpenPayloadBytes);
            CustomGuiProtocolCodec.ReadSessionIdentity(reader, out WindowInstanceId, out DocumentId, out DocumentRevision, out PackageSequence, out SessionNonce);
            ExpiresAtUnixMilliseconds = reader.ReadInt64();
            StateRevision = reader.ReadUInt32();
            if (ExpiresAtUnixMilliseconds <= 0 || StateRevision == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：窗口期限或状态修订无效");
            State = CustomGuiProtocolCodec.ReadState(reader);
            CustomGuiProtocolCodec.EndRead(reader);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            CustomGuiProtocolCodec.BeginWrite(writer);
            CustomGuiProtocolCodec.WriteSessionIdentity(writer, WindowInstanceId, DocumentId, DocumentRevision, PackageSequence, SessionNonce);
            if (ExpiresAtUnixMilliseconds <= 0 || StateRevision == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：窗口期限或状态修订无效");
            writer.Write(ExpiresAtUnixMilliseconds);
            writer.Write(StateRevision);
            CustomGuiProtocolCodec.WriteState(writer, State);
            CustomGuiProtocolCodec.EndWrite(writer, CustomGuiProtocolLimits.MaximumOpenPayloadBytes);
        }
    }

    public sealed class CustomGuiStateDelta : Packet
    {
        public override short Index => (short)ServerPacketIds.CustomGuiStateDelta;

        public ulong WindowInstanceId;
        public string DocumentId = string.Empty;
        public uint DocumentRevision;
        public long PackageSequence;
        public Guid SessionNonce;
        public uint StateRevision;
        public List<CustomGuiStateEntry> State = new();

        protected override void ReadPacket(BinaryReader reader)
        {
            CustomGuiProtocolCodec.BeginRead(reader, CustomGuiProtocolLimits.MaximumDeltaPayloadBytes);
            CustomGuiProtocolCodec.ReadSessionIdentity(reader, out WindowInstanceId, out DocumentId, out DocumentRevision, out PackageSequence, out SessionNonce);
            StateRevision = reader.ReadUInt32();
            if (StateRevision == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：状态修订无效");
            State = CustomGuiProtocolCodec.ReadState(reader);
            CustomGuiProtocolCodec.EndRead(reader);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            CustomGuiProtocolCodec.BeginWrite(writer);
            CustomGuiProtocolCodec.WriteSessionIdentity(writer, WindowInstanceId, DocumentId, DocumentRevision, PackageSequence, SessionNonce);
            if (StateRevision == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：状态修订无效");
            writer.Write(StateRevision);
            CustomGuiProtocolCodec.WriteState(writer, State);
            CustomGuiProtocolCodec.EndWrite(writer, CustomGuiProtocolLimits.MaximumDeltaPayloadBytes);
        }
    }

    public sealed class CustomGuiActionResult : Packet
    {
        public override short Index => (short)ServerPacketIds.CustomGuiActionResult;

        public ulong WindowInstanceId;
        public uint RequestSequence;
        public uint StateRevision;
        public CustomGuiActionResultKind Result;
        public string Message = string.Empty;

        protected override void ReadPacket(BinaryReader reader)
        {
            CustomGuiProtocolCodec.BeginRead(reader, CustomGuiProtocolLimits.MaximumResultPayloadBytes);
            WindowInstanceId = reader.ReadUInt64();
            RequestSequence = reader.ReadUInt32();
            StateRevision = reader.ReadUInt32();
            if (WindowInstanceId == 0 || RequestSequence == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：动作结果身份无效");
            Result = CustomGuiProtocolCodec.ReadEnum<CustomGuiActionResultKind>(reader, "动作结果");
            Message = CustomGuiProtocolCodec.ReadString(reader, CustomGuiProtocolLimits.MaximumMessageCharacters, "动作结果消息", allowEmpty: true);
            CustomGuiProtocolCodec.EndRead(reader);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            CustomGuiProtocolCodec.BeginWrite(writer);
            if (WindowInstanceId == 0 || RequestSequence == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：动作结果身份无效");
            writer.Write(WindowInstanceId);
            writer.Write(RequestSequence);
            writer.Write(StateRevision);
            CustomGuiProtocolCodec.EnsureEnum(Result, "动作结果");
            writer.Write((byte)Result);
            CustomGuiProtocolCodec.WriteString(writer, Message, CustomGuiProtocolLimits.MaximumMessageCharacters, "动作结果消息", allowEmpty: true);
            CustomGuiProtocolCodec.EndWrite(writer, CustomGuiProtocolLimits.MaximumResultPayloadBytes);
        }
    }

    public sealed class CustomGuiClose : Packet
    {
        public override short Index => (short)ServerPacketIds.CustomGuiClose;

        public ulong WindowInstanceId;
        public CustomGuiCloseReason Reason;
        public string Message = string.Empty;

        protected override void ReadPacket(BinaryReader reader)
        {
            CustomGuiProtocolCodec.BeginRead(reader, CustomGuiProtocolLimits.MaximumResultPayloadBytes);
            WindowInstanceId = reader.ReadUInt64();
            if (WindowInstanceId == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：关闭窗口身份无效");
            Reason = CustomGuiProtocolCodec.ReadEnum<CustomGuiCloseReason>(reader, "关闭原因");
            Message = CustomGuiProtocolCodec.ReadString(reader, CustomGuiProtocolLimits.MaximumMessageCharacters, "关闭消息", allowEmpty: true);
            CustomGuiProtocolCodec.EndRead(reader);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            CustomGuiProtocolCodec.BeginWrite(writer);
            if (WindowInstanceId == 0) throw new InvalidDataException("GUI07-PROTOCOL-001：关闭窗口身份无效");
            writer.Write(WindowInstanceId);
            CustomGuiProtocolCodec.EnsureEnum(Reason, "关闭原因");
            writer.Write((byte)Reason);
            CustomGuiProtocolCodec.WriteString(writer, Message, CustomGuiProtocolLimits.MaximumMessageCharacters, "关闭消息", allowEmpty: true);
            CustomGuiProtocolCodec.EndWrite(writer, CustomGuiProtocolLimits.MaximumResultPayloadBytes);
        }
    }
}
