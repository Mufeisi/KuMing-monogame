using System.Buffers.Binary;
using System.Security.Cryptography;
using Shared.CustomGui;
using C = ClientPackets;
using S = ServerPackets;

namespace Server.CustomGui;

public sealed class CustomGuiSessionDecision
{
    internal CustomGuiSessionDecision(bool accepted, CustomGuiActionResultKind result, string message, uint stateRevision)
    {
        Accepted = accepted;
        Result = result;
        Message = message;
        StateRevision = stateRevision;
    }

    public bool Accepted { get; }
    public CustomGuiActionResultKind Result { get; }
    public string Message { get; }
    public uint StateRevision { get; }
}

public sealed class CustomGuiSessionController
{
    public const int MaximumActiveSessions = 8;
    public const long MaximumSessionLifetimeMilliseconds = 30 * 60 * 1000;

    private sealed class Session
    {
        public ulong WindowInstanceId;
        public string DocumentId = string.Empty;
        public uint DocumentRevision;
        public long PackageSequence;
        public Guid SessionNonce;
        public long ExpiresAtUnixMilliseconds;
        public uint StateRevision;
        public uint LastRequestSequence;
    }

    private readonly Action<Packet> _send;
    private readonly Func<bool> _playerInGame;
    private readonly Func<long> _utcNowMilliseconds;
    private readonly Func<Guid> _nonceFactory;
    private readonly Func<ulong> _windowFactory;
    private readonly Func<C.CustomGuiAction, uint, S.CustomGuiActionResult> _acceptedActionHandler;
    private readonly Func<bool> _featureEnabled;
    private readonly Action<Exception> _actionErrorSink;
    private readonly Dictionary<ulong, Session> _sessions = new();

    public CustomGuiSessionController(
        Action<Packet> send,
        Func<bool> playerInGame,
        Func<long> utcNowMilliseconds = null,
        Func<Guid> nonceFactory = null,
        Func<ulong> windowFactory = null,
        Func<C.CustomGuiAction, uint, S.CustomGuiActionResult> acceptedActionHandler = null,
        Func<bool> featureEnabled = null,
        Action<Exception> actionErrorSink = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _playerInGame = playerInGame ?? throw new ArgumentNullException(nameof(playerInGame));
        _utcNowMilliseconds = utcNowMilliseconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _nonceFactory = nonceFactory ?? CreateNonce;
        _windowFactory = windowFactory ?? CreateWindowInstanceId;
        _acceptedActionHandler = acceptedActionHandler;
        _featureEnabled = featureEnabled ?? (() => true);
        _actionErrorSink = actionErrorSink;
    }

    public int ActiveCount => _sessions.Count;

    public S.CustomGuiOpen Open(
        string documentId,
        uint documentRevision,
        long packageSequence,
        long expiresAtUnixMilliseconds,
        uint stateRevision,
        List<CustomGuiStateEntry> state)
    {
        EnsurePlayerInGame();
        EnsureFeatureEnabled();
        ExpireDueSessions();

        long now = _utcNowMilliseconds();
        if (expiresAtUnixMilliseconds <= now)
            throw new InvalidOperationException("GUI08-SESSION-EXPIRED：不能打开已过期窗口");
        long maximumExpiry = now > long.MaxValue - MaximumSessionLifetimeMilliseconds
            ? long.MaxValue
            : now + MaximumSessionLifetimeMilliseconds;
        if (expiresAtUnixMilliseconds > maximumExpiry)
            throw new InvalidOperationException("GUI08-SESSION-LIFETIME：窗口有效期超过上限");

        Session replaced = _sessions.Values.FirstOrDefault(x => string.Equals(x.DocumentId, documentId, StringComparison.Ordinal));
        if (replaced == null && _sessions.Count >= MaximumActiveSessions)
            throw new InvalidOperationException("GUI08-SESSION-LIMIT：活动窗口数量超过上限");

        var session = new Session
        {
            WindowInstanceId = NextWindowInstanceId(),
            DocumentId = documentId ?? string.Empty,
            DocumentRevision = documentRevision,
            PackageSequence = packageSequence,
            SessionNonce = NextNonce(),
            ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds,
            StateRevision = stateRevision
        };
        var packet = new S.CustomGuiOpen
        {
            WindowInstanceId = session.WindowInstanceId,
            DocumentId = session.DocumentId,
            DocumentRevision = session.DocumentRevision,
            PackageSequence = session.PackageSequence,
            SessionNonce = session.SessionNonce,
            ExpiresAtUnixMilliseconds = session.ExpiresAtUnixMilliseconds,
            StateRevision = session.StateRevision,
            State = CloneState(state)
        };

        _ = packet.GetPacketBytes().Count();

        if (replaced != null)
        {
            _sessions.Remove(replaced.WindowInstanceId);
            SendClose(replaced.WindowInstanceId, CustomGuiCloseReason.Replaced, "GUI08-SESSION-REPLACED：窗口已被新版本替换");
        }

        _sessions.Add(session.WindowInstanceId, session);
        _send(packet);
        return CloneOpenPacket(packet);
    }

    public CustomGuiSessionDecision Handle(C.CustomGuiAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        CustomGuiSessionDecision decision;
        if (!_featureEnabled())
        {
            EnforceAvailability();
            decision = Reject(CustomGuiActionResultKind.Rejected, "GUI08-SESSION-DISABLED：活动窗口已由 Kill Switch 停用", 0);
        }
        else if (!_playerInGame())
        {
            decision = Reject(CustomGuiActionResultKind.Invalid, "GUI08-SESSION-PLAYER：玩家不在有效游戏会话", 0);
        }
        else if (!_sessions.TryGetValue(action.WindowInstanceId, out Session session))
        {
            decision = Reject(CustomGuiActionResultKind.Stale, "GUI08-SESSION-STALE：窗口会话不存在", 0);
        }
        else if (_utcNowMilliseconds() >= session.ExpiresAtUnixMilliseconds)
        {
            _sessions.Remove(session.WindowInstanceId);
            decision = Reject(CustomGuiActionResultKind.Expired, "GUI08-SESSION-EXPIRED：窗口会话已过期", session.StateRevision);
        }
        else if (action.SessionNonce != session.SessionNonce)
        {
            decision = Reject(CustomGuiActionResultKind.Invalid, "GUI08-SESSION-NONCE：窗口随机数不匹配", session.StateRevision);
        }
        else if (!IdentityMatches(session, action))
        {
            decision = Reject(CustomGuiActionResultKind.Stale, "GUI08-SESSION-VERSION：GUI 版本身份不匹配", session.StateRevision);
        }
        else if (action.RequestSequence <= session.LastRequestSequence)
        {
            decision = Reject(CustomGuiActionResultKind.Stale, "GUI08-SESSION-REPLAY：动作已处理或发生重放", session.StateRevision);
        }
        else if (session.LastRequestSequence == uint.MaxValue || action.RequestSequence != session.LastRequestSequence + 1)
        {
            decision = Reject(CustomGuiActionResultKind.Stale, "GUI08-SESSION-ORDER：动作序号不连续", session.StateRevision);
        }
        else
        {
            session.LastRequestSequence = action.RequestSequence;
            decision = new CustomGuiSessionDecision(true, CustomGuiActionResultKind.Accepted, string.Empty, session.StateRevision);
            if (action.Action == CustomGuiActionKind.CloseWindow)
                _sessions.Remove(session.WindowInstanceId);
        }

        S.CustomGuiActionResult resultPacket;
        bool resultValidated = false;
        if (!decision.Accepted || action.Action == CustomGuiActionKind.CloseWindow)
        {
            resultPacket = BuildActionResult(action, decision.StateRevision, decision.Result, decision.Message);
        }
        else if (_acceptedActionHandler != null)
        {
            try
            {
                resultPacket = _acceptedActionHandler(action, decision.StateRevision)
                    ?? BuildActionResult(action, decision.StateRevision, CustomGuiActionResultKind.Rejected,
                        "GUI08-ACTION-UNHANDLED：服务端未返回动作结果");
                ValidateActionResultIdentity(action, decision.StateRevision, resultPacket);
                if (_sessions.TryGetValue(action.WindowInstanceId, out Session currentSession) &&
                    currentSession.StateRevision != decision.StateRevision)
                {
                    resultPacket.StateRevision = currentSession.StateRevision;
                    _ = resultPacket.GetPacketBytes().Count();
                    decision = new CustomGuiSessionDecision(
                        decision.Accepted, decision.Result, decision.Message, currentSession.StateRevision);
                }
                resultValidated = true;
            }
            catch (Exception error)
            {
                try { _actionErrorSink?.Invoke(error); } catch { }
                resultPacket = BuildActionResult(action, decision.StateRevision, CustomGuiActionResultKind.Rejected,
                    "GUI08-ACTION-ERROR：服务端动作处理失败");
            }
        }
        else
        {
            resultPacket = BuildActionResult(action, decision.StateRevision, CustomGuiActionResultKind.Rejected,
                "GUI08-ACTION-UNHANDLED：动作尚未登记服务端处理器");
        }
        if (!resultValidated)
            ValidateActionResultIdentity(action, decision.StateRevision, resultPacket);
        _send(resultPacket);

        if (decision.Accepted && action.Action == CustomGuiActionKind.CloseWindow)
            SendClose(action.WindowInstanceId, CustomGuiCloseReason.Requested, string.Empty);
        else if (decision.Result == CustomGuiActionResultKind.Expired)
            SendClose(action.WindowInstanceId, CustomGuiCloseReason.Expired, decision.Message);

        return decision;
    }

    public S.CustomGuiStateDelta UpdateState(
        ulong windowInstanceId,
        uint expectedStateRevision,
        List<CustomGuiStateEntry> state)
    {
        EnsurePlayerInGame();
        EnsureFeatureEnabled();
        ExpireDueSessions();
        if (!_sessions.TryGetValue(windowInstanceId, out Session session))
            throw new InvalidOperationException("GUI08-STATE-SESSION：窗口会话不存在");
        if (session.StateRevision != expectedStateRevision)
            throw new InvalidOperationException("GUI08-STATE-REVISION：窗口状态修订不匹配");
        if (expectedStateRevision == uint.MaxValue)
            throw new InvalidOperationException("GUI08-STATE-REVISION：窗口状态修订已达上限");

        var packet = new S.CustomGuiStateDelta
        {
            WindowInstanceId = session.WindowInstanceId,
            DocumentId = session.DocumentId,
            DocumentRevision = session.DocumentRevision,
            PackageSequence = session.PackageSequence,
            SessionNonce = session.SessionNonce,
            StateRevision = expectedStateRevision + 1,
            State = CloneState(state)
        };
        _ = packet.GetPacketBytes().Count();
        _send(packet);
        session.StateRevision = packet.StateRevision;
        return CloneDeltaPacket(packet);
    }

    public int ExpireDueSessions()
    {
        if (!_playerInGame()) return 0;

        long now = _utcNowMilliseconds();
        List<Session> expired = _sessions.Values
            .Where(x => x.ExpiresAtUnixMilliseconds <= now)
            .ToList();
        foreach (Session session in expired)
        {
            _sessions.Remove(session.WindowInstanceId);
            SendClose(session.WindowInstanceId, CustomGuiCloseReason.Expired, "GUI08-SESSION-EXPIRED：窗口会话已过期");
        }
        return expired.Count;
    }

    public int EnforceAvailability()
    {
        if (_featureEnabled()) return 0;

        List<Session> active = _sessions.Values.ToList();
        foreach (Session session in active)
        {
            _sessions.Remove(session.WindowInstanceId);
            SendClose(session.WindowInstanceId, CustomGuiCloseReason.Invalidated,
                "GUI08-SESSION-DISABLED：活动窗口已由 Kill Switch 停用");
        }
        return active.Count;
    }

    public int InvalidatePackageSequence(long currentPackageSequence)
    {
        if (currentPackageSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentPackageSequence),
                "GUI08-SESSION-VERSION：当前签名包序列必须为正数");
        List<Session> stale = _sessions.Values.Where(x => x.PackageSequence != currentPackageSequence).ToList();
        foreach (Session session in stale)
        {
            _sessions.Remove(session.WindowInstanceId);
            SendClose(session.WindowInstanceId, CustomGuiCloseReason.VersionChanged, "GUI08-SESSION-VERSION：GUI 资源版本已切换");
        }
        return stale.Count;
    }

    public int InvalidateDocuments(IReadOnlySet<string> documentIds)
    {
        if (documentIds == null || documentIds.Count == 0) return 0;
        List<Session> stale = _sessions.Values
            .Where(session => documentIds.Contains(session.DocumentId))
            .ToList();
        foreach (Session session in stale)
        {
            _sessions.Remove(session.WindowInstanceId);
            SendClose(session.WindowInstanceId, CustomGuiCloseReason.VersionChanged,
                "GUI11-HOOK-RELOAD：脚本窗口已随热重载失效");
        }
        return stale.Count;
    }

    public void Clear() => _sessions.Clear();

    private void EnsurePlayerInGame()
    {
        if (!_playerInGame())
            throw new InvalidOperationException("GUI08-SESSION-PLAYER：玩家不在有效游戏会话");
    }

    private void EnsureFeatureEnabled()
    {
        if (!_featureEnabled())
            throw new InvalidOperationException("GUI08-SESSION-DISABLED：活动窗口已由 Kill Switch 停用");
    }

    private ulong NextWindowInstanceId()
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            ulong value = _windowFactory();
            if (value != 0 && !_sessions.ContainsKey(value)) return value;
        }
        throw new InvalidOperationException("GUI08-SESSION-IDENTITY：无法分配唯一窗口实例");
    }

    private Guid NextNonce()
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Guid value = _nonceFactory();
            if (value != Guid.Empty && !_sessions.Values.Any(x => x.SessionNonce == value)) return value;
        }
        throw new InvalidOperationException("GUI08-SESSION-IDENTITY：无法分配唯一会话随机数");
    }

    private static bool IdentityMatches(Session session, C.CustomGuiAction action)
    {
        return string.Equals(session.DocumentId, action.DocumentId, StringComparison.Ordinal)
               && session.DocumentRevision == action.DocumentRevision
               && session.PackageSequence == action.PackageSequence;
    }

    private static CustomGuiSessionDecision Reject(CustomGuiActionResultKind result, string message, uint stateRevision) =>
        new(false, result, message, stateRevision);

    private static S.CustomGuiActionResult BuildActionResult(
        C.CustomGuiAction action,
        uint stateRevision,
        CustomGuiActionResultKind result,
        string message) => new()
    {
        WindowInstanceId = action.WindowInstanceId,
        RequestSequence = action.RequestSequence,
        StateRevision = stateRevision,
        Result = result,
        Message = message
    };

    private static void ValidateActionResultIdentity(C.CustomGuiAction action, uint expectedStateRevision, S.CustomGuiActionResult result)
    {
        if (result.WindowInstanceId != action.WindowInstanceId ||
            result.RequestSequence != action.RequestSequence ||
            result.StateRevision != expectedStateRevision)
            throw new InvalidOperationException("GUI08-ACTION-RESULT：动作处理器返回了错误的会话身份");
        _ = result.GetPacketBytes().Count();
    }

    private void SendClose(ulong windowInstanceId, CustomGuiCloseReason reason, string message)
    {
        _send(new S.CustomGuiClose { WindowInstanceId = windowInstanceId, Reason = reason, Message = message });
    }

    private static Guid CreateNonce() => new(RandomNumberGenerator.GetBytes(16));

    private static ulong CreateWindowInstanceId() =>
        BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    private static List<CustomGuiStateEntry> CloneState(List<CustomGuiStateEntry> state)
    {
        if (state == null) return new();
        return state.Select(entry => new CustomGuiStateEntry
        {
            BindingKey = entry.BindingKey,
            Kind = entry.Kind,
            TextValue = entry.TextValue,
            BooleanValue = entry.BooleanValue,
            IntegerValue = entry.IntegerValue,
            CurrentValue = entry.CurrentValue,
            MaximumValue = entry.MaximumValue,
            ListItems = entry.ListItems?.Select(item => new CustomGuiStateListItem(
                item.Id, item.PrimaryText, item.SecondaryText, item.AssetId)).ToList() ?? new(),
            ItemSlots = entry.ItemSlots?.Select(item => new CustomGuiStateItemSlot(
                item.SlotId, item.ItemId, item.AssetId, item.DisplayName, item.Quantity, item.Enabled)).ToList() ?? new()
        }).ToList();
    }

    private static S.CustomGuiOpen CloneOpenPacket(S.CustomGuiOpen source) => new()
    {
        WindowInstanceId = source.WindowInstanceId,
        DocumentId = source.DocumentId,
        DocumentRevision = source.DocumentRevision,
        PackageSequence = source.PackageSequence,
        SessionNonce = source.SessionNonce,
        ExpiresAtUnixMilliseconds = source.ExpiresAtUnixMilliseconds,
        StateRevision = source.StateRevision,
        State = CloneState(source.State)
    };

    private static S.CustomGuiStateDelta CloneDeltaPacket(S.CustomGuiStateDelta source) => new()
    {
        WindowInstanceId = source.WindowInstanceId,
        DocumentId = source.DocumentId,
        DocumentRevision = source.DocumentRevision,
        PackageSequence = source.PackageSequence,
        SessionNonce = source.SessionNonce,
        StateRevision = source.StateRevision,
        State = CloneState(source.State)
    };
}
