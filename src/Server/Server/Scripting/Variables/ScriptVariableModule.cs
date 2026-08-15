using System.Runtime.CompilerServices;
using System.Text;
using Server.MirDatabase;
using Server.MirObjects;

namespace Server.Scripting.Variables
{
    public readonly struct ScriptVariableReference
    {
        private ScriptVariableReference(ScriptVariableScope scope, string key, bool legacy, int legacyIndex)
        {
            Scope = scope;
            Key = key ?? string.Empty;
            IsLegacy = legacy;
            LegacyIndex = legacyIndex;
        }

        public ScriptVariableScope Scope { get; }
        public string Key { get; }
        public bool IsLegacy { get; }
        public int LegacyIndex { get; }
        internal string StorageKey => IsLegacy ? "#" + LegacyIndex : Key;

        public static ScriptVariableReference Named(ScriptVariableScope scope, string key)
        {
            if (!ScriptVariableName.TryNormalize(key, out var normalized))
                throw new ArgumentException("变量名称无效。", nameof(key));
            return new ScriptVariableReference(scope, normalized, false, -1);
        }

        public static ScriptVariableReference Legacy(ScriptVariableScope scope, int index)
        {
            if (index < 0 || index > 999) throw new ArgumentOutOfRangeException(nameof(index));
            return new ScriptVariableReference(scope, string.Empty, true, index);
        }
    }

    public readonly struct ScriptVariableContext
    {
        private ScriptVariableContext(object owner, uint npcObjectId, object mapInstanceKey, object callFrame)
        {
            Owner = owner;
            NpcObjectId = npcObjectId;
            MapInstanceKey = mapInstanceKey;
            CallFrame = callFrame;
        }

        public object Owner { get; }
        public uint NpcObjectId { get; }
        public object MapInstanceKey { get; }
        public object CallFrame { get; }

        public static ScriptVariableContext ForConversation(
            object owner,
            uint npcObjectId,
            object mapInstanceKey = null,
            object callFrame = null) =>
            new ScriptVariableContext(
                owner ?? throw new ArgumentNullException(nameof(owner)),
                npcObjectId,
                mapInstanceKey,
                callFrame);

        public static ScriptVariableContext ForPlayer(
            object owner,
            object mapInstanceKey = null,
            object callFrame = null) =>
            new ScriptVariableContext(
                owner ?? throw new ArgumentNullException(nameof(owner)),
                0,
                mapInstanceKey,
                callFrame);

        public static ScriptVariableContext ForServer() =>
            new ScriptVariableContext(null, 0, null, null);
    }

    public readonly struct ScriptVariableMutation
    {
        private ScriptVariableMutation(
            ScriptVariableReference reference,
            ScriptVariableOperation operation,
            ScriptVariableValue operand)
        {
            Reference = reference;
            Operation = operation;
            Operand = operand;
        }

        public ScriptVariableReference Reference { get; }
        public ScriptVariableOperation Operation { get; }
        public ScriptVariableValue Operand { get; }

        public static ScriptVariableMutation Set(ScriptVariableReference reference, ScriptVariableValue value) =>
            new ScriptVariableMutation(reference, ScriptVariableOperation.Set, value);

        public static ScriptVariableMutation Apply(
            ScriptVariableReference reference,
            ScriptVariableOperation operation,
            ScriptVariableValue operand) =>
            new ScriptVariableMutation(reference, operation, operand);
    }

    public readonly struct ScriptVariableSelector
    {
        private ScriptVariableSelector(ScriptVariableScope scope)
        {
            Scope = scope;
        }

        public ScriptVariableScope Scope { get; }
        public static ScriptVariableSelector Conversation() => new ScriptVariableSelector(ScriptVariableScope.P);
        public static ScriptVariableSelector ScopeOnly(ScriptVariableScope scope) => new ScriptVariableSelector(scope);
    }

    public readonly struct ScriptVariableReadResult
    {
        internal ScriptVariableReadResult(
            bool success,
            bool found,
            ScriptVariableErrorCode errorCode,
            ScriptVariableValue value,
            string diagnostic)
        {
            Success = success;
            Found = found;
            ErrorCode = errorCode;
            Value = value;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public bool Found { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public ScriptVariableValue Value { get; }
        public string Diagnostic { get; }
    }

    public readonly struct ScriptVariableMutationResult
    {
        internal ScriptVariableMutationResult(
            bool success,
            ScriptVariableErrorCode errorCode,
            ScriptVariableValue oldValue,
            ScriptVariableValue newValue,
            string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            OldValue = oldValue;
            NewValue = newValue;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public ScriptVariableValue OldValue { get; }
        public ScriptVariableValue NewValue { get; }
        public string Diagnostic { get; }
    }

    public readonly struct ScriptVariableResetResult
    {
        internal ScriptVariableResetResult(bool success, ScriptVariableErrorCode errorCode, int clearedCount, string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            ClearedCount = clearedCount;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public int ClearedCount { get; }
        public string Diagnostic { get; }
    }

    public interface IScriptVariableModule
    {
        ScriptVariableReadResult Read(in ScriptVariableContext context, ScriptVariableReference reference);
        ScriptVariableMutationResult Mutate(in ScriptVariableContext context, ScriptVariableMutation mutation);
        ScriptVariableResetResult Reset(in ScriptVariableContext context, ScriptVariableSelector selector);
    }

    public sealed class ScriptVariableModule : IScriptVariableModule
    {
        public const int MaximumCompositeItems = 256;
        public const int MaximumCompositeItemUtf8Bytes = 1024;
        public const int MaximumOwnerCompositeUtf8Bytes = 256 * 1024;

        private sealed class OwnerState
        {
            public uint NpcObjectId;
            public object MapInstanceKey;
            public readonly Dictionary<ScriptVariableScope, Dictionary<string, ScriptVariableValue>> Scopes =
                new Dictionary<ScriptVariableScope, Dictionary<string, ScriptVariableValue>>();

            public Dictionary<string, ScriptVariableValue> GetScope(ScriptVariableScope scope)
            {
                if (Scopes.TryGetValue(scope, out var values)) return values;
                values = new Dictionary<string, ScriptVariableValue>(StringComparer.Ordinal);
                Scopes.Add(scope, values);
                return values;
            }
        }

        private readonly Func<ScriptVariableDeclarationSnapshot> _declarations;
        private readonly ConditionalWeakTable<object, OwnerState> _owners = new ConditionalWeakTable<object, OwnerState>();
        private readonly ConditionalWeakTable<object, OwnerState> _callFrames = new ConditionalWeakTable<object, OwnerState>();
        private readonly Dictionary<string, ScriptVariableValue> _serverRuntime =
            new Dictionary<string, ScriptVariableValue>(StringComparer.Ordinal);
        private readonly Func<bool> _canWrite;
        private readonly Action _requestAutoSave;
        private readonly ServerScriptVariableStore _serverPersistent;
        private readonly Action _requestServerAutoSave;
        private readonly Func<long> _currentDailyPeriod;

        public ScriptVariableModule(
            ScriptVariableDeclarationCatalog catalog,
            Func<bool> canWrite = null,
            Action requestAutoSave = null,
            ServerScriptVariableStore serverPersistent = null,
            Action requestServerAutoSave = null,
            Func<long> currentDailyPeriod = null)
            : this(() => (catalog ?? throw new ArgumentNullException(nameof(catalog))).Current,
                canWrite, requestAutoSave, serverPersistent, requestServerAutoSave, currentDailyPeriod)
        {
        }

        public ScriptVariableModule(
            Func<ScriptVariableDeclarationSnapshot> declarations,
            Func<bool> canWrite = null,
            Action requestAutoSave = null,
            ServerScriptVariableStore serverPersistent = null,
            Action requestServerAutoSave = null,
            Func<long> currentDailyPeriod = null)
        {
            _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
            int creatorThread = Environment.CurrentManagedThreadId;
            _canWrite = canWrite ?? (() => Environment.CurrentManagedThreadId == creatorThread);
            _requestAutoSave = requestAutoSave;
            _serverPersistent = serverPersistent ?? new ServerScriptVariableStore();
            _requestServerAutoSave = requestServerAutoSave;
            _currentDailyPeriod = currentDailyPeriod ??
                (() => ScriptVariableDailyPeriod.FromServerTime(DateTime.Now));
        }

        public ScriptVariableReadResult Read(in ScriptVariableContext context, ScriptVariableReference reference)
        {
            if (!_canWrite())
                return ReadFailure(ScriptVariableErrorCode.WrongThread, "变量状态只能在服务端主线程访问。");
            if (!TryValidateContext(context, reference.Scope, out var contextDiagnostic))
                return ReadFailure(ScriptVariableErrorCode.ContextUnavailable, contextDiagnostic);

            if (!TryResolveContract(reference, out var kind, out var defaultValue, out var diagnostic))
                return ReadFailure(ScriptVariableErrorCode.UnknownReference, diagnostic);

            if (TryGetPersistentStore(context, reference.Scope, out var persistentStore))
            {
                EnsureDailyPeriod(context, reference.Scope, persistentStore);
                if (persistentStore.TryGet(reference.Scope, reference.StorageKey, out var persistentValue))
                    return new ScriptVariableReadResult(true, true, ScriptVariableErrorCode.None, persistentValue, string.Empty);
                return new ScriptVariableReadResult(true, false, ScriptVariableErrorCode.None, defaultValue, string.Empty);
            }
            if (IsServerPersistentScope(reference.Scope))
            {
                if (_serverPersistent.TryGet(reference.Scope, reference.StorageKey, out var serverValue))
                    return new ScriptVariableReadResult(true, true, ScriptVariableErrorCode.None, serverValue, string.Empty);
                return new ScriptVariableReadResult(true, false, ScriptVariableErrorCode.None, defaultValue, string.Empty);
            }

            Dictionary<string, ScriptVariableValue> values = GetScopeValues(context, reference.Scope);
            if (values.TryGetValue(reference.StorageKey, out var value))
                return new ScriptVariableReadResult(true, true, ScriptVariableErrorCode.None, value, string.Empty);

            return new ScriptVariableReadResult(true, false, ScriptVariableErrorCode.None, defaultValue, string.Empty);
        }

        public ScriptVariableMutationResult Mutate(in ScriptVariableContext context, ScriptVariableMutation mutation)
        {
            ScriptVariableReadResult current = Read(context, mutation.Reference);
            if (!current.Success)
                return MutationFailure(current.ErrorCode, current.Value, current.Diagnostic);

            if (!TryResolveContract(mutation.Reference, out var targetKind, out _, out var diagnostic))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, current.Value, diagnostic);

            ScriptVariableResult computed = mutation.Operation == ScriptVariableOperation.Set
                ? CoerceForTarget(targetKind, mutation.Operand)
                : ScriptVariableArithmetic.Apply(current.Value, mutation.Operation, mutation.Operand);
            if (!computed.Success)
                return MutationFailure(computed.ErrorCode, current.Value, computed.Diagnostic);

            ScriptVariableResult coerced = CoerceForTarget(targetKind, computed.Value);
            if (!coerced.Success)
                return MutationFailure(coerced.ErrorCode, current.Value, coerced.Diagnostic);

            if ((targetKind == ScriptVariableKind.List || targetKind == ScriptVariableKind.Dictionary) &&
                !TryValidateCompositeMutation(context, mutation.Reference, coerced.Value, out diagnostic))
                return MutationFailure(ScriptVariableErrorCode.QuotaExceeded, current.Value, diagnostic);

            if (TryGetPersistentStore(context, mutation.Reference.Scope, out var persistentStore))
            {
                try
                {
                    persistentStore.Set(mutation.Reference.Scope, mutation.Reference.StorageKey, coerced.Value);
                    MarkPersistentOwnerDirty(context, mutation.Reference.Scope);
                }
                catch (InvalidOperationException ex)
                {
                    return MutationFailure(ScriptVariableErrorCode.QuotaExceeded, current.Value, ex.Message);
                }
                catch (InvalidDataException ex)
                {
                    return MutationFailure(ScriptVariableErrorCode.TypeMismatch, current.Value, ex.Message);
                }
            }
            else if (IsServerPersistentScope(mutation.Reference.Scope))
            {
                try
                {
                    _serverPersistent.Set(mutation.Reference.Scope, mutation.Reference.StorageKey, coerced.Value);
                    _requestServerAutoSave?.Invoke();
                }
                catch (InvalidOperationException ex)
                {
                    return MutationFailure(ScriptVariableErrorCode.QuotaExceeded, current.Value, ex.Message);
                }
                catch (InvalidDataException ex)
                {
                    return MutationFailure(ScriptVariableErrorCode.TypeMismatch, current.Value, ex.Message);
                }
            }
            else
            {
                Dictionary<string, ScriptVariableValue> values = GetScopeValues(context, mutation.Reference.Scope);
                values[mutation.Reference.StorageKey] = coerced.Value;
            }
            return new ScriptVariableMutationResult(
                true, ScriptVariableErrorCode.None, current.Value, coerced.Value, string.Empty);
        }

        public ScriptVariableResetResult Reset(in ScriptVariableContext context, ScriptVariableSelector selector)
        {
            if (!_canWrite())
                return new ScriptVariableResetResult(false, ScriptVariableErrorCode.WrongThread, 0, "变量状态只能在服务端主线程修改。");
            if (!TryValidateResetContext(context, selector.Scope, out var diagnostic))
                return new ScriptVariableResetResult(false, ScriptVariableErrorCode.ContextUnavailable, 0, diagnostic);

            int count;
            if (TryGetPersistentStore(context, selector.Scope, out var persistentStore))
            {
                EnsureDailyPeriod(context, selector.Scope, persistentStore);
                count = persistentStore.Clear(selector.Scope);
                if (count > 0) MarkPersistentOwnerDirty(context, selector.Scope);
            }
            else if (IsServerPersistentScope(selector.Scope))
            {
                count = _serverPersistent.Clear(selector.Scope);
                if (count > 0) _requestServerAutoSave?.Invoke();
            }
            else
            {
                Dictionary<string, ScriptVariableValue> values = GetScopeValues(context, selector.Scope);
                count = values.Count;
                values.Clear();
            }
            return new ScriptVariableResetResult(true, ScriptVariableErrorCode.None, count, string.Empty);
        }

        private Dictionary<string, ScriptVariableValue> GetScopeValues(
            in ScriptVariableContext context,
            ScriptVariableScope scope)
        {
            if (scope == ScriptVariableScope.I) return _serverRuntime;
            if (scope == ScriptVariableScope.Call)
                return _callFrames.GetOrCreateValue(context.CallFrame).GetScope(scope);

            OwnerState state = _owners.GetOrCreateValue(context.Owner);
            if (scope == ScriptVariableScope.P && state.NpcObjectId != context.NpcObjectId)
            {
                state.GetScope(ScriptVariableScope.P).Clear();
                state.NpcObjectId = context.NpcObjectId;
            }
            if (scope == ScriptVariableScope.M && !ReferenceEquals(state.MapInstanceKey, context.MapInstanceKey))
            {
                state.GetScope(ScriptVariableScope.M).Clear();
                state.MapInstanceKey = context.MapInstanceKey;
            }
            return state.GetScope(scope);
        }

        private bool TryValidateCompositeMutation(
            in ScriptVariableContext context,
            ScriptVariableReference reference,
            ScriptVariableValue value,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            int count = value.Kind == ScriptVariableKind.List ? value.List.Count : value.Dictionary.Count;
            if (count > MaximumCompositeItems)
            {
                diagnostic = $"单个复合变量最多允许 {MaximumCompositeItems} 项。";
                return false;
            }

            IEnumerable<string> parts = value.Kind == ScriptVariableKind.List
                ? value.List
                : value.Dictionary.SelectMany(pair => new[] { pair.Key, pair.Value });
            if (parts.Any(part => Encoding.UTF8.GetByteCount(part ?? string.Empty) > MaximumCompositeItemUtf8Bytes))
            {
                diagnostic = $"复合变量单项最多允许 {MaximumCompositeItemUtf8Bytes} 个 UTF-8 字节。";
                return false;
            }

            OwnerState state = _owners.GetOrCreateValue(context.Owner);
            int total = state.Scopes
                .Where(scope => scope.Key == ScriptVariableScope.L || scope.Key == ScriptVariableScope.Dict)
                .Sum(scope => scope.Value
                    .Where(entry => scope.Key != reference.Scope ||
                                    !string.Equals(entry.Key, reference.StorageKey, StringComparison.Ordinal))
                    .Sum(entry => GetCompositeUtf8Bytes(entry.Value)));
            total += GetCompositeUtf8Bytes(value);
            if (total <= MaximumOwnerCompositeUtf8Bytes) return true;

            diagnostic = $"单个角色的临时复合变量总量最多允许 {MaximumOwnerCompositeUtf8Bytes} 个 UTF-8 字节。";
            return false;
        }

        private static int GetCompositeUtf8Bytes(ScriptVariableValue value)
        {
            return value.Kind == ScriptVariableKind.List || value.Kind == ScriptVariableKind.Dictionary
                ? Encoding.UTF8.GetByteCount(value.Format())
                : 0;
        }

        private static bool TryValidateContext(
            in ScriptVariableContext context,
            ScriptVariableScope scope,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            switch (scope)
            {
                case ScriptVariableScope.P:
                    if (context.Owner != null && context.NpcObjectId != 0) return true;
                    diagnostic = "P 变量需要有效人物和 NPC 对话上下文。";
                    return false;
                case ScriptVariableScope.D:
                case ScriptVariableScope.N:
                case ScriptVariableScope.S:
                case ScriptVariableScope.L:
                case ScriptVariableScope.Dict:
                    if (context.Owner != null) return true;
                    diagnostic = $"{scope} 变量需要有效人物在线上下文。";
                    return false;
                case ScriptVariableScope.U:
                case ScriptVariableScope.T:
                case ScriptVariableScope.J:
                case ScriptVariableScope.Z:
                case ScriptVariableScope.Human:
                    if (ResolveCharacter(context.Owner) != null) return true;
                    diagnostic = $"{scope} 变量需要有效角色持久化上下文。";
                    return false;
                case ScriptVariableScope.M:
                    if (context.Owner != null && context.MapInstanceKey != null) return true;
                    diagnostic = "M 变量需要有效人物和地图实例上下文。";
                    return false;
                case ScriptVariableScope.I:
                case ScriptVariableScope.G:
                case ScriptVariableScope.A:
                case ScriptVariableScope.Global:
                    return true;
                case ScriptVariableScope.Guild:
                    if (ResolveGuild(context.Owner) != null) return true;
                    diagnostic = "GUILD 变量需要有效行会成员或行会上下文。";
                    return false;
                case ScriptVariableScope.Call:
                    if (context.CallFrame != null) return true;
                    diagnostic = "Call 变量需要有效脚本调用帧。";
                    return false;
                default:
                    diagnostic = $"作用域尚未实现：{scope}。";
                    return false;
            }
        }

        private static bool TryValidateResetContext(
            in ScriptVariableContext context,
            ScriptVariableScope scope,
            out string diagnostic)
        {
            if (scope == ScriptVariableScope.I || IsServerPersistentScope(scope))
            {
                diagnostic = string.Empty;
                return true;
            }
            if (scope == ScriptVariableScope.Call)
            {
                diagnostic = context.CallFrame == null ? "Call 变量需要有效脚本调用帧。" : string.Empty;
                return context.CallFrame != null;
            }
            if (scope == ScriptVariableScope.U || scope == ScriptVariableScope.T ||
                scope == ScriptVariableScope.J || scope == ScriptVariableScope.Z ||
                scope == ScriptVariableScope.Human)
            {
                diagnostic = ResolveCharacter(context.Owner) == null
                    ? $"{scope} 变量需要有效角色持久化上下文。"
                    : string.Empty;
                return ResolveCharacter(context.Owner) != null;
            }
            if (scope == ScriptVariableScope.Guild)
            {
                diagnostic = ResolveGuild(context.Owner) == null
                    ? "GUILD 变量需要有效行会成员或行会上下文。"
                    : string.Empty;
                return ResolveGuild(context.Owner) != null;
            }
            diagnostic = context.Owner == null ? "缺少变量所有者。" : string.Empty;
            return context.Owner != null &&
                   (scope == ScriptVariableScope.P || scope == ScriptVariableScope.D ||
                    scope == ScriptVariableScope.M || scope == ScriptVariableScope.N ||
                    scope == ScriptVariableScope.S || scope == ScriptVariableScope.L ||
                    scope == ScriptVariableScope.Dict);
        }

        private bool TryResolveContract(
            ScriptVariableReference reference,
            out ScriptVariableKind kind,
            out ScriptVariableValue defaultValue,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (reference.IsLegacy)
            {
                if ((reference.Scope == ScriptVariableScope.U || reference.Scope == ScriptVariableScope.T ||
                     reference.Scope == ScriptVariableScope.J || reference.Scope == ScriptVariableScope.Z) &&
                    reference.LegacyIndex > 499)
                {
                    kind = default;
                    defaultValue = default;
                    diagnostic = $"{reference.Scope} 固定编号范围为 0-499。";
                    return false;
                }
                if (reference.Scope == ScriptVariableScope.Human ||
                    reference.Scope == ScriptVariableScope.Guild ||
                    reference.Scope == ScriptVariableScope.Global)
                {
                    kind = default;
                    defaultValue = default;
                    diagnostic = $"{reference.Scope} 仅支持显式命名变量。";
                    return false;
                }
                kind = IsLegacyStringScope(reference.Scope)
                    ? ScriptVariableKind.String
                    : ScriptVariableKind.Integer;
                defaultValue = kind == ScriptVariableKind.String
                    ? ScriptVariableValue.FromString(string.Empty)
                    : ScriptVariableValue.FromInteger(0);
                return true;
            }

            ScriptVariableDeclarationSnapshot declarations = _declarations() ?? ScriptVariableDeclarationSnapshot.Empty;
            if (declarations.TryGet(reference.Scope, reference.Key, out var declaration))
            {
                kind = declaration.Kind;
                defaultValue = declaration.DefaultValue;
                return true;
            }

            if (reference.Scope == ScriptVariableScope.N)
            {
                kind = ScriptVariableKind.Integer;
                defaultValue = ScriptVariableValue.FromInteger(0);
                return true;
            }
            if (reference.Scope == ScriptVariableScope.S)
            {
                kind = ScriptVariableKind.String;
                defaultValue = ScriptVariableValue.FromString(string.Empty);
                return true;
            }
            if (reference.Scope == ScriptVariableScope.L || reference.Scope == ScriptVariableScope.Dict)
            {
                kind = reference.Scope == ScriptVariableScope.L
                    ? ScriptVariableKind.List
                    : ScriptVariableKind.Dictionary;
                defaultValue = kind == ScriptVariableKind.List
                    ? ScriptVariableValue.FromList(Array.Empty<string>())
                    : ScriptVariableValue.FromDictionary(Array.Empty<KeyValuePair<string, string>>());
                return true;
            }

            kind = default;
            defaultValue = default;
            diagnostic = $"变量尚未声明：{reference.Scope}.{reference.Key}";
            return false;
        }

        private static ScriptVariableResult CoerceForTarget(ScriptVariableKind targetKind, ScriptVariableValue value)
        {
            if (targetKind == value.Kind) return ScriptVariableResult.Ok(value);
            if (targetKind == ScriptVariableKind.Decimal && value.Kind == ScriptVariableKind.Integer)
                return ScriptVariableResult.Ok(ScriptVariableValue.FromDecimal(value.Integer));

            return ScriptVariableResult.Fail(
                ScriptVariableErrorCode.TypeMismatch,
                $"不能把 {value.Kind} 隐式写入 {targetKind} 变量。");
        }

        private static bool IsLegacyStringScope(ScriptVariableScope scope) =>
            scope == ScriptVariableScope.S ||
            scope == ScriptVariableScope.A ||
            scope == ScriptVariableScope.T ||
            scope == ScriptVariableScope.Z;

        private static bool IsServerPersistentScope(ScriptVariableScope scope) =>
            scope == ScriptVariableScope.G || scope == ScriptVariableScope.A ||
            scope == ScriptVariableScope.Global;

        private static bool TryGetPersistentStore(
            in ScriptVariableContext context,
            ScriptVariableScope scope,
            out CharacterScriptVariableStore store)
        {
            store = null;
            if (scope == ScriptVariableScope.Guild)
            {
                GuildInfo guild = ResolveGuild(context.Owner);
                if (guild == null) return false;
                store = guild.ScriptVariables;
                return true;
            }
            if (scope != ScriptVariableScope.U && scope != ScriptVariableScope.T &&
                scope != ScriptVariableScope.J && scope != ScriptVariableScope.Z &&
                scope != ScriptVariableScope.Human) return false;
            CharacterInfo character = ResolveCharacter(context.Owner);
            if (character == null) return false;
            store = character.ScriptVariables;
            return true;
        }

        private static CharacterInfo ResolveCharacter(object owner) => owner switch
        {
            CharacterInfo character => character,
            HumanObject human => human.Info,
            _ => null
        };

        private static GuildInfo ResolveGuild(object owner) => owner switch
        {
            GuildInfo guild => guild,
            GuildObject guildObject => guildObject.Info,
            HumanObject human => human.MyGuild?.Info,
            _ => null
        };

        private void EnsureDailyPeriod(
            in ScriptVariableContext context,
            ScriptVariableScope scope,
            CharacterScriptVariableStore store)
        {
            if (scope != ScriptVariableScope.J && scope != ScriptVariableScope.Z) return;
            if (store.EnsureDailyPeriod(_currentDailyPeriod()))
                MarkPersistentOwnerDirty(context, scope);
        }

        private void MarkPersistentOwnerDirty(in ScriptVariableContext context, ScriptVariableScope scope)
        {
            if (scope == ScriptVariableScope.Guild)
            {
                GuildInfo guild = ResolveGuild(context.Owner);
                if (guild != null) guild.NeedSave = true;
                return;
            }
            _requestAutoSave?.Invoke();
        }

        private static ScriptVariableReadResult ReadFailure(ScriptVariableErrorCode code, string diagnostic) =>
            new ScriptVariableReadResult(false, false, code, default, diagnostic);

        private static ScriptVariableMutationResult MutationFailure(
            ScriptVariableErrorCode code,
            ScriptVariableValue oldValue,
            string diagnostic) =>
            new ScriptVariableMutationResult(false, code, oldValue, oldValue, diagnostic);
    }
}
