using System.Collections.ObjectModel;
using System.Globalization;

namespace Server.Scripting.Variables
{
    public sealed class ScriptVariableDeclaration : IEquatable<ScriptVariableDeclaration>
    {
        public ScriptVariableDeclaration(
            ScriptVariableScope scope,
            string key,
            ScriptVariableKind kind,
            string defaultValue,
            string sourceFile = "",
            int sourceLine = 0)
        {
            if (!ScriptVariableName.TryNormalize(key, out var normalizedKey))
                throw new ArgumentException("变量名称无效。", nameof(key));
            if (scope == ScriptVariableScope.U && kind == ScriptVariableKind.String)
                throw new ArgumentException("U 作用域只允许 Integer 或 Decimal；私人持久字符串请使用 T。", nameof(kind));
            if (scope == ScriptVariableScope.T && kind != ScriptVariableKind.String)
                throw new ArgumentException("T 作用域只允许 String；私人持久数值请使用 U。", nameof(kind));
            if (scope == ScriptVariableScope.G && kind == ScriptVariableKind.String)
                throw new ArgumentException("G 作用域只允许 Integer 或 Decimal；全局持久字符串请使用 A。", nameof(kind));
            if (scope == ScriptVariableScope.A && kind != ScriptVariableKind.String)
                throw new ArgumentException("A 作用域只允许 String；全局持久数值请使用 G。", nameof(kind));
            if (scope == ScriptVariableScope.J && kind == ScriptVariableKind.String)
                throw new ArgumentException("J 作用域只允许 Integer 或 Decimal；每日字符串请使用 Z。", nameof(kind));
            if (scope == ScriptVariableScope.Z && kind != ScriptVariableKind.String)
                throw new ArgumentException("Z 作用域只允许 String；每日数值请使用 J。", nameof(kind));
            if ((scope == ScriptVariableScope.Human || scope == ScriptVariableScope.Guild ||
                 scope == ScriptVariableScope.Global) && kind == ScriptVariableKind.String)
                throw new ArgumentException($"{scope} 自定义持久作用域只允许 Integer 或 Decimal。", nameof(kind));

            Scope = scope;
            Key = normalizedKey;
            DisplayKey = key.Trim();
            Kind = kind;
            DefaultValue = ParseDefault(kind, defaultValue ?? string.Empty);
            SourceFile = sourceFile ?? string.Empty;
            SourceLine = Math.Max(0, sourceLine);
        }

        public ScriptVariableScope Scope { get; }
        public string Key { get; }
        public string DisplayKey { get; }
        public ScriptVariableKind Kind { get; }
        public ScriptVariableValue DefaultValue { get; }
        public string SourceFile { get; }
        public int SourceLine { get; }

        public bool Equals(ScriptVariableDeclaration other) =>
            other != null &&
            Scope == other.Scope &&
            string.Equals(Key, other.Key, StringComparison.Ordinal) &&
            Kind == other.Kind &&
            DefaultValue.Equals(other.DefaultValue);

        public override bool Equals(object obj) => Equals(obj as ScriptVariableDeclaration);
        public override int GetHashCode() => HashCode.Combine(Scope, Key, Kind, DefaultValue);

        private static ScriptVariableValue ParseDefault(ScriptVariableKind kind, string text)
        {
            switch (kind)
            {
                case ScriptVariableKind.Integer:
                    if (long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                        return ScriptVariableValue.FromInteger(integer);
                    throw new ArgumentException("整数默认值无效。", nameof(text));
                case ScriptVariableKind.Decimal:
                    if (ScriptVariableValue.TryParseDecimal(text, out var decimalValue))
                        return decimalValue;
                    throw new ArgumentException("小数默认值无效或超过允许小数位。", nameof(text));
                case ScriptVariableKind.String:
                    return ScriptVariableValue.FromString(text);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), "首期声明不支持复合持久类型。");
            }
        }
    }

    public sealed class ScriptVariableDeclarationSnapshot
    {
        private readonly IReadOnlyDictionary<string, ScriptVariableDeclaration> _declarations;

        internal ScriptVariableDeclarationSnapshot(IDictionary<string, ScriptVariableDeclaration> declarations)
        {
            _declarations = new ReadOnlyDictionary<string, ScriptVariableDeclaration>(
                new Dictionary<string, ScriptVariableDeclaration>(declarations, StringComparer.Ordinal));
        }

        public static ScriptVariableDeclarationSnapshot Empty { get; } =
            new ScriptVariableDeclarationSnapshot(new Dictionary<string, ScriptVariableDeclaration>());

        public int Count => _declarations.Count;
        internal IEnumerable<ScriptVariableDeclaration> Declarations => _declarations.Values;

        internal static ScriptVariableDeclarationSnapshot Merge(
            params ScriptVariableDeclarationSnapshot[] snapshots)
        {
            var registry = new ScriptVariableRegistry();
            foreach (ScriptVariableDeclarationSnapshot snapshot in snapshots)
            {
                if (snapshot == null) continue;
                foreach (ScriptVariableDeclaration declaration in snapshot.Declarations)
                    registry.Register(declaration);
            }
            registry.Seal();
            return registry.CreateSnapshot();
        }

        public ScriptVariableCatalogReloadResult ValidateCompatibleTransitionTo(
            ScriptVariableDeclarationSnapshot next)
        {
            if (next == null)
                return ScriptVariableCatalogReloadResult.Fail(
                    ScriptVariableErrorCode.DeclarationConflict, "候选变量声明快照不能为空。");

            foreach (var pair in _declarations)
            {
                if (!next._declarations.TryGetValue(pair.Key, out var candidate)) continue;
                ScriptVariableDeclaration current = pair.Value;
                if (current.Kind == candidate.Kind) continue;

                return ScriptVariableCatalogReloadResult.Fail(
                    ScriptVariableErrorCode.DeclarationConflict,
                    $"热重载不能修改变量类型：{current.Scope}.{current.DisplayKey} " +
                    $"({current.Kind} -> {candidate.Kind})；请使用显式迁移。");
            }

            return ScriptVariableCatalogReloadResult.Ok();
        }

        public bool TryGet(ScriptVariableScope scope, string key, out ScriptVariableDeclaration declaration)
        {
            declaration = null;
            return ScriptVariableName.TryNormalize(key, out var normalizedKey) &&
                   _declarations.TryGetValue(CompositeKey(scope, normalizedKey), out declaration);
        }

        public ScriptVariableDeclaration GetRequired(ScriptVariableScope scope, string key)
        {
            if (TryGet(scope, key, out var declaration)) return declaration;
            throw new KeyNotFoundException($"变量声明不存在：{scope}.{key}");
        }

        internal static string CompositeKey(ScriptVariableScope scope, string normalizedKey) =>
            scope.ToString().ToUpperInvariant() + ":" + normalizedKey;
    }

    public readonly struct ScriptVariableCatalogReloadResult
    {
        private ScriptVariableCatalogReloadResult(bool success, ScriptVariableErrorCode errorCode, string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public string Diagnostic { get; }

        public static ScriptVariableCatalogReloadResult Ok() =>
            new ScriptVariableCatalogReloadResult(true, ScriptVariableErrorCode.None, string.Empty);

        public static ScriptVariableCatalogReloadResult Fail(ScriptVariableErrorCode code, string diagnostic) =>
            new ScriptVariableCatalogReloadResult(false, code, diagnostic);
    }

    public sealed class ScriptVariableDeclarationCatalog
    {
        private ScriptVariableDeclarationSnapshot _current = ScriptVariableDeclarationSnapshot.Empty;
        private long _version;

        public ScriptVariableDeclarationSnapshot Current => Volatile.Read(ref _current);
        public long Version => Interlocked.Read(ref _version);

        public ScriptVariableCatalogReloadResult TryReload(IEnumerable<ScriptVariableDeclaration> declarations)
        {
            if (declarations == null)
                return ScriptVariableCatalogReloadResult.Fail(
                    ScriptVariableErrorCode.DeclarationConflict, "变量声明集合不能为空。");

            var candidate = new Dictionary<string, ScriptVariableDeclaration>(StringComparer.Ordinal);
            foreach (var declaration in declarations)
            {
                if (declaration == null)
                    return ScriptVariableCatalogReloadResult.Fail(
                        ScriptVariableErrorCode.DeclarationConflict, "变量声明不能为 null。");

                string compositeKey = ScriptVariableDeclarationSnapshot.CompositeKey(declaration.Scope, declaration.Key);
                if (candidate.TryGetValue(compositeKey, out var existing))
                {
                    if (existing.Equals(declaration)) continue;

                    return ScriptVariableCatalogReloadResult.Fail(
                        ScriptVariableErrorCode.DeclarationConflict,
                        $"变量声明冲突：{declaration.Scope}.{declaration.DisplayKey} " +
                        $"({existing.Kind} / {declaration.Kind})，位置 {declaration.SourceFile}:{declaration.SourceLine}");
                }

                candidate.Add(compositeKey, declaration);
            }

            var candidateSnapshot = new ScriptVariableDeclarationSnapshot(candidate);
            ScriptVariableCatalogReloadResult compatibility = Current
                .ValidateCompatibleTransitionTo(candidateSnapshot);
            if (!compatibility.Success) return compatibility;

            Interlocked.Exchange(ref _current, candidateSnapshot);
            Interlocked.Increment(ref _version);
            return ScriptVariableCatalogReloadResult.Ok();
        }
    }

    internal static class ScriptVariableName
    {
        public static bool TryNormalize(string key, out string normalized)
            => TryNormalize(key, false, out normalized);

        public static bool TryNormalizeLingFeng(string key, out string normalized)
            => TryNormalize(key, true, out normalized);

        private static bool TryNormalize(
            string key, bool allowLeadingDigit, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return false;

            string value = key.Trim();
            if (value.Length > 64) return false;
            if (!char.IsLetter(value[0]) &&
                !(allowLeadingDigit && char.IsDigit(value[0])))
                return false;

            for (var i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.')) return false;
                if (c == '.' && (i == value.Length - 1 || value[i - 1] == '.')) return false;
            }

            normalized = value.ToUpperInvariant();
            return true;
        }
    }
}
