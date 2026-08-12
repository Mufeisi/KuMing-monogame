using System.Net;
using System.Text.RegularExpressions;
using Server.MirNetwork;
using Server.Security;

namespace Server
{
    public partial class ConfigForm : Form
    {
        private GroupBox _tlsGroupBox = null!;
        private CheckBox _tlsEnabledCheckBox = null!;
        private CheckBox _allowLegacyV1CheckBox = null!;
        private TextBox _tlsPortTextBox = null!;
        private TextBox _tlsCertificatePathTextBox = null!;
        private Button _tlsCertificateBrowseButton = null!;

        public ConfigForm()
        {
            InitializeComponent();
            EnsureTlsControls();

            VPathTextBox.Text = Settings.VersionPath;
            VersionCheckBox.Checked = Settings.CheckVersion;
            RelogDelayTextBox.Text = Settings.RelogDelay.ToString();

            IPAddressTextBox.Text = Settings.IPAddress;
            PortTextBox.Text = Settings.Port.ToString();
            _tlsEnabledCheckBox.Checked = Settings.TlsEnabled;
            _tlsPortTextBox.Text = Settings.TlsPort.ToString();
            _allowLegacyV1CheckBox.Checked = Settings.AllowLegacyV1;
            _tlsCertificatePathTextBox.Text = Settings.TlsCertificatePath;
            UpdateTlsControlsEnabled();
            TimeOutTextBox.Text = Settings.TimeOut.ToString();
            MaxUserTextBox.Text = Settings.MaxUser.ToString();

            StartHTTPCheckBox.Checked = Settings.StartHTTPService;
            HTTPIPAddressTextBox.Text = Settings.HTTPIPAddress;
            HTTPTrustedIPAddressTextBox.Text = Settings.HTTPTrustedIPAddress;

            MicroServerActiveCheckBox.Checked = Settings.MicroServerActive;
            MicroResourcePathTextBox.Text = Settings.MicroResourcePath;
            MicroAuthorTextBox.Text = Settings.MicroAuthor;
            MicroCodeTextBox.Text = string.Empty;
            MicroCodeTextBox.Enabled = false;
            MicroCodeTextBox.PlaceholderText = "由受保护秘密存储提供";

            AccountCheckBox.Checked = Settings.AllowNewAccount;
            PasswordCheckBox.Checked = Settings.AllowChangePassword;
            LoginCheckBox.Checked = Settings.AllowLogin;
            NCharacterCheckBox.Checked = Settings.AllowNewCharacter;
            DCharacterCheckBox.Checked = Settings.AllowDeleteCharacter;
            StartGameCheckBox.Checked = Settings.AllowStartGame;
            AllowAssassinCheckBox.Checked = Settings.AllowCreateAssassin;
            AllowArcherCheckBox.Checked = Settings.AllowCreateArcher;
            Resolution_textbox.Text = Settings.AllowedResolution.ToString();

            SafeZoneBorderCheckBox.Checked = Settings.SafeZoneBorder;
            SafeZoneHealingCheckBox.Checked = Settings.SafeZoneHealing;
            gameMasterEffect_CheckBox.Checked = Settings.GameMasterEffect;
            lineMessageTimeTextBox.Text = Settings.LineMessageTimer.ToString();

            SaveDelayTextBox.Text = Settings.SaveDelay.ToString();

            ServerVersionLabel.Text = Application.ProductVersion;
            DBVersionLabel.Text = MirEnvir.Envir.LoadVersion.ToString() + ((MirEnvir.Envir.LoadVersion < MirEnvir.Envir.Version) ? " (需要更新)" : "");
        }

        private void ConfigForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Settings.Save();
            Settings.LoadVersion();
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            if (!TrySave(out string error))
            {
                configTabs.SelectedTab = tabPage2;
                MessageBox.Show(this, error, "配置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }

        public bool TrySave(out string error)
        {
            error = string.Empty;
            if (!ushort.TryParse(PortTextBox.Text, out ushort legacyPort) || legacyPort == 0)
            {
                error = "V1 端口必须是 1 到 65535 的整数。";
                return false;
            }
            ushort tlsPort = Settings.TlsPort;
            if (_tlsEnabledCheckBox.Checked && !ushort.TryParse(_tlsPortTextBox.Text, out tlsPort))
            {
                error = "TLS 端口必须是 1 到 65535 的整数。";
                return false;
            }
            if (!_tlsEnabledCheckBox.Checked && ushort.TryParse(_tlsPortTextBox.Text, out ushort disabledTlsPort))
                tlsPort = disabledTlsPort;

            if (!int.TryParse(SaveDelayTextBox.Text, out int saveDelay))
            {
                error = "自动保存间隔必须是整数分钟。";
                return false;
            }
            try
            {
                ProductionRpoPolicy.ValidateSaveDelay(saveDelay, enforceProductionMaximum: !Settings.TestServer);
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }

            try
            {
                TlsTransportPolicy.ValidateConfiguration(
                    _tlsEnabledCheckBox.Checked,
                    legacyPort,
                    tlsPort,
                    _tlsCertificatePathTextBox.Text.Trim());
            }
            catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.Security.Cryptography.CryptographicException)
            {
                error = ex.Message;
                return false;
            }

            Settings.VersionPath = VPathTextBox.Text;
            Settings.CheckVersion = VersionCheckBox.Checked;

            IPAddress tempIP;
            if (IPAddress.TryParse(IPAddressTextBox.Text, out tempIP))
                Settings.IPAddress = tempIP.ToString();

            Settings.StartHTTPService = StartHTTPCheckBox.Checked;
            if (tryParseHttp())
                Settings.HTTPIPAddress = HTTPIPAddressTextBox.Text.ToString();

            if (tryParseTrustedHttp())
                Settings.HTTPTrustedIPAddress = HTTPTrustedIPAddressTextBox.Text.ToString();

            Settings.MicroServerActive = MicroServerActiveCheckBox.Checked;
            Settings.MicroResourcePath = MicroResourcePathTextBox.Text;
            Settings.MicroAuthor = MicroAuthorTextBox.Text;

            ushort tempshort;
            int tempint;

            Settings.Port = legacyPort;
            Settings.TlsEnabled = _tlsEnabledCheckBox.Checked;
            Settings.TlsPort = tlsPort;
            Settings.AllowLegacyV1 = _allowLegacyV1CheckBox.Checked;
            Settings.TlsCertificatePath = _tlsCertificatePathTextBox.Text.Trim();

            if (ushort.TryParse(TimeOutTextBox.Text, out tempshort))
                Settings.TimeOut = tempshort;

            if (ushort.TryParse(MaxUserTextBox.Text, out tempshort))
                Settings.MaxUser = tempshort;

            if (ushort.TryParse(RelogDelayTextBox.Text, out tempshort))
                Settings.RelogDelay = tempshort;

            Settings.SaveDelay = saveDelay;

            Settings.AllowNewAccount = AccountCheckBox.Checked;
            Settings.AllowChangePassword = PasswordCheckBox.Checked;
            Settings.AllowLogin = LoginCheckBox.Checked;
            Settings.AllowNewCharacter = NCharacterCheckBox.Checked;
            Settings.AllowDeleteCharacter = DCharacterCheckBox.Checked;
            Settings.AllowStartGame = StartGameCheckBox.Checked;
            Settings.AllowCreateAssassin = AllowAssassinCheckBox.Checked;
            Settings.AllowCreateArcher = AllowArcherCheckBox.Checked;

            if (int.TryParse(Resolution_textbox.Text, out tempint))
                Settings.AllowedResolution = tempint;

            Settings.SafeZoneBorder = SafeZoneBorderCheckBox.Checked;
            Settings.SafeZoneHealing = SafeZoneHealingCheckBox.Checked;
            Settings.GameMasterEffect = gameMasterEffect_CheckBox.Checked;
            if (int.TryParse(lineMessageTimeTextBox.Text, out tempint))
                Settings.LineMessageTimer = tempint;

            return true;
        }

        private void EnsureTlsControls()
        {
            _tlsGroupBox = new GroupBox
            {
                Location = new Point(238, 12),
                Name = "TlsGroupBox",
                Size = new Size(222, 178),
                Text = "TLS V2",
            };
            _tlsEnabledCheckBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(12, 24),
                Name = "TlsEnabledCheckBox",
                Text = "启用 TLS 监听",
            };
            _tlsEnabledCheckBox.CheckedChanged += (_, _) => UpdateTlsControlsEnabled();
            _allowLegacyV1CheckBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(12, 50),
                Name = "AllowLegacyV1CheckBox",
                Text = "私网允许 V1 明文",
            };
            var tlsPortLabel = new Label
            {
                AutoSize = true,
                Location = new Point(12, 80),
                Text = "TLS 端口",
            };
            _tlsPortTextBox = new TextBox
            {
                Location = new Point(83, 76),
                MaxLength = 5,
                Name = "TlsPortTextBox",
                Size = new Size(58, 23),
            };
            _tlsPortTextBox.TextChanged += CheckUShort;
            var certificateLabel = new Label
            {
                AutoSize = true,
                Location = new Point(12, 108),
                Text = "PFX 证书（密码来自环境变量）",
            };
            _tlsCertificatePathTextBox = new TextBox
            {
                Location = new Point(12, 132),
                Name = "TlsCertificatePathTextBox",
                Size = new Size(162, 23),
            };
            _tlsCertificateBrowseButton = new Button
            {
                Location = new Point(179, 131),
                Name = "TlsCertificateBrowseButton",
                Size = new Size(32, 25),
                Text = "...",
            };
            _tlsCertificateBrowseButton.Click += TlsCertificateBrowseButton_Click;
            _tlsGroupBox.Controls.AddRange(new Control[]
            {
                _tlsEnabledCheckBox,
                _allowLegacyV1CheckBox,
                tlsPortLabel,
                _tlsPortTextBox,
                certificateLabel,
                _tlsCertificatePathTextBox,
                _tlsCertificateBrowseButton,
            });
            tabPage2.Controls.Add(_tlsGroupBox);
            _tlsGroupBox.BringToFront();
        }

        private void UpdateTlsControlsEnabled()
        {
            bool enabled = _tlsEnabledCheckBox.Checked;
            _tlsPortTextBox.Enabled = enabled;
            _tlsCertificatePathTextBox.Enabled = enabled;
            _tlsCertificateBrowseButton.Enabled = enabled;
        }

        private void TlsCertificateBrowseButton_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = "PKCS#12 证书 (*.pfx;*.p12)|*.pfx;*.p12|所有文件 (*.*)|*.*",
                FileName = _tlsCertificatePathTextBox.Text,
                Title = "选择 TLS 服务器证书",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _tlsCertificatePathTextBox.Text = dialog.FileName;
        }

        private void IPAddressCheck(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            IPAddress temp;

            ActiveControl.BackColor = !IPAddress.TryParse(ActiveControl.Text, out temp) ? Color.Red : SystemColors.Window;
        }

        private void CheckUShort(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            ushort temp;

            ActiveControl.BackColor = !ushort.TryParse(ActiveControl.Text, out temp) ? Color.Red : SystemColors.Window;
        }

        private void VPathBrowseButton_Click(object sender, EventArgs e)
        {
            if (VPathDialog.ShowDialog() == DialogResult.OK)
            {
                VPathTextBox.Text = string.Join(",", VPathDialog.FileNames);
            }
        }

        private void Resolution_textbox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;

            int temp;

            ActiveControl.BackColor = !int.TryParse(ActiveControl.Text, out temp) ? Color.Red : SystemColors.Window;

        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void SafeZoneBorderCheckBox_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void SafeZoneHealingCheckBox_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void HTTPIPAddressTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;
            ActiveControl.BackColor = !tryParseHttp() ? Color.Red : SystemColors.Window;
        }


        private void HTTPTrustedIPAddressTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ActiveControl != sender) return;
            ActiveControl.BackColor = !tryParseTrustedHttp() ? Color.Red : SystemColors.Window;
        }

        bool tryParseHttp()
        {
            if ((HTTPIPAddressTextBox.Text.StartsWith("http://") || HTTPIPAddressTextBox.Text.StartsWith("https://")) && HTTPIPAddressTextBox.Text.EndsWith("/"))
            {
                return true;
            }
            return false;
        }

        bool tryParseTrustedHttp()
        {
            string pattern = @"[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}";
            return Regex.IsMatch(HTTPTrustedIPAddressTextBox.Text, pattern);
        }

        private void StartHTTPCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Settings.StartHTTPService = StartHTTPCheckBox.Checked;
        }
    }
}
