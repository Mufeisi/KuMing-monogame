using Server.CustomGui;
using Server.MirObjects;
using Shared.CustomGui;
using S = ServerPackets;

namespace Server.Scripting
{
    public sealed class CustomGuiScriptWindowDefinition
    {
        public string DocumentId { get; set; } = string.Empty;
        public uint DocumentRevision { get; set; } = 1;
        public long PackageSequence { get; set; } = 1;
        public uint InitialStateRevision { get; set; } = 1;
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);
        public Func<ScriptContext, PlayerObject, IReadOnlyList<CustomGuiStateEntry>> ProvideState { get; set; }
        public IReadOnlyList<CustomGuiActionRule> Actions { get; set; } = Array.Empty<CustomGuiActionRule>();
    }

    public sealed class CustomGuiScriptOpenPlan
    {
        internal CustomGuiScriptOpenPlan(
            string documentId,
            uint documentRevision,
            long packageSequence,
            uint stateRevision,
            long expiresAtUnixMilliseconds,
            List<CustomGuiStateEntry> state,
            IReadOnlyList<CustomGuiActionRule> actions)
        {
            DocumentId = documentId;
            DocumentRevision = documentRevision;
            PackageSequence = packageSequence;
            StateRevision = stateRevision;
            ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds;
            State = state;
            Actions = actions;
        }

        public string DocumentId { get; }
        public uint DocumentRevision { get; }
        public long PackageSequence { get; }
        public uint StateRevision { get; }
        public long ExpiresAtUnixMilliseconds { get; }
        public IReadOnlyList<CustomGuiStateEntry> State { get; }
        public IReadOnlyList<CustomGuiActionRule> Actions { get; }
    }

    public sealed class CustomGuiScriptPlanResult
    {
        private CustomGuiScriptPlanResult(bool success, string diagnostic, CustomGuiScriptOpenPlan plan)
        {
            Success = success;
            Diagnostic = diagnostic;
            Plan = plan;
        }

        public bool Success { get; }
        public string Diagnostic { get; }
        public CustomGuiScriptOpenPlan Plan { get; }

        internal static CustomGuiScriptPlanResult Accepted(CustomGuiScriptOpenPlan plan) =>
            new CustomGuiScriptPlanResult(true, string.Empty, plan);

        internal static CustomGuiScriptPlanResult Rejected(string diagnostic) =>
            new CustomGuiScriptPlanResult(false, diagnostic, null);
    }

    public sealed class CustomGuiScriptOpenResult
    {
        private CustomGuiScriptOpenResult(bool success, string diagnostic, S.CustomGuiOpen opened)
        {
            Success = success;
            Diagnostic = diagnostic;
            Opened = opened;
        }

        public bool Success { get; }
        public string Diagnostic { get; }
        public S.CustomGuiOpen Opened { get; }

        internal static CustomGuiScriptOpenResult Accepted(S.CustomGuiOpen opened) =>
            new CustomGuiScriptOpenResult(true, string.Empty, opened);

        internal static CustomGuiScriptOpenResult Rejected(string diagnostic) =>
            new CustomGuiScriptOpenResult(false, diagnostic, null);
    }

    /// <summary>
    /// C# 热更脚本的动态 GUI 旁路注册表。它只保存窗口声明、状态提供器和经 GUI-09 校验的动作规则。
    /// </summary>
    public sealed class CustomGuiScriptRegistry
    {
        public const int MaximumWindows = 32;
        public const int MaximumActionsPerWindow = 16;

        private sealed class RegisteredWindow
        {
            public string DocumentId = string.Empty;
            public uint DocumentRevision;
            public long PackageSequence;
            public uint InitialStateRevision;
            public TimeSpan Lifetime;
            public Func<ScriptContext, PlayerObject, IReadOnlyList<CustomGuiStateEntry>> ProvideState;
            public IReadOnlyList<CustomGuiActionRule> Actions = Array.Empty<CustomGuiActionRule>();
        }

        private readonly Dictionary<string, RegisteredWindow> _windows =
            new Dictionary<string, RegisteredWindow>(StringComparer.Ordinal);
        private readonly Action<string, Exception> _errorSink;

        public CustomGuiScriptRegistry(Action<string, Exception> errorSink = null)
        {
            _errorSink = errorSink;
        }

        public int Count => _windows.Count;
        public IReadOnlyCollection<string> DocumentIds => _windows.Keys.ToArray();

        public void Register(CustomGuiScriptWindowDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.DocumentId) ||
                definition.DocumentId.Length > CustomGuiProtocolLimits.MaximumIdentifierCharacters)
                throw new ArgumentException("GUI11-HOOK-WINDOW：窗口文档标识无效", nameof(definition));
            if (definition.DocumentRevision == 0 || definition.PackageSequence <= 0 ||
                definition.InitialStateRevision == 0)
                throw new ArgumentException("GUI11-HOOK-VERSION：窗口版本身份无效", nameof(definition));
            if (definition.Lifetime <= TimeSpan.Zero ||
                definition.Lifetime > TimeSpan.FromMilliseconds(CustomGuiSessionController.MaximumSessionLifetimeMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(definition),
                    "GUI11-HOOK-LIFETIME：窗口有效期超出允许范围");
            if (definition.ProvideState == null)
                throw new ArgumentException("GUI11-HOOK-DATA：必须提供有界状态提供器", nameof(definition));
            if (definition.Actions == null || definition.Actions.Count == 0 ||
                definition.Actions.Count > MaximumActionsPerWindow)
                throw new ArgumentOutOfRangeException(nameof(definition),
                    "GUI11-HOOK-ACTIONS：窗口动作数量超出允许范围");
            if (_windows.ContainsKey(definition.DocumentId))
                throw new InvalidOperationException("GUI11-HOOK-DUPLICATE：窗口文档标识重复");
            if (_windows.Count >= MaximumWindows)
                throw new InvalidOperationException("GUI11-HOOK-LIMIT：脚本窗口数量超过上限");

            List<CustomGuiActionRule> actions = definition.Actions.Select(action =>
                CloneRuleForWindow(action, definition)).ToList();
            new CustomGuiActionAuthority().RegisterBatch(actions);

            _windows.Add(definition.DocumentId, new RegisteredWindow
            {
                DocumentId = definition.DocumentId,
                DocumentRevision = definition.DocumentRevision,
                PackageSequence = definition.PackageSequence,
                InitialStateRevision = definition.InitialStateRevision,
                Lifetime = definition.Lifetime,
                ProvideState = definition.ProvideState,
                Actions = actions
            });
        }

        public CustomGuiScriptPlanResult PrepareOpen(
            ScriptContext context,
            PlayerObject player,
            string documentId,
            DateTimeOffset nowUtc)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (string.IsNullOrWhiteSpace(documentId) || !_windows.TryGetValue(documentId, out RegisteredWindow window))
                return CustomGuiScriptPlanResult.Rejected("GUI11-HOOK-WINDOW：脚本窗口未登记");

            IReadOnlyList<CustomGuiStateEntry> supplied;
            try
            {
                supplied = window.ProvideState(context, player) ?? Array.Empty<CustomGuiStateEntry>();
            }
            catch (Exception error)
            {
                Report("GUI11-HOOK-DATA", error);
                return CustomGuiScriptPlanResult.Rejected("GUI11-HOOK-DATA：脚本状态提供失败");
            }

            List<CustomGuiStateEntry> state;
            try
            {
                state = CloneState(supplied);
                ValidateState(window, nowUtc, state);
            }
            catch (Exception error)
            {
                Report("GUI11-HOOK-STATE", error);
                return CustomGuiScriptPlanResult.Rejected("GUI11-HOOK-STATE：脚本状态不符合动态 GUI 上限");
            }

            long expiresAt = checked(nowUtc.ToUnixTimeMilliseconds() + (long)window.Lifetime.TotalMilliseconds);
            return CustomGuiScriptPlanResult.Accepted(new CustomGuiScriptOpenPlan(
                window.DocumentId,
                window.DocumentRevision,
                window.PackageSequence,
                window.InitialStateRevision,
                expiresAt,
                state,
                window.Actions.Select(CloneRule).ToList()));
        }

        private void Report(string code, Exception error)
        {
            try { _errorSink?.Invoke(code, error); } catch { }
        }

        private static void ValidateState(RegisteredWindow window, DateTimeOffset nowUtc, List<CustomGuiStateEntry> state)
        {
            var packet = new S.CustomGuiOpen
            {
                WindowInstanceId = 1,
                DocumentId = window.DocumentId,
                DocumentRevision = window.DocumentRevision,
                PackageSequence = window.PackageSequence,
                SessionNonce = new Guid("11111111-1111-1111-1111-111111111111"),
                ExpiresAtUnixMilliseconds = checked(nowUtc.ToUnixTimeMilliseconds() + (long)window.Lifetime.TotalMilliseconds),
                StateRevision = window.InitialStateRevision,
                State = state
            };
            _ = packet.GetPacketBytes().Count();
        }

        private static CustomGuiActionRule CloneRuleForWindow(
            CustomGuiActionRule source,
            CustomGuiScriptWindowDefinition window)
        {
            if (source == null) throw new ArgumentException("GUI11-HOOK-ACTIONS：动作规则不能为空", nameof(window));
            CustomGuiActionRule clone = CloneRule(source);
            clone.DocumentId = window.DocumentId;
            clone.DocumentRevision = window.DocumentRevision;
            clone.PackageSequence = window.PackageSequence;
            return clone;
        }

        private static CustomGuiActionRule CloneRule(CustomGuiActionRule source) => new CustomGuiActionRule
        {
            DocumentId = source.DocumentId,
            DocumentRevision = source.DocumentRevision,
            PackageSequence = source.PackageSequence,
            ActionId = source.ActionId,
            Action = source.Action,
            MinimumTextCharacters = source.MinimumTextCharacters,
            MaximumTextCharacters = source.MaximumTextCharacters,
            TextValidator = source.TextValidator,
            MinimumSelections = source.MinimumSelections,
            MaximumSelections = source.MaximumSelections,
            AllowedSelections = new HashSet<string>(source.AllowedSelections ?? new HashSet<string>(), StringComparer.Ordinal),
            MinimumSubmittedItems = source.MinimumSubmittedItems,
            MaximumSubmittedItems = source.MaximumSubmittedItems,
            RequiredNpcInfoIndex = source.RequiredNpcInfoIndex,
            MaximumNpcDistance = source.MaximumNpcDistance,
            ActiveFromUtc = source.ActiveFromUtc,
            ActiveUntilUtc = source.ActiveUntilUtc,
            Currency = source.Currency,
            CurrencyCost = source.CurrencyCost,
            MaximumUsageCount = source.MaximumUsageCount,
            UsageCount = source.UsageCount,
            Prepare = source.Prepare
        };

        private static List<CustomGuiStateEntry> CloneState(IEnumerable<CustomGuiStateEntry> source) =>
            source.Select(entry => entry == null ? null : new CustomGuiStateEntry
            {
                BindingKey = entry.BindingKey,
                Kind = entry.Kind,
                TextValue = entry.TextValue,
                BooleanValue = entry.BooleanValue,
                IntegerValue = entry.IntegerValue,
                CurrentValue = entry.CurrentValue,
                MaximumValue = entry.MaximumValue,
                ListItems = entry.ListItems?.Select(item => item == null ? null : new CustomGuiStateListItem(
                    item.Id, item.PrimaryText, item.SecondaryText, item.AssetId)).ToList(),
                ItemSlots = entry.ItemSlots?.Select(item => item == null ? null : new CustomGuiStateItemSlot(
                    item.SlotId, item.ItemId, item.AssetId, item.DisplayName, item.Quantity, item.Enabled)).ToList()
            }).ToList();
    }
}
