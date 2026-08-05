namespace Server.MirForms.Systems
{
    internal sealed class PasswordPromptForm : Form
    {
        private readonly TextBox _passwordTextBox;

        private PasswordPromptForm(string title, string message)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var messageLabel = new Label
            {
                AutoSize = true,
                Text = message,
                Dock = DockStyle.Top,
                Padding = new Padding(12, 12, 12, 6),
            };

            _passwordTextBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Top,
                Margin = new Padding(12, 0, 12, 6),
            };

            var okButton = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                AutoSize = true,
            };

            var cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 6, 12, 12),
                WrapContents = false,
            };
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(okButton);

            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            mainPanel.Controls.Add(_passwordTextBox);

            Controls.Add(mainPanel);
            Controls.Add(buttonPanel);
            Controls.Add(messageLabel);

            ClientSize = new Size(420, 140);
            Shown += (_, _) => _passwordTextBox.Focus();
        }

        public static bool TryPrompt(IWin32Window owner, string title, string message, out string password)
        {
            using var form = new PasswordPromptForm(title, message);
            var result = form.ShowDialog(owner);
            if (result == DialogResult.OK)
            {
                password = form._passwordTextBox.Text ?? string.Empty;
                return true;
            }

            password = string.Empty;
            return false;
        }
    }
}

