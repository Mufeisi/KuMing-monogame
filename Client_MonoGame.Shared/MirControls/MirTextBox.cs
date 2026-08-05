using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoShare.Share.Extensions;
using System;

namespace MonoShare.MirControls
{
    public sealed class MirTextBox : MirControl
    {
        private static Texture2D _pixel;

        public static MirTextBox ActiveTextBox { get; private set; }

        public bool Password { get; set; }
        public bool NumericOnly { get; set; }
        public int MaxLength { get; set; } = 50;
        public string SoftKeyboardTitle { get; set; } = "输入";
        public string SoftKeyboardDescription { get; set; } = string.Empty;

        private string _text = string.Empty;
        private int _caretIndex;
        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;

                if (NumericOnly && value.Length > 0)
                    value = FilterDigits(value);

                if (value.Length > MaxLength)
                    value = value.Substring(0, MaxLength);

                if (_text == value)
                    return;

                _text = value;
                _caretIndex = Math.Clamp(_caretIndex, 0, _text.Length);
                TextChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler TextChanged;
        public event EventHandler EnterPressed;

        internal void NotifySoftKeyboardSubmitted()
        {
            EnterPressed?.Invoke(this, EventArgs.Empty);
        }

        private long _nextCursorBlinkTime;
        private bool _cursorVisible = true;
        private KeyboardState _previousKeyboardState;

        public MirTextBox()
        {
            Size = new System.Drawing.Size(120, 22);
            Location = System.Drawing.Point.Empty;
            Border = true;
            BorderColour = System.Drawing.Color.FromArgb(160, 255, 255, 255);
            BackColour = System.Drawing.Color.FromArgb(140, 0, 0, 0);
        }

        public void MoveCaretToEnd()
        {
            _caretIndex = _text.Length;
            _cursorVisible = true;
            _nextCursorBlinkTime = 0;
        }

        public void SetFocus()
        {
            if (ActiveTextBox != null && ActiveTextBox != this)
            {
                MirTextBox previous = ActiveTextBox;
                ActiveTextBox = null;
                previous._cursorVisible = false;
                CMain.RequestSoftKeyboard(false);
            }

            ActiveTextBox = this;
            CMain.RequestSoftKeyboard(true);
            _previousKeyboardState = Keyboard.GetState();
            _cursorVisible = true;
            _nextCursorBlinkTime = 0;
        }

        public void ClearFocus()
        {
            if (ActiveTextBox != this)
                return;

            ActiveTextBox = null;
            CMain.RequestSoftKeyboard(false);
            _cursorVisible = false;
        }

        public override void Event()
        {
            if (IsDisposed || !Visible || !Enabled)
                return;

            bool clickDown = CMain.currentMouseState.LeftButton == ButtonState.Pressed &&
                             CMain.previousMouseState.LeftButton == ButtonState.Released;

            if (clickDown)
            {
                System.Drawing.Point mouse = CMain.currentMouseState.Position.ToDrawPoint();
                if (DisplayRectangle.Contains(mouse))
                {
                    SetFocus();
                    TryUpdateCaretFromMouse(mouse);
                }
                else if (ActiveTextBox == this)
                {
                    if (MouseControl == null || MouseControl == this)
                    {
                        ClearFocus();
                    }
                }
            }

            if (ActiveTextBox == this)
            {
                UpdateCursorBlink();
                ProcessNavigationKeys();
                ProcessTextInput();
            }
            else
            {
                _cursorVisible = false;
            }
        }

        protected internal override void DrawControl()
        {
            Texture2D pixel = GetPixel();

            System.Drawing.Rectangle bounds = DisplayRectangle;
            var xnaBounds = new Microsoft.Xna.Framework.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);

            bool focused = ActiveTextBox == this;
            Microsoft.Xna.Framework.Color back = focused ? new Microsoft.Xna.Framework.Color(20, 90, 180, 160) : BackColour.ToXnaColor();
            Microsoft.Xna.Framework.Color border = BorderColour.ToXnaColor();

            CMain.SpriteBatchScope.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            CMain.spriteBatch.Draw(pixel, xnaBounds, back);
            DrawBorder(pixel, xnaBounds, border, thickness: 2);

            SpriteFontBase font = CMain.fontSystem?.GetFont(Math.Max(14, bounds.Height - 8));
            if (font != null)
            {
                string display = Password ? new string('*', Text.Length) : Text;
                float caretX = GetCaretX(font, display);

                float availableWidth = Math.Max(1F, bounds.Width - 12F);
                float fullWidth = font.MeasureString(display).X;
                float scrollX = 0F;
                if (fullWidth > availableWidth)
                {
                    float maxScroll = Math.Max(0F, fullWidth - availableWidth);
                    scrollX = Math.Clamp(caretX - availableWidth + 8F, 0F, maxScroll);
                }

                Vector2 pos = new Vector2(bounds.X + 6 - scrollX, bounds.Y + 4);
                CMain.spriteBatch.DrawString(font, display, pos, Microsoft.Xna.Framework.Color.White);

                if (focused && _cursorVisible)
                {
                    float cursorX = bounds.X + 6 - scrollX + caretX;
                    var cursorRect = new Microsoft.Xna.Framework.Rectangle((int)Math.Round(cursorX), bounds.Y + 4, 2, Math.Max(1, bounds.Height - 8));
                    CMain.spriteBatch.Draw(pixel, cursorRect, Microsoft.Xna.Framework.Color.White);
                }
            }

            CMain.SpriteBatchScope.End();

            base.DrawControl();
        }

        private void UpdateCursorBlink()
        {
            if (_nextCursorBlinkTime == 0)
                _nextCursorBlinkTime = CMain.Time + 500;

            if (CMain.Time < _nextCursorBlinkTime)
                return;

            _nextCursorBlinkTime = CMain.Time + 500;
            _cursorVisible = !_cursorVisible;
        }

        private void ProcessTextInput()
        {
            char[] input = CMain.ConsumeTextInput();
            if (input.Length == 0)
                return;

            bool changed = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '\b')
                {
                    if (_caretIndex > 0 && _text.Length > 0)
                    {
                        _text = _text.Remove(_caretIndex - 1, 1);
                        _caretIndex = Math.Max(0, _caretIndex - 1);
                        changed = true;
                    }
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    EnterPressed?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                if (char.IsControl(c))
                    continue;

                if (NumericOnly && !char.IsDigit(c))
                    continue;

                if (_text.Length >= MaxLength)
                    continue;

                _text = _text.Insert(_caretIndex, c.ToString());
                _caretIndex = Math.Min(_text.Length, _caretIndex + 1);
                changed = true;
            }

            if (changed)
            {
                _cursorVisible = true;
                _nextCursorBlinkTime = 0;
                TextChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ProcessNavigationKeys()
        {
            KeyboardState current = Keyboard.GetState();

            if (IsNewKeyPress(current, Keys.Left))
                _caretIndex = Math.Max(0, _caretIndex - 1);
            if (IsNewKeyPress(current, Keys.Right))
                _caretIndex = Math.Min(_text.Length, _caretIndex + 1);
            if (IsNewKeyPress(current, Keys.Home))
                _caretIndex = 0;
            if (IsNewKeyPress(current, Keys.End))
                _caretIndex = _text.Length;
            if (IsNewKeyPress(current, Keys.Delete) && _caretIndex < _text.Length)
            {
                _text = _text.Remove(_caretIndex, 1);
                _cursorVisible = true;
                _nextCursorBlinkTime = 0;
                TextChanged?.Invoke(this, EventArgs.Empty);
            }

            _previousKeyboardState = current;
        }

        private bool IsNewKeyPress(KeyboardState current, Keys key)
        {
            return current.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        private float GetCaretX(SpriteFontBase font, string displayText)
        {
            int caret = Math.Clamp(_caretIndex, 0, displayText.Length);
            if (caret == 0)
                return 0F;

            string before = caret >= displayText.Length ? displayText : displayText.Substring(0, caret);
            return font.MeasureString(before).X;
        }

        private static string FilterDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool hasNonDigit = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    hasNonDigit = true;
                    break;
                }
            }

            if (!hasNonDigit)
                return value;

            var builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsDigit(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private void TryUpdateCaretFromMouse(System.Drawing.Point mouse)
        {
            System.Drawing.Rectangle bounds = DisplayRectangle;
            SpriteFontBase font = CMain.fontSystem?.GetFont(Math.Max(14, bounds.Height - 8));
            if (font == null)
            {
                _caretIndex = _text.Length;
                return;
            }

            string display = Password ? new string('*', Text.Length) : Text;
            float relativeX = mouse.X - (bounds.X + 6);
            if (relativeX <= 0F)
            {
                _caretIndex = 0;
                return;
            }

            float fullWidth = font.MeasureString(display).X;
            float availableWidth = Math.Max(1F, bounds.Width - 12F);
            float scrollX = 0F;
            if (fullWidth > availableWidth)
            {
                float caretX = GetCaretX(font, display);
                float maxScroll = Math.Max(0F, fullWidth - availableWidth);
                scrollX = Math.Clamp(caretX - availableWidth + 8F, 0F, maxScroll);
            }

            relativeX += scrollX;
            if (relativeX >= fullWidth)
            {
                _caretIndex = display.Length;
                return;
            }

            int best = 0;
            for (int i = 1; i <= display.Length; i++)
            {
                float width = font.MeasureString(display.Substring(0, i)).X;
                if (width >= relativeX)
                {
                    best = i;
                    break;
                }
            }

            _caretIndex = best;
            _cursorVisible = true;
            _nextCursorBlinkTime = 0;
        }

        private static Texture2D GetPixel()
        {
            if (_pixel == null)
            {
                _pixel = new Texture2D(CMain.spriteBatch.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Microsoft.Xna.Framework.Color.White });
            }

            return _pixel;
        }

        private static void DrawBorder(Texture2D pixel, Microsoft.Xna.Framework.Rectangle rect, Microsoft.Xna.Framework.Color color, int thickness)
        {
            if (thickness <= 0)
                return;

            CMain.spriteBatch.Draw(pixel, new Microsoft.Xna.Framework.Rectangle(rect.Left, rect.Top, rect.Width, thickness), color);
            CMain.spriteBatch.Draw(pixel, new Microsoft.Xna.Framework.Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), color);
            CMain.spriteBatch.Draw(pixel, new Microsoft.Xna.Framework.Rectangle(rect.Left, rect.Top, thickness, rect.Height), color);
            CMain.spriteBatch.Draw(pixel, new Microsoft.Xna.Framework.Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), color);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            bool wasActive = ActiveTextBox == this;
            if (ActiveTextBox == this)
                ActiveTextBox = null;

            if (wasActive)
                CMain.RequestSoftKeyboard(false);

            TextChanged = null;
            EnterPressed = null;
        }
    }
}
