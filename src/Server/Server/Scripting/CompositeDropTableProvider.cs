using Server.MirDatabase;

namespace Server.Scripting
{
    internal sealed class CompositeDropTableProvider : IDropTableProvider
    {
        private readonly IDropTableProvider _csharp;
        private readonly IDropTableProvider _txt;
        private readonly bool _csharpRuntimeActive;
        private readonly bool _fallbackToTxt;
        private readonly TextFileSourcePriority _priority;

        public CompositeDropTableProvider(
            IDropTableProvider csharp,
            IDropTableProvider txt,
            bool csharpRuntimeActive,
            bool fallbackToTxt,
            TextFileSourcePriority priority)
        {
            _csharp = csharp;
            _txt = txt;
            _csharpRuntimeActive = csharpRuntimeActive;
            _fallbackToTxt = fallbackToTxt;
            _priority = priority;
        }

        public IReadOnlyList<DropInfo> Get(string key)
        {
            if (!_csharpRuntimeActive) return _txt?.Get(key);
            if (_priority == TextFileSourcePriority.TxtFirst)
                return _txt?.Get(key) ?? _csharp?.Get(key);
            return _csharp?.Get(key) ?? (_fallbackToTxt ? _txt?.Get(key) : null);
        }
    }
}
