#nullable enable

using Client.MirControls;
using Shared.CustomGui;

namespace Client.CustomGui;

internal static class PcCustomGuiRenderSmoke
{
    private static CustomGuiRuntimeDocument? _document;
    private static string? _outputPath;
    internal static Exception? Failure { get; private set; }

    internal static bool IsConfigured => _document is not null && !string.IsNullOrWhiteSpace(_outputPath);

    internal static void Configure(CustomGuiRuntimeDocument document, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _document = document;
        _outputPath = Path.GetFullPath(outputPath);
        Failure = null;
    }

    internal static void AttachScene()
    {
        if (!IsConfigured) throw new InvalidOperationException("PC GUI 截图冒烟尚未配置");
        MirScene.ActiveScene?.Dispose();
        MirScene.ActiveScene = new StaticScene(_document!, _outputPath!);
    }

    internal static void Reset()
    {
        _document = null;
        _outputPath = null;
        Failure = null;
    }

    private sealed class StaticScene : MirScene
    {
        private readonly PcCustomGuiHost _host;
        private readonly string _outputPath;
        private int _frames;
        private bool _captured;

        internal StaticScene(CustomGuiRuntimeDocument document, string outputPath)
        {
            BackColour = Color.FromArgb(12, 18, 26);
            _outputPath = outputPath;
            _host = PcCustomGuiAdapter.Create(document, Size);
            _host.AttachTo(this);
        }

        public override void Process()
        {
            if (_captured || ++_frames < 12) return;
            _captured = true;
            try
            {
                if (!Program.Form.SaveBackBuffer(_outputPath)) throw new InvalidOperationException("PC GUI 截图读取回退缓冲失败");
            }
            catch (Exception error)
            {
                Failure = error;
            }
            finally
            {
                Program.Form.BeginInvoke(new Action(Program.Form.Close));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _host.Dispose();
            base.Dispose(disposing);
        }
    }
}
