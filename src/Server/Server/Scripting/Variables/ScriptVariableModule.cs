using System.Runtime.CompilerServices;

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
        private ScriptVariableContext(object owner, uint npcObjectId)
        {
            Owner = owner;
            NpcObjectId = npcObjectId;
        }

        public object Owner { get; }
        public uint NpcObjectId { get; }

        public static ScriptVariableContext ForConversation(object owner, uint npcObjectId) =>
            new ScriptVariableContext(owner ?? throw new ArgumentNullException(nameof(owner)), npcObjectId);
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
        private sealed class OwnerState
        {
            public uint NpcObjectId;
            public readonly Dictionary<string, ScriptVariableValue> Conversation =
                new Dictionary<string, ScriptVariableValue>(StringComparer.Ordinal);
        }

        private readonly Func<ScriptVariableDeclarationSnapshot> _declarations;
        private readonly ConditionalWeakTable<object, OwnerState> _owners = new ConditionalWeakTable<object, OwnerState>();
        private readonly Func<bool> _canWrite;

        public ScriptVariableModule(ScriptVariableDeclarationCatalog catalog, Func<bool> canWrite = null)
            : this(() => (catalog ?? throw new ArgumentNullException(nameof(catalog))).Current, canWrite)
        {
        }

        public ScriptVariableModule(
            Func<ScriptVariableDeclarationSnapshot> declarations,
            Func<bool> canWrite = null)
        {
            _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
            int creatorThread = Environment.CurrentManagedThreadId;
            _canWrite = canWrite ?? (() => Environment.CurrentManagedThreadId == creatorThread);
        }

        public ScriptVariableReadResult Read(in ScriptVariableContext context, ScriptVariableReference reference)
        {
            if (reference.Scope != ScriptVariableScope.P)
                return ReadFailure(ScriptVariableErrorCode.UnknownReference, "VAR-01 只实现 P 对话作用域。");
            if (context.Owner == null || context.NpcObjectId == 0)
                return ReadFailure(ScriptVariableErrorCode.ContextUnavailable, "P 变量需要有效人物和 NPC 对话上下文。");
            if (!_canWrite())
                return ReadFailure(ScriptVariableErrorCode.WrongThread, "变量状态只能在服务端主线程访问。");

            if (!TryResolveContract(reference, out var kind, out var defaultValue, out var diagnostic))
                return ReadFailure(ScriptVariableErrorCode.UnknownReference, diagnostic);

            OwnerState state = GetConversationState(context);
            if (state.Conversation.TryGetValue(reference.StorageKey, out var value))
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

            OwnerState state = GetConversationState(context);
            state.Conversation[mutation.Reference.StorageKey] = coerced.Value;
            return new ScriptVariableMutationResult(
                true, ScriptVariableErrorCode.None, current.Value, coerced.Value, string.Empty);
        }

        public ScriptVariableResetResult Reset(in ScriptVariableContext context, ScriptVariableSelector selector)
        {
            if (selector.Scope != ScriptVariableScope.P)
                return new ScriptVariableResetResult(false, ScriptVariableErrorCode.UnknownReference, 0, "VAR-01 只实现 P 对话作用域。");
            if (context.Owner == null)
                return new ScriptVariableResetResult(false, ScriptVariableErrorCode.ContextUnavailable, 0, "缺少变量所有者。");
            if (!_canWrite())
                return new ScriptVariableResetResult(false, ScriptVariableErrorCode.WrongThread, 0, "变量状态只能在服务端主线程修改。");

            OwnerState state = _owners.GetOrCreateValue(context.Owner);
            int count = state.Conversation.Count;
            state.Conversation.Clear();
            state.NpcObjectId = context.NpcObjectId;
            return new ScriptVariableResetResult(true, ScriptVariableErrorCode.None, count, string.Empty);
        }

        private OwnerState GetConversationState(in ScriptVariableContext context)
        {
            OwnerState state = _owners.GetOrCreateValue(context.Owner);
            if (state.NpcObjectId != context.NpcObjectId)
            {
                state.Conversation.Clear();
                state.NpcObjectId = context.NpcObjectId;
            }
            return state;
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
                kind = IsLegacyStringScope(reference.Scope)
                    ? ScriptVariableKind.String
                    : ScriptVariableKind.Integer;
                defaultValue = kind == ScriptVariableKind.String
                    ? ScriptVariableValue.FromString(string.Empty)
                    : ScriptVariableValue.FromInteger(0);
                return true;
            }

            ScriptVariableDeclarationSnapshot declarations =
                _declarations() ?? ScriptVariableDeclarationSnapshot.Empty;
            if (declarations.TryGet(reference.Scope, reference.Key, out var declaration))
            {
                kind = declaration.Kind;
                defaultValue = declaration.DefaultValue;
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

        private static ScriptVariableReadResult ReadFailure(ScriptVariableErrorCode code, string diagnostic) =>
            new ScriptVariableReadResult(false, false, code, default, diagnostic);

        private static ScriptVariableMutationResult MutationFailure(
            ScriptVariableErrorCode code,
            ScriptVariableValue oldValue,
            string diagnostic) =>
            new ScriptVariableMutationResult(false, code, oldValue, oldValue, diagnostic);
    }
}
