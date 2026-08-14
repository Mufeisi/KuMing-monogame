#nullable enable

namespace Shared.CustomGui;

public interface ICustomGuiStateProjectionTarget
{
    void Apply(IReadOnlyDictionary<string, CustomGuiStateEntry> state);
}

public sealed class CustomGuiStateProjectionException : InvalidOperationException
{
    public CustomGuiStateProjectionException(string code, string message) : base($"{code}：{message}") => Code = code;
    public string Code { get; }
}

public sealed record CustomGuiOpenState(
    ulong WindowInstanceId, string DocumentId, uint DocumentRevision, long PackageSequence, Guid SessionNonce,
    long ExpiresAtUnixMilliseconds, uint StateRevision, IReadOnlyList<CustomGuiStateEntry> State);
public sealed record CustomGuiDeltaState(
    ulong WindowInstanceId, string DocumentId, uint DocumentRevision, long PackageSequence, Guid SessionNonce,
    uint StateRevision, IReadOnlyList<CustomGuiStateEntry> State);
public sealed record CustomGuiClientAction(
    ulong WindowInstanceId,
    string DocumentId,
    uint DocumentRevision,
    long PackageSequence,
    Guid SessionNonce,
    uint RequestSequence,
    CustomGuiActionKind Action,
    string ActionId,
    string TextValue,
    IReadOnlyList<string> SelectionIds,
    IReadOnlyList<long> ItemIds);

public sealed class CustomGuiClientStateSession
{
    private readonly CustomGuiRuntimeDocument _document;
    private readonly long _packageSequence;
    private readonly ICustomGuiStateProjectionTarget _target;
    private readonly IReadOnlyDictionary<string, HashSet<CustomGuiStateKind>> _bindings;
    private IReadOnlyDictionary<string, CustomGuiStateEntry> _state = new Dictionary<string, CustomGuiStateEntry>(StringComparer.Ordinal);
    private ulong _windowInstanceId;
    private Guid _sessionNonce;
    private uint _lastRequestSequence;

    public CustomGuiClientStateSession(CustomGuiRuntimeDocument document, long packageSequence, ICustomGuiStateProjectionTarget target)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        if (packageSequence <= 0) throw new ArgumentOutOfRangeException(nameof(packageSequence));
        if (string.IsNullOrWhiteSpace(document.DocumentId) || document.Revision <= 0 || document.Revision > uint.MaxValue)
            throw Error("GUI10-STATE-PACKAGE", "已接受 GUI 文档身份无效");
        _packageSequence = packageSequence;
        _bindings = BuildBindings(document);
    }

    public bool IsOpen => _windowInstanceId != 0;
    public uint StateRevision { get; private set; }
    public IReadOnlyDictionary<string, CustomGuiStateEntry> State => CloneState(_state);
    public uint LastResultSequence { get; private set; }
    public CustomGuiActionResultKind? LastResult { get; private set; }
    public string LastResultMessage { get; private set; } = string.Empty;

    public void Open(CustomGuiOpenState packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ValidatePackageIdentity(packet.DocumentId, packet.DocumentRevision, packet.PackageSequence);
        if (packet.WindowInstanceId == 0 || packet.SessionNonce == Guid.Empty || packet.StateRevision == 0)
            throw Error("GUI10-STATE-IDENTITY", "窗口身份无效");
        if (packet.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw Error("GUI10-STATE-EXPIRED", "窗口已过期");
        IReadOnlyDictionary<string, CustomGuiStateEntry> replacement = BuildState(packet.State, null);
        _target.Apply(replacement);
        _windowInstanceId = packet.WindowInstanceId;
        _sessionNonce = packet.SessionNonce;
        StateRevision = packet.StateRevision;
        _state = CloneState(replacement);
        LastResultSequence = 0;
        LastResult = null;
        LastResultMessage = string.Empty;
        _lastRequestSequence = 0;
    }

    public void ApplyDelta(CustomGuiDeltaState packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        EnsureOpen();
        ValidatePackageIdentity(packet.DocumentId, packet.DocumentRevision, packet.PackageSequence);
        if (packet.WindowInstanceId != _windowInstanceId || packet.SessionNonce != _sessionNonce)
            throw Error("GUI10-STATE-IDENTITY", "增量窗口身份不匹配");
        if (packet.StateRevision != StateRevision + 1)
            throw Error("GUI10-STATE-REVISION", "状态修订必须严格连续");
        IReadOnlyDictionary<string, CustomGuiStateEntry> replacement = BuildState(packet.State, _state);
        _target.Apply(replacement);
        StateRevision = packet.StateRevision;
        _state = CloneState(replacement);
    }

    public bool Close(ulong windowInstanceId)
    {
        if (!IsOpen || windowInstanceId != _windowInstanceId) return false;
        var empty = new Dictionary<string, CustomGuiStateEntry>(StringComparer.Ordinal);
        _target.Apply(empty);
        _state = empty;
        _windowInstanceId = 0;
        _sessionNonce = Guid.Empty;
        StateRevision = 0;
        _lastRequestSequence = 0;
        return true;
    }

    public CustomGuiClientAction SendAction(
        Action<CustomGuiClientAction> send,
        CustomGuiActionKind action,
        string actionId,
        string? textValue = null,
        IReadOnlyList<string>? selectionIds = null,
        IReadOnlyList<long>? itemIds = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        EnsureOpen();
        if (_lastRequestSequence == uint.MaxValue)
            throw Error("GUI10-ACTION-SEQUENCE", "动作序号已达上限");
        string text = textValue ?? string.Empty;
        List<string> selections = selectionIds?.ToList() ?? new();
        List<long> items = itemIds?.ToList() ?? new();
        CustomGuiProtocolCodec.ValidateActionPayload(action, text, selections, items);
        var request = new CustomGuiClientAction(
            _windowInstanceId,
            _document.DocumentId,
            (uint)_document.Revision,
            _packageSequence,
            _sessionNonce,
            _lastRequestSequence + 1,
            action,
            actionId ?? string.Empty,
            text,
            selections,
            items);
        send(request);
        _lastRequestSequence = request.RequestSequence;
        return request;
    }

    public void AcceptActionResult(ulong windowInstanceId, uint requestSequence, uint stateRevision, CustomGuiActionResultKind result, string? message)
    {
        EnsureOpen();
        if (windowInstanceId != _windowInstanceId || requestSequence == 0)
            throw Error("GUI10-STATE-IDENTITY", "动作结果身份无效");
        if (requestSequence <= LastResultSequence)
            throw Error("GUI10-STATE-RESULT", "动作结果序号必须递增");
        if (stateRevision < StateRevision || stateRevision > StateRevision + 1)
            throw Error("GUI10-STATE-REVISION", "动作结果状态修订超出当前窗口范围");
        LastResultSequence = requestSequence;
        LastResult = result;
        LastResultMessage = message ?? string.Empty;
    }

    private IReadOnlyDictionary<string, CustomGuiStateEntry> BuildState(
        IReadOnlyList<CustomGuiStateEntry>? entries,
        IReadOnlyDictionary<string, CustomGuiStateEntry>? baseline)
    {
        var result = baseline is null
            ? new Dictionary<string, CustomGuiStateEntry>(StringComparer.Ordinal)
            : baseline.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CustomGuiStateEntry entry in entries ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.BindingKey) || !seen.Add(entry.BindingKey))
                throw Error("GUI10-STATE-BINDING", "状态绑定为空或重复");
            if (!_bindings.TryGetValue(entry.BindingKey, out HashSet<CustomGuiStateKind>? kinds) || !kinds.Contains(entry.Kind))
                throw Error("GUI10-STATE-BINDING", $"未知或类型不匹配的状态绑定：{entry.BindingKey}");
            result[entry.BindingKey] = Clone(entry);
        }
        return result;
    }

    private void ValidatePackageIdentity(string documentId, uint documentRevision, long packageSequence)
    {
        if (!string.Equals(documentId, _document.DocumentId, StringComparison.Ordinal) ||
            documentRevision != _document.Revision || packageSequence != _packageSequence)
            throw Error("GUI10-STATE-PACKAGE", "状态与已接受签名包不匹配");
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw Error("GUI10-STATE-CLOSED", "窗口尚未打开");
    }

    private static IReadOnlyDictionary<string, HashSet<CustomGuiStateKind>> BuildBindings(CustomGuiRuntimeDocument document)
    {
        var result = new Dictionary<string, HashSet<CustomGuiStateKind>>(StringComparer.Ordinal);
        Dictionary<string, CustomGuiElement> elements = (document.Elements ?? []).ToDictionary(element => element.Id, StringComparer.Ordinal);
        foreach ((string key, string elementId) in CustomGuiStateBindingCatalog.Create(document))
        {
            CustomGuiElement element = elements[elementId];
            switch (element)
            {
                case CustomGuiText:
                    Add(result, key, CustomGuiStateKind.Text, CustomGuiStateKind.Integer);
                    break;
                case CustomGuiPanel:
                case CustomGuiWindow:
                    Add(result, key, CustomGuiStateKind.Boolean);
                    break;
                case CustomGuiProgressBar:
                    Add(result, key, CustomGuiStateKind.Progress);
                    break;
                case CustomGuiList:
                    Add(result, key, CustomGuiStateKind.List);
                    break;
                case CustomGuiItemSlot:
                    Add(result, key, CustomGuiStateKind.ItemSlots);
                    break;
                case CustomGuiButton when key.EndsWith(".visible", StringComparison.Ordinal):
                    Add(result, key, CustomGuiStateKind.ButtonVisible);
                    break;
                case CustomGuiButton:
                    Add(result, key, CustomGuiStateKind.ButtonEnabled);
                    break;
                case CustomGuiTextInput:
                    Add(result, key, CustomGuiStateKind.Text);
                    break;
            }
        }
        return result;
    }

    private static void Add(Dictionary<string, HashSet<CustomGuiStateKind>> result, string key, params CustomGuiStateKind[] kinds)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!result.TryGetValue(key, out HashSet<CustomGuiStateKind>? values)) result.Add(key, values = []);
        foreach (CustomGuiStateKind kind in kinds) values.Add(kind);
    }

    public static CustomGuiStateEntry Clone(CustomGuiStateEntry source) => new()
    {
        BindingKey = source.BindingKey ?? string.Empty,
        Kind = source.Kind,
        TextValue = source.TextValue ?? string.Empty,
        BooleanValue = source.BooleanValue,
        IntegerValue = source.IntegerValue,
        CurrentValue = source.CurrentValue,
        MaximumValue = source.MaximumValue,
        ListItems = (source.ListItems ?? []).Select(item => new CustomGuiStateListItem(item.Id, item.PrimaryText, item.SecondaryText, item.AssetId)).ToList(),
        ItemSlots = (source.ItemSlots ?? []).Select(item => new CustomGuiStateItemSlot(item.SlotId, item.ItemId, item.AssetId, item.DisplayName, item.Quantity, item.Enabled)).ToList(),
    };

    private static IReadOnlyDictionary<string, CustomGuiStateEntry> CloneState(IReadOnlyDictionary<string, CustomGuiStateEntry> source) =>
        source.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.Ordinal);

    private static CustomGuiStateProjectionException Error(string code, string message) => new(code, message);
}

public static class CustomGuiStateBindingCatalog
{
    public static IReadOnlyDictionary<string, string> Create(CustomGuiRuntimeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CustomGuiElement element in document.Elements ?? [])
        {
            switch (element)
            {
                case CustomGuiText:
                case CustomGuiPanel:
                case CustomGuiWindow:
                case CustomGuiList:
                    result[element.Id] = element.Id;
                    break;
                case CustomGuiProgressBar progress:
                    result[string.IsNullOrWhiteSpace(progress.BindingKey) ? progress.Id : progress.BindingKey] = progress.Id;
                    break;
                case CustomGuiItemSlot slot:
                    result[string.IsNullOrWhiteSpace(slot.BindingKey) ? slot.Id : slot.BindingKey] = slot.Id;
                    break;
                case CustomGuiButton button:
                    result[$"{button.Id}.visible"] = button.Id;
                    result[$"{button.Id}.enabled"] = button.Id;
                    break;
                case CustomGuiTextInput input when !string.IsNullOrWhiteSpace(input.BindingKey):
                    result[input.BindingKey] = input.Id;
                    break;
            }
        }
        return result;
    }
}
