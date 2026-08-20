using System;
using FairyGUI;
using Microsoft.Xna.Framework;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private sealed class MobileTextPromptBinding
        {
            public GComponent Root;
            public GGraph Dim;

            public GComponent Panel;
            public GGraph PanelBg;

            public GTextField Title;
            public GTextField Message;

            public GGraph InputBg;
            public GTextInput Input;

            public GComponent OkButton;
            public GGraph OkBg;
            public GTextField OkLabel;

            public GComponent CancelButton;
            public GGraph CancelBg;
            public GTextField CancelLabel;

            public EventCallback0 DimClickCallback;
            public EventCallback0 OkClickCallback;
            public EventCallback0 CancelClickCallback;
            public EventCallback0 SubmitCallback;
            public EventCallback0 LayoutCallback;
        }

        private static MobileTextPromptBinding _mobileTextPrompt;
        private static Action<string> _mobileTextPromptOk;
        private static Action _mobileTextPromptCancel;
        private static int _mobileTextPromptMaxLength;
        private static bool _mobileTextPromptNumericOnly;

        public static bool IsMobileTextPromptVisible
        {
            get
            {
                MobileTextPromptBinding binding = _mobileTextPrompt;
                return binding != null &&
                       binding.Root != null &&
                       !binding.Root._disposed &&
                       binding.Root.visible;
            }
        }

        public static bool TryShowMobileTextPrompt(
            string title,
            string message,
            string initialText,
            int maxLength,
            Action<string> onOk,
            Action onCancel = null,
            bool numericOnly = false)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return false;

            if (onOk == null)
                return false;

            TryCancelMobileTextPrompt(invokeCancel: true);

            EnsureMobileTextPromptUi();

            MobileTextPromptBinding binding = _mobileTextPrompt;
            if (binding == null || binding.Root == null || binding.Root._disposed)
                return false;

            _mobileTextPromptOk = onOk;
            _mobileTextPromptCancel = onCancel;
            _mobileTextPromptMaxLength = maxLength;
            _mobileTextPromptNumericOnly = numericOnly;

            string safeTitle = string.IsNullOrWhiteSpace(title) ? "输入" : title.Trim();
            string safeMessage = message ?? string.Empty;
            string safeText = initialText ?? string.Empty;

            if (maxLength > 0 && safeText.Length > maxLength)
                safeText = safeText.Substring(0, maxLength);

            try
            {
                if (binding.Title != null && !binding.Title._disposed)
                    binding.Title.text = safeTitle;
            }
            catch
            {
            }

            try
            {
                if (binding.Message != null && !binding.Message._disposed)
                    binding.Message.text = safeMessage;
            }
            catch
            {
            }

            try
            {
                if (binding.Input != null && !binding.Input._disposed)
                {
                    binding.Input.maxLength = maxLength > 0 ? maxLength : 0;
                    binding.Input.restrict = numericOnly ? "0-9" : string.Empty;
                    binding.Input.text = safeText;
                    binding.Input.caretPosition = binding.Input.text?.Length ?? 0;
                }
            }
            catch
            {
            }

            try
            {
                if (binding.Root != null && !binding.Root._disposed)
                {
                    binding.Root.visible = true;
                    BringToFront(binding.Root);
                }
            }
            catch
            {
            }

            LayoutMobileTextPrompt();

            try
            {
                if (binding.Input != null && !binding.Input._disposed)
                    _stage.focus = binding.Input.inputTextField;
            }
            catch
            {
            }

            return true;
        }

        private static void EnsureMobileTextPromptUi()
        {
            MobileTextPromptBinding existing = _mobileTextPrompt;
            if (existing != null && existing.Root != null && !existing.Root._disposed)
            {
                if (existing.Root.parent == null || existing.Root.parent._disposed)
                {
                    try
                    {
                        GComponent layer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                            ? _mobileOverlaySafeAreaRoot
                            : (_uiManager?.OverlayLayer ?? GRoot.inst);

                        layer.AddChild(existing.Root);
                        existing.Root.AddRelation(layer, RelationType.Size);
                    }
                    catch
                    {
                    }
                }

                return;
            }

            GComponent overlayLayer = _mobileOverlaySafeAreaRoot != null && !_mobileOverlaySafeAreaRoot._disposed
                ? _mobileOverlaySafeAreaRoot
                : (_uiManager?.OverlayLayer ?? GRoot.inst);

            var binding = new MobileTextPromptBinding();

            var root = new GComponent
            {
                name = "MobileTextPromptRoot",
                touchable = true,
                opaque = true,
                visible = false,
            };

            overlayLayer.AddChild(root);
            root.AddRelation(overlayLayer, RelationType.Size);

            var dim = new GGraph
            {
                name = "Dim",
                touchable = true,
            };
            root.AddChild(dim);

            var panel = new GComponent
            {
                name = "Panel",
                touchable = true,
                opaque = false,
            };
            root.AddChild(panel);

            var panelBg = new GGraph
            {
                name = "PanelBg",
                touchable = false,
            };
            panel.AddChild(panelBg);

            var title = new GTextField
            {
                name = "Title",
                touchable = false,
            };
            try
            {
                title.autoSize = AutoSizeType.None;
                title.align = AlignType.Center;
                TextFormat tf = title.textFormat;
                tf.size = 24;
                tf.color = Color.White;
                title.textFormat = tf;
            }
            catch
            {
            }
            panel.AddChild(title);

            var message = new GTextField
            {
                name = "Message",
                touchable = false,
            };
            try
            {
                message.autoSize = AutoSizeType.Height;
                message.align = AlignType.Left;
                TextFormat tf = message.textFormat;
                tf.size = 18;
                tf.color = Color.White;
                message.textFormat = tf;
            }
            catch
            {
            }
            panel.AddChild(message);

            var inputBg = new GGraph
            {
                name = "InputBg",
                touchable = false,
            };
            panel.AddChild(inputBg);

            var input = new GTextInput
            {
                name = "Input",
                touchable = true,
            };
            try
            {
                input.autoSize = AutoSizeType.None;
                TextFormat tf = input.textFormat;
                tf.size = 20;
                tf.color = Color.White;
                input.textFormat = tf;
            }
            catch
            {
            }
            panel.AddChild(input);

            static GComponent CreateButton(string name, out GGraph bg, out GTextField label)
            {
                var button = new GComponent
                {
                    name = name,
                    touchable = true,
                    opaque = true,
                };

                bg = new GGraph
                {
                    name = name + "Bg",
                    touchable = false,
                };
                button.AddChild(bg);

                label = new GTextField
                {
                    name = name + "Label",
                    touchable = false,
                };

                try
                {
                    label.autoSize = AutoSizeType.None;
                    label.align = AlignType.Center;
                    TextFormat tf = label.textFormat;
                    tf.size = 20;
                    tf.color = Color.White;
                    label.textFormat = tf;
                }
                catch
                {
                }

                button.AddChild(label);
                return button;
            }

            GComponent okButton = CreateButton("OkButton", out GGraph okBg, out GTextField okLabel);
            okLabel.text = "确定";
            panel.AddChild(okButton);

            GComponent cancelButton = CreateButton("CancelButton", out GGraph cancelBg, out GTextField cancelLabel);
            cancelLabel.text = "取消";
            panel.AddChild(cancelButton);

            binding.Root = root;
            binding.Dim = dim;
            binding.Panel = panel;
            binding.PanelBg = panelBg;
            binding.Title = title;
            binding.Message = message;
            binding.InputBg = inputBg;
            binding.Input = input;
            binding.OkButton = okButton;
            binding.OkBg = okBg;
            binding.OkLabel = okLabel;
            binding.CancelButton = cancelButton;
            binding.CancelBg = cancelBg;
            binding.CancelLabel = cancelLabel;

            try
            {
                binding.LayoutCallback = LayoutMobileTextPrompt;
                root.onSizeChanged.Add(binding.LayoutCallback);
            }
            catch
            {
            }

            try
            {
                binding.DimClickCallback = () => TryCancelMobileTextPrompt(invokeCancel: true);
                dim.onClick.Add(binding.DimClickCallback);
            }
            catch
            {
            }

            try
            {
                binding.OkClickCallback = InvokeMobileTextPromptOk;
                okButton.onClick.Add(binding.OkClickCallback);
            }
            catch
            {
            }

            try
            {
                binding.CancelClickCallback = () => TryCancelMobileTextPrompt(invokeCancel: true);
                cancelButton.onClick.Add(binding.CancelClickCallback);
            }
            catch
            {
            }

            try
            {
                binding.SubmitCallback = InvokeMobileTextPromptOk;
                input.onSubmit.Add(binding.SubmitCallback);
            }
            catch
            {
            }

            _mobileTextPrompt = binding;

            LayoutMobileTextPrompt();
        }

        private static void LayoutMobileTextPrompt()
        {
            MobileTextPromptBinding binding = _mobileTextPrompt;
            if (binding == null || binding.Root == null || binding.Root._disposed)
                return;

            float rootWidth = binding.Root.width;
            float rootHeight = binding.Root.height;
            if (rootWidth <= 1 || rootHeight <= 1)
                return;

            try
            {
                binding.Dim?.DrawRect(rootWidth, rootHeight, 0, Color.Transparent, new Color(0, 0, 0, 160));
            }
            catch
            {
            }

            float panelWidth = Math.Clamp(rootWidth - 60, 320, 720);
            float padding = 24;
            float spacing = 16;
            float titleHeight = 40;
            float inputHeight = 56;
            float buttonHeight = 56;
            float buttonSpacing = 16;

            float contentWidth = Math.Max(1, panelWidth - padding * 2);
            float maxMessageHeight = Math.Max(80, rootHeight * 0.35F);

            try
            {
                if (binding.Title != null && !binding.Title._disposed)
                {
                    binding.Title.autoSize = AutoSizeType.None;
                    binding.Title.maxWidth = (int)contentWidth;
                    binding.Title.SetSize(contentWidth, titleHeight);
                    binding.Title.SetPosition(padding, padding);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.Message != null && !binding.Message._disposed)
                {
                    binding.Message.autoSize = AutoSizeType.Height;
                    binding.Message.maxWidth = (int)contentWidth;
                    binding.Message.maxHeight = (int)maxMessageHeight;
                    binding.Message.SetSize(contentWidth, 0);
                    binding.Message.SetPosition(padding, padding + titleHeight + spacing);
                }
            }
            catch
            {
            }

            float messageHeight = 120;
            try
            {
                if (binding.Message != null && !binding.Message._disposed)
                    messageHeight = Math.Clamp(binding.Message.height, 40, maxMessageHeight);
            }
            catch
            {
                messageHeight = Math.Clamp(messageHeight, 40, maxMessageHeight);
            }

            float inputY = padding + titleHeight + spacing + messageHeight + spacing;

            try
            {
                if (binding.InputBg != null && !binding.InputBg._disposed)
                {
                    binding.InputBg.DrawRect(contentWidth, inputHeight, 2, new Color(180, 180, 180, 255), new Color(20, 20, 20, 255));
                    binding.InputBg.SetPosition(padding, inputY);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.Input != null && !binding.Input._disposed)
                {
                    binding.Input.autoSize = AutoSizeType.None;
                    binding.Input.SetSize(contentWidth - 16, inputHeight - 16);
                    binding.Input.SetPosition(padding + 8, inputY + 8);
                }
            }
            catch
            {
            }

            float buttonWidth = Math.Max(120, (contentWidth - buttonSpacing) / 2);
            float buttonsY = inputY + inputHeight + spacing;

            float panelHeight = buttonsY + buttonHeight + padding;
            panelHeight = Math.Min(panelHeight, rootHeight - 40);

            try
            {
                if (binding.Panel != null && !binding.Panel._disposed)
                {
                    binding.Panel.SetSize(panelWidth, panelHeight);
                    binding.Panel.SetPosition((rootWidth - panelWidth) / 2, (rootHeight - panelHeight) / 2);
                }
            }
            catch
            {
            }

            try
            {
                binding.PanelBg?.DrawRect(panelWidth, panelHeight, 2, new Color(220, 220, 220, 255), new Color(35, 35, 35, 245));
            }
            catch
            {
            }

            try
            {
                if (binding.OkButton != null && !binding.OkButton._disposed)
                {
                    binding.OkButton.SetSize(buttonWidth, buttonHeight);
                    binding.OkButton.SetPosition(padding, buttonsY);
                }

                if (binding.OkBg != null && !binding.OkBg._disposed)
                    binding.OkBg.DrawRoundRect(buttonWidth, buttonHeight, new Color(60, 160, 60, 255), new[] { 10F, 10F, 10F, 10F });

                if (binding.OkLabel != null && !binding.OkLabel._disposed)
                {
                    binding.OkLabel.autoSize = AutoSizeType.None;
                    binding.OkLabel.SetSize(buttonWidth, buttonHeight);
                    binding.OkLabel.SetPosition(0, 0);
                }
            }
            catch
            {
            }

            try
            {
                if (binding.CancelButton != null && !binding.CancelButton._disposed)
                {
                    binding.CancelButton.SetSize(buttonWidth, buttonHeight);
                    binding.CancelButton.SetPosition(padding + buttonWidth + buttonSpacing, buttonsY);
                }

                if (binding.CancelBg != null && !binding.CancelBg._disposed)
                    binding.CancelBg.DrawRoundRect(buttonWidth, buttonHeight, new Color(170, 70, 70, 255), new[] { 10F, 10F, 10F, 10F });

                if (binding.CancelLabel != null && !binding.CancelLabel._disposed)
                {
                    binding.CancelLabel.autoSize = AutoSizeType.None;
                    binding.CancelLabel.SetSize(buttonWidth, buttonHeight);
                    binding.CancelLabel.SetPosition(0, 0);
                }
            }
            catch
            {
            }
        }

        private static void InvokeMobileTextPromptOk()
        {
            MobileTextPromptBinding binding = _mobileTextPrompt;
            if (binding == null || binding.Root == null || binding.Root._disposed || !binding.Root.visible)
                return;

            string text = string.Empty;
            try
            {
                if (binding.Input != null && !binding.Input._disposed)
                    text = binding.Input.text ?? string.Empty;
            }
            catch
            {
                text = string.Empty;
            }

            string cleaned = SanitizeMobileTextPromptResult(text);

            Action<string> ok = _mobileTextPromptOk;

            TryCancelMobileTextPrompt(invokeCancel: false);

            try
            {
                ok?.Invoke(cleaned);
            }
            catch (Exception ex)
            {
                CMain.SaveError("FairyGUI: MobileTextPrompt OK 回调异常：" + ex);
            }
        }

        private static string SanitizeMobileTextPromptResult(string text)
        {
            string result = text?.Trim() ?? string.Empty;

            int maxLength = _mobileTextPromptMaxLength;
            if (maxLength > 0 && result.Length > maxLength)
                result = result.Substring(0, maxLength);

            if (_mobileTextPromptNumericOnly)
            {
                if (result.Length == 0)
                    return string.Empty;

                var buffer = new char[result.Length];
                int count = 0;
                for (int i = 0; i < result.Length; i++)
                {
                    char c = result[i];
                    if (c >= '0' && c <= '9')
                        buffer[count++] = c;
                }

                return count == 0 ? string.Empty : new string(buffer, 0, count);
            }

            return result;
        }

        private static bool TryCancelMobileTextPrompt(bool invokeCancel)
        {
            MobileTextPromptBinding binding = _mobileTextPrompt;
            if (binding == null || binding.Root == null || binding.Root._disposed || !binding.Root.visible)
                return false;

            Action cancel = _mobileTextPromptCancel;

            _mobileTextPromptOk = null;
            _mobileTextPromptCancel = null;
            _mobileTextPromptMaxLength = 0;
            _mobileTextPromptNumericOnly = false;

            try
            {
                binding.Root.visible = false;
            }
            catch
            {
            }

            try
            {
                if (_stage != null && binding.Input != null && !binding.Input._disposed &&
                    ReferenceEquals(_stage.focus, binding.Input.inputTextField))
                {
                    _stage.focus = null;
                }
            }
            catch
            {
            }

            if (invokeCancel)
            {
                try
                {
                    cancel?.Invoke();
                }
                catch (Exception ex)
                {
                    CMain.SaveError("FairyGUI: MobileTextPrompt Cancel 回调异常：" + ex);
                }
            }

            return true;
        }
    }
}
