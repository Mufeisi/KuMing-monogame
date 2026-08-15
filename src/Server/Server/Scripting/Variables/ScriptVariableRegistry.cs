namespace Server.Scripting.Variables
{
    /// <summary>
    /// 单次脚本编译周期内的变量声明收集器。它随 ScriptRegistry 一起发布，
    /// 因而脚本处理器和变量声明只会以同一版本对外可见。
    /// </summary>
    public sealed class ScriptVariableRegistry
    {
        private readonly Dictionary<string, ScriptVariableDeclaration> _declarations =
            new Dictionary<string, ScriptVariableDeclaration>(StringComparer.Ordinal);
        private ScriptVariableDeclarationSnapshot _snapshot;
        private bool _sealed;

        public int Count => _declarations.Count;

        public void Register(ScriptVariableDeclaration declaration)
        {
            if (_sealed)
                throw new InvalidOperationException("变量声明注册表已经发布，不能在热重载安全点之外修改。");
            if (declaration == null) throw new ArgumentNullException(nameof(declaration));

            string compositeKey = ScriptVariableDeclarationSnapshot.CompositeKey(
                declaration.Scope, declaration.Key);
            if (_declarations.TryGetValue(compositeKey, out var existing))
            {
                if (existing.Equals(declaration)) return;

                throw new InvalidOperationException(
                    $"变量声明冲突：{declaration.Scope}.{declaration.DisplayKey} " +
                    $"({existing.Kind} / {declaration.Kind})，位置 " +
                    $"{declaration.SourceFile}:{declaration.SourceLine}");
            }

            _declarations.Add(compositeKey, declaration);
        }

        internal ScriptVariableDeclarationSnapshot CreateSnapshot() =>
            _snapshot ?? new ScriptVariableDeclarationSnapshot(_declarations);

        internal void Seal()
        {
            if (_sealed) return;
            _snapshot = new ScriptVariableDeclarationSnapshot(_declarations);
            _sealed = true;
        }
    }
}
