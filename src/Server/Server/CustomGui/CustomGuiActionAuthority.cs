using Server.MirObjects;
using Shared.CustomGui;
using C = ClientPackets;
using S = ServerPackets;

namespace Server.CustomGui;

public enum CustomGuiCurrencyKind
{
    None = 0,
    Gold = 1,
    Credit = 2
}

public interface ICustomGuiActionTransaction
{
    string Commit();
    void Rollback();
}

public sealed class CustomGuiDelegateTransaction : ICustomGuiActionTransaction
{
    private readonly Func<string> _commit;
    private readonly Action _rollback;

    public CustomGuiDelegateTransaction(Func<string> commit, Action rollback)
    {
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
    }

    public string Commit() => _commit();
    public void Rollback() => _rollback();
}

public sealed class CustomGuiActionRule
{
    public string DocumentId { get; set; } = string.Empty;
    public uint DocumentRevision { get; set; } = 1;
    public long PackageSequence { get; set; } = 1;
    public string ActionId { get; set; } = string.Empty;
    public CustomGuiActionKind Action { get; set; }
    public int MinimumTextCharacters { get; set; }
    public int MaximumTextCharacters { get; set; }
    public Func<string, bool> TextValidator { get; set; }
    public int MinimumSelections { get; set; }
    public int MaximumSelections { get; set; }
    public IReadOnlySet<string> AllowedSelections { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    public int MinimumSubmittedItems { get; set; }
    public int MaximumSubmittedItems { get; set; }
    public int? RequiredNpcInfoIndex { get; set; }
    public int MaximumNpcDistance { get; set; } = Globals.DataRange;
    public DateTimeOffset? ActiveFromUtc { get; set; }
    public DateTimeOffset? ActiveUntilUtc { get; set; }
    public CustomGuiCurrencyKind Currency { get; set; }
    public ulong CurrencyCost { get; set; }
    public int MaximumUsageCount { get; set; } = -1;
    public Func<PlayerObject, int> UsageCount { get; set; }
    public Func<PlayerObject, C.CustomGuiAction, ICustomGuiActionTransaction> Prepare { get; set; }
}

public sealed class CustomGuiActionAuthority
{
    public const int MaximumRegisteredRules = 128;

    private sealed class RegisteredRule
    {
        public string DocumentId = string.Empty;
        public uint DocumentRevision;
        public long PackageSequence;
        public string ActionId = string.Empty;
        public CustomGuiActionKind Action;
        public int MinimumTextCharacters;
        public int MaximumTextCharacters;
        public Func<string, bool> TextValidator;
        public int MinimumSelections;
        public int MaximumSelections;
        public HashSet<string> AllowedSelections = new(StringComparer.Ordinal);
        public int MinimumSubmittedItems;
        public int MaximumSubmittedItems;
        public int? RequiredNpcInfoIndex;
        public int MaximumNpcDistance;
        public DateTimeOffset? ActiveFromUtc;
        public DateTimeOffset? ActiveUntilUtc;
        public CustomGuiCurrencyKind Currency;
        public ulong CurrencyCost;
        public int MaximumUsageCount;
        public Func<PlayerObject, int> UsageCount;
        public Func<PlayerObject, C.CustomGuiAction, ICustomGuiActionTransaction> Prepare;
    }

    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string, Exception> _errorSink;
    private readonly Dictionary<(string DocumentId, string ActionId), RegisteredRule> _rules = new();

    public CustomGuiActionAuthority(
        Func<DateTimeOffset> clock = null,
        Action<string, Exception> errorSink = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _errorSink = errorSink;
    }

    public int RuleCount => _rules.Count;

    public void Register(CustomGuiActionRule rule)
    {
        RegisterBatch(new[] { rule });
    }

    public void RegisterBatch(IEnumerable<CustomGuiActionRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        List<RegisteredRule> candidates = rules.Select(ValidateAndCopy).ToList();
        if (candidates.Count == 0) return;

        var next = new Dictionary<(string DocumentId, string ActionId), RegisteredRule>(_rules);
        foreach (RegisteredRule candidate in candidates)
        {
            var key = (candidate.DocumentId, candidate.ActionId);
            if (next.TryGetValue(key, out RegisteredRule current))
            {
                bool newer = candidate.PackageSequence > current.PackageSequence ||
                             candidate.PackageSequence == current.PackageSequence &&
                             candidate.DocumentRevision > current.DocumentRevision;
                if (!newer)
                    throw new InvalidOperationException("GUI09-RULE-VERSION：动作规则版本未前进");
                next[key] = candidate;
                continue;
            }
            next.Add(key, candidate);
        }

        if (next.Count > MaximumRegisteredRules)
            throw new InvalidOperationException("GUI09-RULE-LIMIT：动作规则数量超过上限");

        _rules.Clear();
        foreach (var pair in next) _rules.Add(pair.Key, pair.Value);
    }

    public void RegisterDocumentSnapshot(IEnumerable<CustomGuiActionRule> rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        List<RegisteredRule> candidates = rules.Select(ValidateAndCopy).ToList();
        if (candidates.Count == 0)
            throw new ArgumentException("GUI09-RULE-SNAPSHOT：文档动作快照不能为空", nameof(rules));

        RegisteredRule first = candidates[0];
        if (candidates.Any(candidate =>
                !string.Equals(candidate.DocumentId, first.DocumentId, StringComparison.Ordinal) ||
                candidate.DocumentRevision != first.DocumentRevision ||
                candidate.PackageSequence != first.PackageSequence) ||
            candidates.Select(candidate => candidate.ActionId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new ArgumentException("GUI09-RULE-SNAPSHOT：文档动作快照身份不一致或动作重复", nameof(rules));

        List<RegisteredRule> current = _rules.Values
            .Where(rule => string.Equals(rule.DocumentId, first.DocumentId, StringComparison.Ordinal))
            .ToList();
        if (current.Any(rule =>
                first.PackageSequence < rule.PackageSequence ||
                first.PackageSequence == rule.PackageSequence && first.DocumentRevision < rule.DocumentRevision))
            throw new InvalidOperationException("GUI09-RULE-VERSION：动作规则版本发生降级");

        var next = new Dictionary<(string DocumentId, string ActionId), RegisteredRule>(_rules);
        foreach (var key in next.Keys.Where(key =>
                     string.Equals(key.DocumentId, first.DocumentId, StringComparison.Ordinal)).ToList())
            next.Remove(key);
        foreach (RegisteredRule candidate in candidates)
            next.Add((candidate.DocumentId, candidate.ActionId), candidate);
        if (next.Count > MaximumRegisteredRules)
            throw new InvalidOperationException("GUI09-RULE-LIMIT：动作规则数量超过上限");

        _rules.Clear();
        foreach (var pair in next) _rules.Add(pair.Key, pair.Value);
    }

    public S.CustomGuiActionResult Handle(PlayerObject player, C.CustomGuiAction action, uint stateRevision)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (player == null || player.Info == null || player.Account == null || player.Dead)
            return Result(action, stateRevision, CustomGuiActionResultKind.Rejected,
                "GUI09-AUTH-PLAYER：玩家状态不允许执行动作");

        if (!_rules.TryGetValue((action.DocumentId, action.ActionId), out RegisteredRule rule))
            return Result(action, stateRevision, CustomGuiActionResultKind.Rejected,
                "GUI09-AUTH-ACTION：动作未在服务端登记");
        if (rule.DocumentRevision != action.DocumentRevision || rule.PackageSequence != action.PackageSequence)
            return Result(action, stateRevision, CustomGuiActionResultKind.Stale,
                "GUI09-AUTH-VERSION：动作规则与 GUI 版本不匹配");
        if (rule.Action != action.Action)
            return Result(action, stateRevision, CustomGuiActionResultKind.Invalid,
                "GUI09-AUTH-KIND：动作类型与服务端规则不匹配");

        string validationError = ValidateText(rule, action)
                                 ?? ValidateSelections(rule, action)
                                 ?? ValidateItems(rule, player, action)
                                 ?? ValidateNpc(rule, player)
                                 ?? ValidateActivity(rule)
                                 ?? ValidateCurrency(rule, player)
                                 ?? ValidateUsage(rule, player);
        if (validationError != null)
            return Result(action, stateRevision, CustomGuiActionResultKind.Rejected, validationError);

        ICustomGuiActionTransaction transaction;
        try
        {
            transaction = rule.Prepare(player, action);
            if (transaction == null)
                return Result(action, stateRevision, CustomGuiActionResultKind.Rejected,
                    "GUI09-AUTH-PREPARE：服务端未生成动作事务");
        }
        catch (Exception error)
        {
            ReportError("GUI09-AUTH-PREPARE", error);
            return Result(action, stateRevision, CustomGuiActionResultKind.Rejected,
                "GUI09-AUTH-PREPARE：服务端无法准备动作事务");
        }

        try
        {
            string message = transaction.Commit() ?? string.Empty;
            if (message.Length > CustomGuiProtocolLimits.MaximumMessageCharacters)
                throw new InvalidDataException("动作结果消息超过上限");
            S.CustomGuiActionResult accepted = Result(
                action, stateRevision, CustomGuiActionResultKind.Accepted, message);
            _ = accepted.GetPacketBytes().Count();
            return accepted;
        }
        catch (Exception commitError)
        {
            ReportError("GUI09-AUTH-TRANSACTION", commitError);
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackError)
            {
                ReportError("GUI09-AUTH-ROLLBACK", rollbackError);
                throw new AggregateException("GUI09-AUTH-ROLLBACK：动作事务回滚失败", commitError, rollbackError);
            }
            return Result(action, stateRevision, CustomGuiActionResultKind.Rejected,
                "GUI09-AUTH-TRANSACTION：动作事务提交失败并已回滚");
        }
    }

    public int InvalidatePackageSequence(long currentPackageSequence)
    {
        if (currentPackageSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentPackageSequence));
        var stale = _rules.Where(pair => pair.Value.PackageSequence != currentPackageSequence)
            .Select(pair => pair.Key).ToList();
        foreach (var key in stale) _rules.Remove(key);
        return stale.Count;
    }

    public int RemoveDocuments(IReadOnlySet<string> documentIds)
    {
        if (documentIds == null || documentIds.Count == 0) return 0;
        List<(string DocumentId, string ActionId)> stale = _rules.Keys
            .Where(key => documentIds.Contains(key.DocumentId))
            .ToList();
        foreach (var key in stale) _rules.Remove(key);
        return stale.Count;
    }

    public void Clear() => _rules.Clear();

    private void ReportError(string code, Exception error)
    {
        try { _errorSink?.Invoke(code, error); } catch { }
    }

    private string ValidateActivity(RegisteredRule rule)
    {
        DateTimeOffset now = _clock();
        if (rule.ActiveFromUtc.HasValue && now < rule.ActiveFromUtc.Value ||
            rule.ActiveUntilUtc.HasValue && now >= rule.ActiveUntilUtc.Value)
            return "GUI09-AUTH-ACTIVITY：活动当前不可用";
        return null;
    }

    private static string ValidateText(RegisteredRule rule, C.CustomGuiAction action)
    {
        int count = action.TextValue?.Length ?? 0;
        if (count < rule.MinimumTextCharacters || count > rule.MaximumTextCharacters)
            return "GUI09-AUTH-TEXT：文本长度不符合服务端规则";
        if (rule.TextValidator != null)
        {
            try
            {
                if (!rule.TextValidator(action.TextValue ?? string.Empty))
                    return "GUI09-AUTH-TEXT：文本内容未通过服务端规则";
            }
            catch
            {
                return "GUI09-AUTH-TEXT：文本校验器执行失败";
            }
        }
        return null;
    }

    private static string ValidateSelections(RegisteredRule rule, C.CustomGuiAction action)
    {
        List<string> values = action.SelectionIds ?? new();
        if (values.Count < rule.MinimumSelections || values.Count > rule.MaximumSelections)
            return "GUI09-AUTH-SELECTION：选择数量不符合服务端规则";
        if (values.Any(value => !rule.AllowedSelections.Contains(value)))
            return "GUI09-AUTH-SELECTION：选择项不在服务端白名单";
        return null;
    }

    private static string ValidateItems(RegisteredRule rule, PlayerObject player, C.CustomGuiAction action)
    {
        List<long> itemIds = action.ItemIds ?? new();
        if (itemIds.Count < rule.MinimumSubmittedItems || itemIds.Count > rule.MaximumSubmittedItems)
            return "GUI09-AUTH-ITEM：提交物品数量不符合服务端规则";
        if (itemIds.Count == 0) return null;

        var owned = new HashSet<ulong>(player.Info.Inventory
            .Where(item => item != null)
            .Select(item => item.UniqueID));
        if (itemIds.Any(itemId => itemId <= 0 || !owned.Contains((ulong)itemId)))
            return "GUI09-AUTH-ITEM：提交物品不属于玩家当前背包";
        return null;
    }

    private static string ValidateNpc(RegisteredRule rule, PlayerObject player)
    {
        if (!rule.RequiredNpcInfoIndex.HasValue) return null;
        if (player.CurrentMap == null || player.NPCObjectID == 0)
            return "GUI09-AUTH-NPC：玩家没有有效 NPC 上下文";
        NPCObject npc = player.CurrentMap.NPCs.FirstOrDefault(candidate =>
            candidate.ObjectID == player.NPCObjectID && candidate.Info?.Index == rule.RequiredNpcInfoIndex.Value);
        if (npc == null || !Functions.InRange(npc.CurrentLocation, player.CurrentLocation, rule.MaximumNpcDistance))
            return "GUI09-AUTH-NPC：NPC 不匹配或距离过远";
        return null;
    }

    private static string ValidateCurrency(RegisteredRule rule, PlayerObject player)
    {
        ulong balance = rule.Currency switch
        {
            CustomGuiCurrencyKind.None => 0,
            CustomGuiCurrencyKind.Gold => player.Account.Gold,
            CustomGuiCurrencyKind.Credit => player.Account.Credit,
            _ => 0
        };
        if (rule.Currency != CustomGuiCurrencyKind.None && balance < rule.CurrencyCost)
            return "GUI09-AUTH-CURRENCY：服务端余额不足";
        return null;
    }

    private static string ValidateUsage(RegisteredRule rule, PlayerObject player)
    {
        if (rule.MaximumUsageCount < 0) return null;
        int current;
        try
        {
            current = rule.UsageCount(player);
        }
        catch
        {
            return "GUI09-AUTH-USAGE：服务端次数读取失败";
        }
        if (current < 0 || current >= rule.MaximumUsageCount)
            return "GUI09-AUTH-USAGE：服务端次数已达上限";
        return null;
    }

    private static RegisteredRule ValidateAndCopy(CustomGuiActionRule rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        if (string.IsNullOrWhiteSpace(rule.DocumentId) ||
            rule.DocumentId.Length > CustomGuiProtocolLimits.MaximumIdentifierCharacters ||
            rule.DocumentRevision == 0 || rule.PackageSequence <= 0)
            throw new ArgumentException("GUI09-RULE-IDENTITY：规则 GUI 身份无效", nameof(rule));
        if (string.IsNullOrWhiteSpace(rule.ActionId) ||
            rule.ActionId.Length > CustomGuiProtocolLimits.MaximumActionIdCharacters ||
            !Enum.IsDefined(rule.Action))
            throw new ArgumentException("GUI09-RULE-ACTION：规则动作无效", nameof(rule));
        ValidateRange(rule.MinimumTextCharacters, rule.MaximumTextCharacters,
            CustomGuiProtocolLimits.MaximumInputCharacters, "文本");
        ValidateRange(rule.MinimumSelections, rule.MaximumSelections,
            CustomGuiProtocolLimits.MaximumSelectionCount, "选择");
        ValidateRange(rule.MinimumSubmittedItems, rule.MaximumSubmittedItems,
            CustomGuiProtocolLimits.MaximumSubmittedItemCount, "物品");
        if (rule.AllowedSelections == null || rule.AllowedSelections.Count > CustomGuiProtocolLimits.MaximumSelectionCount ||
            rule.AllowedSelections.Any(value => string.IsNullOrWhiteSpace(value) ||
                value.Length > CustomGuiProtocolLimits.MaximumIdentifierCharacters))
            throw new ArgumentException("GUI09-RULE-SELECTION：选择白名单无效", nameof(rule));
        if (rule.RequiredNpcInfoIndex.HasValue &&
            (rule.RequiredNpcInfoIndex.Value <= 0 || rule.MaximumNpcDistance <= 0 || rule.MaximumNpcDistance > Globals.DataRange))
            throw new ArgumentException("GUI09-RULE-NPC：NPC 规则无效", nameof(rule));
        if (rule.ActiveFromUtc.HasValue && rule.ActiveUntilUtc.HasValue && rule.ActiveFromUtc >= rule.ActiveUntilUtc)
            throw new ArgumentException("GUI09-RULE-ACTIVITY：活动时间无效", nameof(rule));
        if (!Enum.IsDefined(rule.Currency) ||
            rule.Currency == CustomGuiCurrencyKind.None && rule.CurrencyCost != 0 ||
            rule.CurrencyCost > uint.MaxValue)
            throw new ArgumentException("GUI09-RULE-CURRENCY：货币规则无效", nameof(rule));
        if (rule.MaximumUsageCount >= 0 && rule.UsageCount == null)
            throw new ArgumentException("GUI09-RULE-USAGE：次数规则缺少事实读取器", nameof(rule));
        if (rule.Prepare == null)
            throw new ArgumentException("GUI09-RULE-TRANSACTION：动作规则缺少事务准备器", nameof(rule));

        return new RegisteredRule
        {
            DocumentId = rule.DocumentId,
            DocumentRevision = rule.DocumentRevision,
            PackageSequence = rule.PackageSequence,
            ActionId = rule.ActionId,
            Action = rule.Action,
            MinimumTextCharacters = rule.MinimumTextCharacters,
            MaximumTextCharacters = rule.MaximumTextCharacters,
            TextValidator = rule.TextValidator,
            MinimumSelections = rule.MinimumSelections,
            MaximumSelections = rule.MaximumSelections,
            AllowedSelections = new HashSet<string>(rule.AllowedSelections, StringComparer.Ordinal),
            MinimumSubmittedItems = rule.MinimumSubmittedItems,
            MaximumSubmittedItems = rule.MaximumSubmittedItems,
            RequiredNpcInfoIndex = rule.RequiredNpcInfoIndex,
            MaximumNpcDistance = rule.MaximumNpcDistance,
            ActiveFromUtc = rule.ActiveFromUtc,
            ActiveUntilUtc = rule.ActiveUntilUtc,
            Currency = rule.Currency,
            CurrencyCost = rule.CurrencyCost,
            MaximumUsageCount = rule.MaximumUsageCount,
            UsageCount = rule.UsageCount,
            Prepare = rule.Prepare
        };
    }

    private static void ValidateRange(int minimum, int maximum, int hardMaximum, string field)
    {
        if (minimum < 0 || maximum < minimum || maximum > hardMaximum)
            throw new ArgumentException($"GUI09-RULE-LIMIT：{field}范围无效");
    }

    private static S.CustomGuiActionResult Result(
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
}
