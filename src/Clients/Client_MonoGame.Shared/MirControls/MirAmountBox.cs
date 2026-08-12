using MonoShare.MirGraphics;
using MonoShare.MirScenes;
using MonoShare.MirSounds;
using Microsoft.Xna.Framework.Input;
using System;
using System.Drawing;

namespace MonoShare.MirControls
{
    public sealed class MirAmountBox : MirImageControl
    {
        public MirLabel TitleLabel;
        public MirButton OKButton;
        public MirButton CancelButton;
        public MirButton CloseButton;
        public MirTextBox InputTextBox;
        public MirControl ItemImage;

        public int ImageIndex;
        public uint Amount { get; private set; }
        public uint MinAmount;
        public uint MaxAmount;

        private Rectangle _layoutSafeArea;
        private KeyboardState _previousKeyboardState;

        public MirAmountBox(string title, int image, uint max, uint min = 0, uint defaultAmount = 0)
        {
            ImageIndex = image;
            MaxAmount = max;
            MinAmount = min;

            Modal = true;
            Movable = false;

            Index = 238;
            Library = Libraries.Prguse;

            EnsureLayout();

            TitleLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(19, 8),
                Parent = this,
                NotControl = true,
                Text = title,
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(180, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Dispose();

            ItemImage = new MirControl
            {
                Location = new Point(15, 34),
                Size = new Size(38, 34),
                Parent = this,
                NotControl = true,
            };
            ItemImage.AfterDraw += (o, e) => DrawItem();

            OKButton = new MirButton
            {
                HoverIndex = 201,
                Index = 200,
                Library = Libraries.Title,
                Location = new Point(23, 76),
                Parent = this,
                PressedIndex = 202,
                Sound = SoundList.ButtonA,
            };
            OKButton.Click += (o, e) => Dispose();

            CancelButton = new MirButton
            {
                HoverIndex = 204,
                Index = 203,
                Library = Libraries.Title,
                Location = new Point(110, 76),
                Parent = this,
                PressedIndex = 205,
                Sound = SoundList.ButtonA,
            };
            CancelButton.Click += (o, e) => Dispose();

            InputTextBox = new MirTextBox
            {
                Parent = this,
                Border = true,
                BorderColour = Color.Lime,
                Location = new Point(58, 43),
                Size = new Size(132, 22),
                NumericOnly = true,
                MaxLength = 10,
                SoftKeyboardTitle = title,
                SoftKeyboardDescription = BuildDescription(),
            };
            InputTextBox.TextChanged += (o, e) => ValidateText();
            InputTextBox.EnterPressed += (o, e) =>
            {
                if (OKButton != null && !OKButton.IsDisposed && OKButton.Visible)
                    OKButton.InvokeMouseClick(EventArgs.Empty);
            };

            uint initial = defaultAmount > 0 && defaultAmount <= MaxAmount ? defaultAmount : MaxAmount;
            if (initial < MinAmount)
                initial = MinAmount;
            Amount = initial;
            InputTextBox.Text = initial.ToString();
            InputTextBox.MoveCaretToEnd();
            ValidateText();
            InputTextBox.SetFocus();
        }

        public override void Show()
        {
            if (Parent != null)
                return;

            Parent = MirScene.ActiveScene;
            Highlight();
        }

        public override void Event()
        {
            EnsureLayout();
            base.Event();

            KeyboardState state = Keyboard.GetState();
            if (state.IsKeyDown(Keys.Escape) && !_previousKeyboardState.IsKeyDown(Keys.Escape))
            {
                if (CancelButton != null && !CancelButton.IsDisposed)
                    CancelButton.InvokeMouseClick(EventArgs.Empty);
            }

            _previousKeyboardState = state;
        }

        private void EnsureLayout()
        {
            Rectangle safeArea = Settings.GetMobileSafeAreaBounds();
            if (_layoutSafeArea == safeArea)
                return;

            _layoutSafeArea = safeArea;

            int xOffset = (safeArea.Width - Size.Width) / 2;
            if (xOffset < 0) xOffset = 0;
            int yOffset = (safeArea.Height - Size.Height) / 2;
            if (yOffset < 0) yOffset = 0;

            Location = new Point(safeArea.Left + xOffset, safeArea.Top + yOffset);
        }

        private string BuildDescription()
        {
            if (MaxAmount == 0)
                return "仅数字";

            if (MinAmount == 0)
                return $"仅数字（最大 {MaxAmount:#,##0}）";

            return $"仅数字（{MinAmount:#,##0}~{MaxAmount:#,##0}）";
        }

        private void ValidateText()
        {
            if (InputTextBox == null || InputTextBox.IsDisposed)
                return;

            uint parsed;
            if (!uint.TryParse(InputTextBox.Text ?? string.Empty, out parsed) || parsed < MinAmount)
            {
                InputTextBox.BorderColour = Color.Red;
                if (OKButton != null && !OKButton.IsDisposed)
                    OKButton.Visible = false;
                return;
            }

            if (parsed > MaxAmount)
            {
                parsed = MaxAmount;
                string next = parsed.ToString();
                if (InputTextBox.Text != next)
                {
                    InputTextBox.Text = next;
                    InputTextBox.MoveCaretToEnd();
                }
            }

            Amount = parsed;
            InputTextBox.BorderColour = Amount == MaxAmount ? Color.Orange : Color.Lime;

            if (OKButton != null && !OKButton.IsDisposed)
                OKButton.Visible = true;
        }

        private void DrawItem()
        {
            if (ImageIndex < 0)
                return;

            Size iconSize = Libraries.Items.GetSize(ImageIndex);
            if (iconSize.IsEmpty)
                return;

            int x = ItemImage.DisplayLocation.X;
            int y = ItemImage.DisplayLocation.Y;

            x += (ItemImage.Size.Width - iconSize.Width) / 2;
            y += (ItemImage.Size.Height - iconSize.Height) / 2;

            Libraries.Items.Draw(ImageIndex, new Point(x, y), Color.White, offSet: false);
        }
    }
}

