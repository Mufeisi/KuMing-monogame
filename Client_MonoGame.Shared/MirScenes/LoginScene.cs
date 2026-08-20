using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using MonoShare;
using MonoShare.MirControls;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirSounds;
using S = ServerPackets;
using C = ClientPackets;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoShare.MirScenes
{
	    public sealed class LoginScene : MirScene
	    {
	        private MirAnimatedControl _background;
	        public MirLabel Version;

	        private LoginDialog _login;
	        private Rectangle _layoutSafeArea;
	        private byte[] _lastClientVersionHash = System.Array.Empty<byte>();
	        private string _lastClientVersionHashSource = string.Empty;
	        private bool _autoLoginAttempted;
	        private bool _manualLoginAttempted;
	        private bool _smokeTestAutoCreateAccountPending;
	        private bool _smokeTestAutoLoginSent;
	        private NewAccountDialog _account;
	        private ChangePasswordDialog _password;

        //private MirMessageBox _connectBox;

        //private InputKeyDialog _ViewKey;

        public MirImageControl TestLabel, ViolenceLabel, MinorLabel, YouthLabel;

        public LoginScene()
        {
            SoundManager.PlaySound(SoundList.IntroMusic, true);
            Disposing += (o, e) => SoundManager.StopSound(SoundList.IntroMusic);

            _background = new MirAnimatedControl
            {
                Size = new Size(800, 600),
                Animated = false,
                AnimationCount = 19,
                AnimationDelay = 100,
                Index = 0,
                Library = Libraries.ChrSel,
                Loop = false,
                Parent = this,

            };
            _background.Location = new Point((Settings.ScreenWidth - _background.Size.Width) / 2,
                (Settings.ScreenHeight - _background.Size.Height) / 2);

            _login = new LoginDialog { Parent = _background, Visible = true };
            _login.Location = new Point((_background.Size.Width - _login.Size.Width) / 2,
            (_background.Size.Height - _login.Size.Height) / 2);

            _login.OKButton.Click += (o, e) => _manualLoginAttempted = true;
            _login.PasswordTextBox.EnterPressed += (o, e) => _manualLoginAttempted = true;


 	            _login.AccountButton.Click += (o, e) =>
 	                {
	                    if (_background == null || _background.IsDisposed || _login == null || _login.IsDisposed)
	                        return;

	                    if (_account != null && !_account.IsDisposed)
	                        return;

	                    _login.Hide();
	                    _account = new NewAccountDialog { Parent = _background };
	                    _account.Location = new Point((_background.Size.Width - _account.Size.Width) / 2, (_background.Size.Height - _account.Size.Height) / 2);
	                    _account.Disposing += (o1, e1) =>
	                        {
	                            _account = null;
	                            _login.Show();
	                            _login.AccountIDTextBox?.SetFocus();
	                        };
 	                };
	            _login.PassButton.Click += (o, e) =>
	                {
	                    if (_background == null || _background.IsDisposed || _login == null || _login.IsDisposed)
	                        return;

	                    if (_password != null && !_password.IsDisposed)
	                        return;

	                    _login.Hide();
	                    _password = new ChangePasswordDialog { Parent = _background };
	                    _password.Location = new Point((_background.Size.Width - _password.Size.Width) / 2, (_background.Size.Height - _password.Size.Height) / 2);
	                    _password.Disposing += (o1, e1) =>
	                        {
	                            _password = null;
	                            _login.Show();
	                            _login.AccountIDTextBox?.SetFocus();
	                        };
	                };

            //_login.ViewKeyButton.Click += (o, e) =>     //ADD
            //{
            //    if (_ViewKey != null && !_ViewKey.IsDisposed) return;

            //    _ViewKey = new InputKeyDialog(_login) { Parent = _background };
            //};

            Version = new MirLabel
            {
                AutoSize = true,
                BackColour = Color.FromArgb(200, 50, 50, 50),
                Border = true,
                BorderColour = Color.White,
                Location = new Point(5, Settings.ScreenHeight - 20),
                Parent = this,
                Text = string.Format("Build: {0}.{1}", Globals.ProductCodename, Globals.ProductVersion),
            };

            EnsureLayout();

            //TestLabel = new MirImageControl
            //{
            //    Index = 79,
            //    Library = Libraries.Prguse,
            //    Parent = this,
            //    Location = new Point(_background.Size.Width - 116, 10),
            //    Visible = Settings.UseTestConfig
            //};

            //ViolenceLabel = new MirImageControl
            //{
            //    Index = 89,
            //    Library = Libraries.Prguse,
            //    Parent = this,
            //    Location = new Point(471, 10)
            //};

            //MinorLabel = new MirImageControl
            //{
            //    Index = 87,
            //    Library = Libraries.Prguse,
            //    Parent = this,
            //    Location = new Point(578, 10)
            //};

            //YouthLabel = new MirImageControl
            //{
            //    Index = 88,
            //    Library = Libraries.Prguse,
            //    Parent = this,
            //    Location = new Point(684, 10)
            //};
            Network.Connect();
            //_connectBox = new MirMessageBox("正在尝试连接到服务器.", MirMessageBoxButtons.Cancel);
            //_connectBox.CancelButton.Click += (o, e) => Program.Form.Close();
            //Shown += (sender, args) =>
            //    {
            //        Network.Connect();
            //        _connectBox.Show();
            //    };
        }

        public override void Process()
        {
            //if (!Network.Connected && _connectBox.Label != null)
            //    _connectBox.Label.Text = string.Format(GameLanguage.AttemptingConnect, "\n\n", Network.ConnectAttempt);

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MobileMainHudPrewarm.Tick();

            EnsureLayout();
        }

        private void EnsureLayout()
        {
            Rectangle safeArea = Settings.GetMobileSafeAreaBounds();
            if (_layoutSafeArea == safeArea)
                return;

            _layoutSafeArea = safeArea;

            if (_background != null && !_background.IsDisposed)
            {
                int x = safeArea.Left + (safeArea.Width - _background.Size.Width) / 2;
                int y = safeArea.Top + (safeArea.Height - _background.Size.Height) / 2;
                _background.Location = new Point(x, y);
            }

            if (_login != null && !_login.IsDisposed && _background != null && !_background.IsDisposed)
            {
                _login.Location = new Point((_background.Size.Width - _login.Size.Width) / 2, (_background.Size.Height - _login.Size.Height) / 2);
            }

            if (Version != null && !Version.IsDisposed)
            {
                int x = safeArea.Left + 5;
                int y = safeArea.Bottom - 20;
                if (y < safeArea.Top)
                    y = safeArea.Top;
                Version.Location = new Point(x, y);
            }
        }
        public override void ProcessPacket(Packet p)
        {
            switch (p.Index)
            {
                case (short)ServerPacketIds.Connected:
                    Network.Connected = true;
                    SendVersion();
                    break;
                case (short)ServerPacketIds.ClientVersion:
                    ClientVersion((S.ClientVersion)p);
                    break;
                case (short)ServerPacketIds.NewAccount:
                    NewAccount((S.NewAccount)p);
                    break;
                case (short)ServerPacketIds.ChangePassword:
                    ChangePassword((S.ChangePassword)p);
                    break;
                case (short)ServerPacketIds.ChangePasswordBanned:
                    ChangePassword((S.ChangePasswordBanned)p);
                    break;
                case (short)ServerPacketIds.Login:
                    Login((S.Login)p);
                    break;
                case (short)ServerPacketIds.LoginBanned:
                    Login((S.LoginBanned)p);
                    break;
                case (short)ServerPacketIds.LoginSuccess:
                    Login((S.LoginSuccess)p);
                    break;
                default:
                    base.ProcessPacket(p);
                    break;
            }
        }

	        private void SendVersion()
	        {
	            //_connectBox.Label.Text = "正在发送客户端版本.";

	            C.ClientVersion p = new C.ClientVersion
	            {
	                VersionHash = System.Array.Empty<byte>()
	            };

	            try
	            {
	                p.VersionHash = ComputeClientVersionHash(out string source);
	                _lastClientVersionHash = p.VersionHash ?? System.Array.Empty<byte>();
	                _lastClientVersionHashSource = source ?? string.Empty;

	                if (Settings.LogErrors)
	                    CMain.SaveLog($"ClientVersionHash={ToHex(_lastClientVersionHash)} Source={_lastClientVersionHashSource}");
	            }
	            catch (System.Exception ex)
	            {
	                _lastClientVersionHash = p.VersionHash ?? System.Array.Empty<byte>();
	                _lastClientVersionHashSource = "ComputeFailed";

	                if (Settings.LogErrors)
	                    CMain.SaveError($"SendVersion 计算版本 Hash 失败：{ex}");
	            }

	            Network.Enqueue(p);
	        }

	        private static byte[] ComputeClientVersionHash(out string source)
	        {
	            Assembly assembly = Assembly.GetExecutingAssembly();

	            string assemblyName = string.Empty;
	            try
	            {
	                assemblyName = assembly.GetName().Name ?? string.Empty;
	            }
	            catch
	            {
	            }

	            string[] candidates = new[]
	            {
	                SafeGetAssemblyLocation(assembly),
	                SafeGetProcessPath(),
	                SafeCombineBaseDirectory(AppContext.BaseDirectory, assemblyName + ".dll"),
	                SafeCombineBaseDirectory(AppContext.BaseDirectory, Path.GetFileName(SafeGetAssemblyLocation(assembly))),
	                SafeCombineBaseDirectory(AppDomain.CurrentDomain.BaseDirectory, assemblyName + ".dll"),
	            };

	            for (int i = 0; i < candidates.Length; i++)
	            {
	                string candidate = candidates[i];
	                if (string.IsNullOrWhiteSpace(candidate))
	                    continue;

	                if (TryComputeMd5FromFile(candidate, out byte[] hash))
	                {
	                    source = candidate;
	                    return hash;
	                }
	            }

	            source = "AssemblyFullName";
	            string identity = string.Empty;
	            try
	            {
	                identity = assembly.FullName ?? assemblyName;
	            }
	            catch
	            {
	                identity = assemblyName;
	            }

	            using MD5 md5 = MD5.Create();
	            return md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity ?? string.Empty));
	        }

	        private static string SafeGetAssemblyLocation(Assembly assembly)
	        {
	            try
	            {
	                return assembly?.Location ?? string.Empty;
	            }
	            catch
	            {
	                return string.Empty;
	            }
	        }

	        private static string SafeGetProcessPath()
	        {
	            try
	            {
	                return Environment.ProcessPath ?? string.Empty;
	            }
	            catch
	            {
	                return string.Empty;
	            }
	        }

	        private static string SafeCombineBaseDirectory(string baseDirectory, string fileName)
	        {
	            if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(fileName))
	                return string.Empty;

	            try
	            {
	                return Path.Combine(baseDirectory, fileName);
	            }
	            catch
	            {
	                return string.Empty;
	            }
	        }

	        private static bool TryComputeMd5FromFile(string filePath, out byte[] hash)
	        {
	            hash = System.Array.Empty<byte>();

	            try
	            {
	                if (string.IsNullOrWhiteSpace(filePath))
	                    return false;

	                string fullPath = Path.GetFullPath(filePath);
	                if (!File.Exists(fullPath))
	                    return false;

	                using var stream = File.OpenRead(fullPath);
	                using MD5 md5 = MD5.Create();
	                hash = md5.ComputeHash(stream);
	                return true;
	            }
	            catch
	            {
	                hash = System.Array.Empty<byte>();
	                return false;
	            }
	        }

	        private static string ToHex(byte[] bytes)
	        {
	            if (bytes == null || bytes.Length == 0)
	                return string.Empty;

	            var sb = new System.Text.StringBuilder(bytes.Length * 2);
	            for (int i = 0; i < bytes.Length; i++)
	            {
	                sb.Append(bytes[i].ToString("x2"));
	            }

	            return sb.ToString();
	        }
	        private void ClientVersion(S.ClientVersion p)
	        {
	            switch (p.Result)
	            {
	                case 0:
	                    if (Settings.LogErrors)
	                    {
	                        CMain.SaveError($"客户端版本不匹配，服务端拒绝连接。Hash={ToHex(_lastClientVersionHash)} Source={_lastClientVersionHashSource}");
	                    }

	                    new MirMessageBox(
	                        $"版本错误：服务端拒绝连接。\n\nHash={ToHex(_lastClientVersionHash)}\nSource={_lastClientVersionHashSource}\n\n可尝试：关闭服务端 CheckVersion 或把对应版本文件加入 VersionPath。",
	                        MirMessageBoxButtons.OK)
	                        .Show();

	                    Network.Disconnect();
	                    break;
	                case 1:
	                    //_connectBox.Dispose();
	                    _login.Show();
	                    if (Settings.LogErrors)
	                        CMain.SaveLog("握手通过：ClientVersion=OK，已进入登录界面。");

	                    TryAutoLogin();
	                    break;
	            }
	        }

	        private void TryAutoLogin()
	        {
	            if (_autoLoginAttempted)
	                return;

	            if (!Settings.SmokeTestAutoLogin)
	                return;

	            _autoLoginAttempted = true;

	            string account = Settings.AccountID ?? string.Empty;
	            string password = Settings.Password ?? string.Empty;

	            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveLog($"SmokeTest 自动登录跳过：账号或密码为空（AccountLen={account.Length}, PasswordLen={password.Length}, RememberPassword={Settings.RememberPassword}）。");
	                return;
	            }

	            try
	            {
	                if (Settings.SmokeTestAutoCreateAccount)
	                {
	                    _smokeTestAutoCreateAccountPending = true;

	                    if (Settings.LogErrors)
	                        CMain.SaveLog($"SmokeTest 自动注册发起：Account={account}（PasswordLen={password.Length}）。");

	                    Network.Enqueue(new C.NewAccount
	                    {
	                        AccountID = account,
	                        Password = password,
	                        EMailAddress = string.Empty,
	                        BirthDate = DateTime.MinValue,
	                        UserName = string.Empty,
	                        SecretQuestion = string.Empty,
	                        SecretAnswer = string.Empty,
	                    });
	                }
	                else
	                {
	                    EnqueueSmokeTestLogin(account, password);
	                }
	            }
	            catch (System.Exception ex)
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveError($"SmokeTest 自动登录失败：{ex}");
	            }
	        }

	        private void EnqueueSmokeTestLogin(string account, string password)
	        {
	            if (_smokeTestAutoLoginSent)
	                return;

	            _smokeTestAutoLoginSent = true;

	            if (Settings.LogErrors)
	                CMain.SaveLog($"SmokeTest 自动登录发起：Account={account}（PasswordLen={password.Length}）。");

	            Network.Enqueue(new C.Login { AccountID = account, Password = password });
	        }
        private void NewAccount(S.NewAccount p)
        {
            if (_smokeTestAutoCreateAccountPending)
            {
                _smokeTestAutoCreateAccountPending = false;

                try
                {
                    if (Settings.LogErrors)
                        CMain.SaveLog($"SmokeTest 自动注册返回：Result={p.Result}（0=禁用/限流 1=账号格式 2=密码格式 7=已存在 8=成功）。");

                    if (p.Result == 8 || p.Result == 7)
                    {
                        string account = Settings.AccountID ?? string.Empty;
                        string password = Settings.Password ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password))
	                        EnqueueSmokeTestLogin(account, password);
                    }
                    else
                    {
                        if (Settings.LogErrors)
                            CMain.SaveError($"SmokeTest 自动注册失败：Result={p.Result}");
                    }
                }
                catch (System.Exception ex)
                {
                    if (Settings.LogErrors)
                        CMain.SaveError($"SmokeTest 自动注册处理失败：{ex}");
                }
            }

            if (_account == null || _account.IsDisposed)
                return;

            _account.OKButton.Enabled = true;
            switch (p.Result)
            {
                case 0:
                    MirMessageBox.Show("创建账号失败：当前已禁用创建账号，或创建过于频繁已被限制。");
                    _account.Dispose();
                    break;
                case 1:
                    MirMessageBox.Show($"账号格式错误：仅允许字母/数字，长度 {Globals.MinAccountIDLength}-{Globals.MaxAccountIDLength} 位。");
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 2:
                    MirMessageBox.Show($"密码格式错误：仅允许字母/数字，长度 {Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位。");
                    _account.Password1TextBox.SetFocus();
                    break;
                case 3:
                    MirMessageBox.Show("邮箱地址格式错误。");
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 4:
                    MirMessageBox.Show("用户名填写有误。");
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 5:
                    MirMessageBox.Show("密保问题填写有误。");
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 6:
                    MirMessageBox.Show("密保答案填写有误。");
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 7:
                    MirMessageBox.Show("账号已存在，请更换账号名。");
                    _account.AccountIDTextBox.Text = string.Empty;
                    _account.AccountIDTextBox.SetFocus();
                    break;
                case 8:
                    Settings.AccountID = _account.AccountIDTextBox.Text;
                    Settings.Password = _account.Password1TextBox.Text;
                    Settings.Save();

                    MirMessageBox.Show("账号创建成功，请返回登录。");
                    _account.Dispose();
                    break;
                default:
                    MirMessageBox.Show($"创建账号失败：未知错误（Result={p.Result}）。");
                    break;
            }
        }
        private void ChangePassword(S.ChangePassword p)
        {
            if (_password == null || _password.IsDisposed)
                return;

            _password.OKButton.Enabled = true;

            switch (p.Result)
            {
                case 0:
                    MirMessageBox.Show("修改密码失败：当前已禁用修改密码。");
                    _password.Dispose();
                    break;
                case 1:
                    MirMessageBox.Show($"账号格式错误：仅允许字母/数字，长度 {Globals.MinAccountIDLength}-{Globals.MaxAccountIDLength} 位。");
                    _password.AccountIDTextBox.SetFocus();
                    break;
                case 2:
                    MirMessageBox.Show("当前密码填写有误。");
                    _password.CurrentPasswordTextBox.SetFocus();
                    break;
                case 3:
                    MirMessageBox.Show($"新密码格式错误：仅允许字母/数字，长度 {Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位。");
                    _password.NewPassword1TextBox.SetFocus();
                    break;
                case 4:
                    MirMessageBox.Show("账号不存在。");
                    _password.AccountIDTextBox.SetFocus();
                    break;
                case 5:
                    MirMessageBox.Show("账号或密码错误。");
                    _password.CurrentPasswordTextBox.SetFocus();
                    _password.CurrentPasswordTextBox.Text = string.Empty;
                    break;
                case 6:
                    MirMessageBox.Show("密码修改成功。");
                    _password.Dispose();
                    break;
            }
        }
        private void ChangePassword(S.ChangePasswordBanned p)
        {
            if (_password != null && !_password.IsDisposed)
                _password.Dispose();

            TimeSpan d = p.ExpiryDate - CMain.Now;
            MirMessageBox.Show(string.Format("此账号暂时禁止修改密码。\n\n原因：{0}\n到期时间：{1}\n剩余：{2:#,##0} 小时 {3} 分 {4} 秒",
                                             p.Reason, p.ExpiryDate, Math.Floor(d.TotalHours), d.Minutes, d.Seconds));
        }
        private void Login(S.Login p)
        {
            _login.OKButton.Enabled = true;
            switch (p.Result)
            {
                case 0:
                    ReportLoginFailure("登录失败：当前已禁用登录。");
                    break;
                case 1:
                    ReportLoginFailure($"账号格式错误：仅允许字母/数字，长度 {Globals.MinAccountIDLength}-{Globals.MaxAccountIDLength} 位。");
                    _login.AccountIDTextBox.SetFocus();
                    break;
                case 2:
                    ReportLoginFailure($"密码格式错误：仅允许字母/数字，长度 {Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位。");
                    _login.PasswordTextBox.SetFocus();
                    break;
                case 3:
                    ReportLoginFailure("账号不存在。");
                    _login.PasswordTextBox.SetFocus();
                    break;
                case 4:
                    ReportLoginFailure("账号或密码错误。");
                    _login.PasswordTextBox.Text = string.Empty;
                    _login.PasswordTextBox.SetFocus();
                    break;
            }
        }

		        private void ReportLoginFailure(string message)
		        {
		            bool suppressUi = !_manualLoginAttempted && Settings.SmokeTestAutoLogin && (_smokeTestAutoLoginSent || _smokeTestAutoCreateAccountPending);
		            if (suppressUi)
		            {
		                if (Settings.LogErrors)
		                    CMain.SaveError($"SmokeTest 登录失败：{message}");
		                return;
	            }

	            MirMessageBox.Show(message);
	        }
        private void Login(S.LoginBanned p)
        {
            _login.OKButton.Enabled = true;

            TimeSpan d = p.ExpiryDate - CMain.Now;

		            string message = string.Format("此账号已被禁止登录。\n\n原因：{0}\n到期时间：{1}\n剩余：{2:#,##0} 小时 {3} 分 {4} 秒",
		                                           p.Reason, p.ExpiryDate, Math.Floor(d.TotalHours), d.Minutes, d.Seconds);

		            bool suppressUi = !_manualLoginAttempted && Settings.SmokeTestAutoLogin && (_smokeTestAutoLoginSent || _smokeTestAutoCreateAccountPending);
		            if (suppressUi)
		            {
		                if (Settings.LogErrors)
		                    CMain.SaveError($"SmokeTest 登录封禁：{message}");
		                return;
	            }

	            MirMessageBox.Show(message);
        }
	        private void Login(S.LoginSuccess p)
	        {
	            Enabled = false;
	            _login.Dispose();
	            //if (_ViewKey != null && !_ViewKey.IsDisposed) _ViewKey.Dispose();

	            if (Settings.LogErrors)
	                CMain.SaveLog($"登录成功：Characters={p.Characters?.Count ?? 0}，准备进入选角。");

	            SoundManager.PlaySound(SoundList.LoginEffect);
	            _background.Animated = true;
	            _background.AfterAnimation += (o, e) =>
	                {
	                    Dispose();
	                    ActiveScene = new SelectScene(p.Characters);
	                };
	        }

        public sealed class LoginDialog : MirImageControl
        {
            public MirImageControl TitleLabel, AccountIDLabel, PassLabel;
            public MirButton AccountButton, CloseButton, OKButton, PassButton, ViewKeyButton;
            public MirTextBox AccountIDTextBox, PasswordTextBox;
            private bool _accountIDValid, _passwordValid;

            public LoginDialog()
            {
                Index = 1084;
                Library = Libraries.Prguse;
                PixelDetect = false;
                Size = new Size(328, 220);

                TitleLabel = new MirImageControl
                {
                    Index = 30,
                    Library = Libraries.Title,
                    Parent = this,
                };
                TitleLabel.Location = new Point((Size.Width - TitleLabel.Size.Width) / 2, 12);

                AccountIDLabel = new MirImageControl
                {
                    Index = 31,
                    Library = Libraries.Title,
                    Parent = this,
                    Location = new Point(52, 83),
                };

                PassLabel = new MirImageControl
                {
                    Index = 32,
                    Library = Libraries.Title,
                    Parent = this,
                    Location = new Point(43, 105)
                };

                OKButton = new MirButton
                {
                    Enabled = true,
                    Size = new Size(42, 42),
                    HoverIndex = 321,
                    Index = 320,
                    Library = Libraries.Title,
                    Location = new Point(227, 81),
                    Parent = this,
                    PressedIndex = 322
                };
                OKButton.Click += (o, e) => Login();

                AccountButton = new MirButton
                {
                    HoverIndex = 324,
                    Index = 323,
                    Library = Libraries.Title,
                    Location = new Point(60, 163),
                    Parent = this,
                    PressedIndex = 325,
                };

                PassButton = new MirButton
                {
                    HoverIndex = 327,
                    Index = 326,
                    Library = Libraries.Title,
                    Location = new Point(166, 163),
                    Parent = this,
                    PressedIndex = 328,
                };

                ViewKeyButton = new MirButton
                {
                    HoverIndex = 333,
                    Index = 332,
                    Library = Libraries.Title,
                    Location = new Point(60, 189),
                    Parent = this,
                    PressedIndex = 334,
                };

                CloseButton = new MirButton
                {
                    HoverIndex = 330,
                    Index = 329,
                    Library = Libraries.Title,
                    Location = new Point(166, 189),
                    Parent = this,
                    PressedIndex = 331,
                };
                //CloseButton.Click += (o, e) => Program.Form.Close();

                AccountIDTextBox = new MirTextBox
                {
                    Location = new Point(85, 84),
                    Parent = this,
                    Size = new Size(136, 22),
                    MaxLength = Globals.MaxAccountIDLength,
                    SoftKeyboardTitle = "账号",
                    SoftKeyboardDescription = $"长度:{Globals.MinAccountIDLength}~{Globals.MaxAccountIDLength}",
                };
                AccountIDTextBox.Text = Settings.AccountID;

                PasswordTextBox = new MirTextBox
                {
                    Location = new Point(85, 106),
                    Parent = this,
                    Password = true,
                    Size = new Size(136, 22),
                    MaxLength = Globals.MaxPasswordLength,
                    SoftKeyboardTitle = "密码",
                    SoftKeyboardDescription = Settings.RememberPassword ? "将保存到配置" : "不会保存到配置",
                };
                PasswordTextBox.Text = Settings.Password;

#if REAL_ANDROID
                // 说明：移动端正式使用时不应默认填充开发账号。
                // 仅当启用 SmokeTest 自动流程时，才允许自动填充兜底值，避免误登录/误保存密码导致“无法登录/提示被抑制”的错觉。
                bool smokeEnabled = Settings.SmokeTestAutoLogin || Settings.SmokeTestAutoCreateAccount || Settings.SmokeTestAutoCreateCharacter || Settings.SmokeTestAutoStartGame;
                if (smokeEnabled)
                {
                    if (string.IsNullOrWhiteSpace(AccountIDTextBox.Text))
                        AccountIDTextBox.Text = "qtanchun";

                    if (string.IsNullOrWhiteSpace(PasswordTextBox.Text))
                        PasswordTextBox.Text = "123456";
                }
#endif

                AccountIDTextBox.EnterPressed += (o, e) => PasswordTextBox.SetFocus();
                PasswordTextBox.EnterPressed += (o, e) => Login();

                AccountIDTextBox.SetFocus();

            }

            //private void AccountIDTextBox_TextChanged(object sender, EventArgs e)
            //{
            //    Regex reg =
            //        new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");

            //    if (string.IsNullOrEmpty(AccountIDTextBox.Text) || !reg.IsMatch(AccountIDTextBox.TextBox.Text))
            //    {
            //        _accountIDValid = false;
            //        AccountIDTextBox.Border = !string.IsNullOrEmpty(AccountIDTextBox.Text);
            //        AccountIDTextBox.BorderColour = Color.Red;
            //    }
            //    else
            //    {
            //        _accountIDValid = true;
            //        AccountIDTextBox.Border = true;
            //        AccountIDTextBox.BorderColour = Color.Green;
            //    }

            //}
            //private void PasswordTextBox_TextChanged(object sender, EventArgs e)
            //{
            //    Regex reg =
            //        new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

            //    if (string.IsNullOrEmpty(PasswordTextBox.TextBox.Text) || !reg.IsMatch(PasswordTextBox.TextBox.Text))
            //    {
            //        _passwordValid = false;
            //        PasswordTextBox.Border = !string.IsNullOrEmpty(PasswordTextBox.TextBox.Text);
            //        PasswordTextBox.BorderColour = Color.Red;
            //    }
            //    else
            //    {
            //        _passwordValid = true;
            //        PasswordTextBox.Border = true;
            //        PasswordTextBox.BorderColour = Color.Green;
            //    }

            //    RefreshLoginButton();
            //}
            //public void TextBox_KeyPress(object sender, KeyPressEventArgs e)
            //{
            //    if (sender == null || e.KeyChar != (char)Keys.Enter) return;

            //    e.Handled = true;

            //    if (!_accountIDValid)
            //    {
            //        AccountIDTextBox.SetFocus();
            //        return;
            //    }
            //    if (!_passwordValid)
            //    {
            //        PasswordTextBox.SetFocus();
            //        return;
            //    }

            //    if (OKButton.Enabled)
            //        OKButton.InvokeMouseClick(null);
            //}
            //private void RefreshLoginButton()
            //{
            //    OKButton.Enabled = _accountIDValid && _passwordValid;
            //}

            private void Login()
            {
                OKButton.Enabled = false;

                string account = AccountIDTextBox?.Text ?? string.Empty;
                string password = PasswordTextBox?.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(account))
                    account = Settings.AccountID;

                if (string.IsNullOrWhiteSpace(password))
                    password = Settings.Password;

                Settings.AccountID = account ?? string.Empty;
                Settings.Password = password ?? string.Empty;
                Settings.Save();

                Network.Enqueue(new C.Login { AccountID = Settings.AccountID, Password = Settings.Password });
            }

            //public override void Show()
            //{
            //    if (Visible) return;
            //    Visible = true;
            //    AccountIDTextBox.SetFocus();

            //    if (Settings.Password != string.Empty && Settings.AccountID != string.Empty)
            //    {
            //        Login();
            //    }
            //}
            //public void Clear()
            //{
            //    AccountIDTextBox.Text = string.Empty;
            //    PasswordTextBox.Text = string.Empty;
            //}

            #region Disposable

            //protected override void Dispose(bool disposing)
            //{
            //    if (disposing)
            //    {
            //        TitleLabel = null;
            //        AccountIDLabel = null;
            //        PassLabel = null;
            //        AccountButton = null;
            //        CloseButton = null;
            //        OKButton = null;
            //        PassButton = null;
            //        AccountIDTextBox = null;
            //        PasswordTextBox = null;

            //    }

            //    base.Dispose(disposing);
            //}

             #endregion
         }

	        public sealed class NewAccountDialog : MirImageControl
	        {
	            private static readonly Regex AccountIdReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");
	            private static readonly Regex PasswordReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

	            public MirButton OKButton, CancelButton;
	            public MirTextBox AccountIDTextBox, Password1TextBox, Password2TextBox;

	            private bool _accountIDValid;
	            private bool _password1Valid;
	            private bool _password2Valid;

	            public NewAccountDialog()
	            {
	                Index = 63;
	                Library = Libraries.Prguse;
	                Modal = true;
	                PixelDetect = false;

	                CancelButton = new MirButton
	                {
	                    HoverIndex = 204,
	                    Index = 203,
	                    Library = Libraries.Title,
	                    Location = new Point(409, 425),
	                    Parent = this,
	                    PressedIndex = 205
	                };
	                CancelButton.Click += (o, e) => Dispose();

	                OKButton = new MirButton
	                {
	                    Enabled = false,
	                    HoverIndex = 201,
	                    Index = 200,
	                    Library = Libraries.Title,
	                    Location = new Point(135, 425),
	                    Parent = this,
	                    PressedIndex = 202,
	                };
	                OKButton.Click += (o, e) => CreateAccount();

	                AccountIDTextBox = new MirTextBox
	                {
	                    Location = new Point(226, 103),
	                    Parent = this,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxAccountIDLength,
	                    SoftKeyboardTitle = "账号",
	                    SoftKeyboardDescription = $"仅字母/数字，{Globals.MinAccountIDLength}-{Globals.MaxAccountIDLength} 位",
	                };
	                AccountIDTextBox.TextChanged += (o, e) => ValidateAccountID();
	                AccountIDTextBox.EnterPressed += (o, e) => Password1TextBox?.SetFocus();

	                Password1TextBox = new MirTextBox
	                {
	                    Location = new Point(226, 129),
	                    Parent = this,
	                    Password = true,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxPasswordLength,
	                    SoftKeyboardTitle = "密码",
	                    SoftKeyboardDescription = $"仅字母/数字，{Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位",
	                };
	                Password1TextBox.TextChanged += (o, e) => ValidatePassword1();
	                Password1TextBox.EnterPressed += (o, e) => Password2TextBox?.SetFocus();

	                Password2TextBox = new MirTextBox
	                {
	                    Location = new Point(226, 155),
	                    Parent = this,
	                    Password = true,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxPasswordLength,
	                    SoftKeyboardTitle = "确认密码",
	                    SoftKeyboardDescription = "再次输入相同密码",
	                };
	                Password2TextBox.TextChanged += (o, e) => ValidatePassword2();
	                Password2TextBox.EnterPressed += (o, e) =>
	                {
	                    if (OKButton.Enabled)
	                        CreateAccount();
	                };

	                AccountIDTextBox.SetFocus();
	            }

	            private void ValidateAccountID()
	            {
	                string value = AccountIDTextBox.Text ?? string.Empty;

	                _accountIDValid = value.Length > 0 && AccountIdReg.IsMatch(value);
	                AccountIDTextBox.BorderColour = ResolveBorderColour(_accountIDValid, value.Length);
	                RefreshConfirmButton();
	            }

	            private void ValidatePassword1()
	            {
	                string value = Password1TextBox.Text ?? string.Empty;

	                _password1Valid = value.Length > 0 && PasswordReg.IsMatch(value);
	                Password1TextBox.BorderColour = ResolveBorderColour(_password1Valid, value.Length);

	                ValidatePassword2();
	            }

	            private void ValidatePassword2()
	            {
	                string value = Password2TextBox.Text ?? string.Empty;
	                string password1 = Password1TextBox.Text ?? string.Empty;

	                _password2Valid = value.Length > 0 && PasswordReg.IsMatch(value) && string.Equals(value, password1, StringComparison.Ordinal);
	                Password2TextBox.BorderColour = ResolveBorderColour(_password2Valid, value.Length);
	                RefreshConfirmButton();
	            }

	            private static Color ResolveBorderColour(bool valid, int length)
	            {
	                if (length <= 0)
	                    return Color.Gray;

	                return valid ? Color.Green : Color.Red;
	            }

	            private void RefreshConfirmButton()
	            {
	                OKButton.Enabled = _accountIDValid && _password1Valid && _password2Valid;
	            }

	            private void CreateAccount()
	            {
	                if (!OKButton.Enabled)
	                    return;

	                OKButton.Enabled = false;

	                Network.Enqueue(new C.NewAccount
	                {
	                    AccountID = AccountIDTextBox.Text ?? string.Empty,
	                    Password = Password1TextBox.Text ?? string.Empty,
	                    EMailAddress = string.Empty,
	                    BirthDate = DateTime.MinValue,
	                    UserName = string.Empty,
	                    SecretQuestion = string.Empty,
	                    SecretAnswer = string.Empty,
	                });
	            }
	        }

	        public sealed class ChangePasswordDialog : MirImageControl
	        {
	            private static readonly Regex AccountIdReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");
	            private static readonly Regex PasswordReg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

	            public readonly MirButton OKButton, CancelButton;
	            public readonly MirTextBox AccountIDTextBox, CurrentPasswordTextBox, NewPassword1TextBox, NewPassword2TextBox;

	            private bool _accountIDValid;
	            private bool _currentPasswordValid;
	            private bool _newPassword1Valid;
	            private bool _newPassword2Valid;

	            public ChangePasswordDialog()
	            {
	                Index = 50;
	                Library = Libraries.Prguse;
	                Modal = true;
	                PixelDetect = false;

	                CancelButton = new MirButton
	                {
	                    HoverIndex = 111,
	                    Index = 110,
	                    Library = Libraries.Title,
	                    Location = new Point(222, 236),
	                    Parent = this,
	                    PressedIndex = 112
	                };
	                CancelButton.Click += (o, e) => Dispose();

	                OKButton = new MirButton
	                {
	                    Enabled = false,
	                    HoverIndex = 108,
	                    Index = 107,
	                    Library = Libraries.Title,
	                    Location = new Point(80, 236),
	                    Parent = this,
	                    PressedIndex = 109,
	                };
	                OKButton.Click += (o, e) => ChangePassword();

	                AccountIDTextBox = new MirTextBox
	                {
	                    Location = new Point(178, 75),
	                    Parent = this,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxAccountIDLength,
	                    SoftKeyboardTitle = "账号",
	                    SoftKeyboardDescription = $"仅字母/数字，{Globals.MinAccountIDLength}-{Globals.MaxAccountIDLength} 位",
	                };
	                AccountIDTextBox.TextChanged += (o, e) => ValidateAccountID();
	                AccountIDTextBox.EnterPressed += (o, e) => CurrentPasswordTextBox?.SetFocus();

	                CurrentPasswordTextBox = new MirTextBox
	                {
	                    Location = new Point(178, 113),
	                    Parent = this,
	                    Password = true,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxPasswordLength,
	                    SoftKeyboardTitle = "当前密码",
	                    SoftKeyboardDescription = string.Empty,
	                };
	                CurrentPasswordTextBox.TextChanged += (o, e) => ValidateCurrentPassword();
	                CurrentPasswordTextBox.EnterPressed += (o, e) => NewPassword1TextBox?.SetFocus();

	                NewPassword1TextBox = new MirTextBox
	                {
	                    Location = new Point(178, 151),
	                    Parent = this,
	                    Password = true,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxPasswordLength,
	                    SoftKeyboardTitle = "新密码",
	                    SoftKeyboardDescription = $"仅字母/数字，{Globals.MinPasswordLength}-{Globals.MaxPasswordLength} 位",
	                };
	                NewPassword1TextBox.TextChanged += (o, e) => ValidateNewPassword1();
	                NewPassword1TextBox.EnterPressed += (o, e) => NewPassword2TextBox?.SetFocus();

	                NewPassword2TextBox = new MirTextBox
	                {
	                    Location = new Point(178, 188),
	                    Parent = this,
	                    Password = true,
	                    Size = new Size(136, 22),
	                    MaxLength = Globals.MaxPasswordLength,
	                    SoftKeyboardTitle = "确认新密码",
	                    SoftKeyboardDescription = "再次输入相同的新密码",
	                };
	                NewPassword2TextBox.TextChanged += (o, e) => ValidateNewPassword2();
	                NewPassword2TextBox.EnterPressed += (o, e) =>
	                {
	                    if (OKButton.Enabled)
	                        ChangePassword();
	                };

	                AccountIDTextBox.Text = Settings.AccountID ?? string.Empty;
	                AccountIDTextBox.SetFocus();
	            }

	            private void ValidateAccountID()
	            {
	                string value = AccountIDTextBox.Text ?? string.Empty;
	                _accountIDValid = value.Length > 0 && AccountIdReg.IsMatch(value);
	                AccountIDTextBox.BorderColour = ResolveBorderColour(_accountIDValid, value.Length);
	                RefreshConfirmButton();
	            }

	            private void ValidateCurrentPassword()
	            {
	                string value = CurrentPasswordTextBox.Text ?? string.Empty;
	                _currentPasswordValid = value.Length > 0 && PasswordReg.IsMatch(value);
	                CurrentPasswordTextBox.BorderColour = ResolveBorderColour(_currentPasswordValid, value.Length);
	                RefreshConfirmButton();
	            }

	            private void ValidateNewPassword1()
	            {
	                string value = NewPassword1TextBox.Text ?? string.Empty;
	                _newPassword1Valid = value.Length > 0 && PasswordReg.IsMatch(value);
	                NewPassword1TextBox.BorderColour = ResolveBorderColour(_newPassword1Valid, value.Length);

	                ValidateNewPassword2();
	            }

	            private void ValidateNewPassword2()
	            {
	                string value = NewPassword2TextBox.Text ?? string.Empty;
	                string newPassword1 = NewPassword1TextBox.Text ?? string.Empty;

	                _newPassword2Valid = value.Length > 0 && PasswordReg.IsMatch(value) && string.Equals(value, newPassword1, StringComparison.Ordinal);
	                NewPassword2TextBox.BorderColour = ResolveBorderColour(_newPassword2Valid, value.Length);
	                RefreshConfirmButton();
	            }

	            private static Color ResolveBorderColour(bool valid, int length)
	            {
	                if (length <= 0)
	                    return Color.Gray;

	                return valid ? Color.Green : Color.Red;
	            }

	            private void RefreshConfirmButton()
	            {
	                OKButton.Enabled = _accountIDValid && _currentPasswordValid && _newPassword1Valid && _newPassword2Valid;
	            }

	            private void ChangePassword()
	            {
	                if (!OKButton.Enabled)
	                    return;

	                OKButton.Enabled = false;

	                Network.Enqueue(new C.ChangePassword
	                {
	                    AccountID = AccountIDTextBox.Text ?? string.Empty,
	                    CurrentPassword = CurrentPasswordTextBox.Text ?? string.Empty,
	                    NewPassword = NewPassword1TextBox.Text ?? string.Empty,
	                });
	            }
	        }
 
         //public sealed class InputKeyDialog : MirImageControl
         //{
         //    public readonly MirButton KeyEscButton, KeyDelButton, KeyRandButton, KeyEnterButton;

        //    private LoginDialog _loginDialog;

        //    private List<MirButton> _buttons = new List<MirButton>();

        //    private char[] _letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        //    private char[] _numbers = "0123456789".ToCharArray();

        //    public InputKeyDialog(LoginDialog loginDialog)
        //    {
        //        _loginDialog = loginDialog;

        //        Index = 1080;
        //        Library = Libraries.Prguse;
        //        Location = new Point((Client.Settings.ScreenWidth - Size.Width) / 2 + 285, (Client.Settings.ScreenHeight - Size.Height) / 2 + 150);
        //        Visible = true;

        //        KeyEscButton = new MirButton
        //        {
        //            Text = "Esc",
        //            HoverIndex = 301,
        //            Index = 300,
        //            Library = Libraries.Title,
        //            Location = new Point(12, 12),
        //            Parent = this,
        //            PressedIndex = 302,
        //            CenterText = true
        //        };
        //        KeyEscButton.Click += (o, e) => Dispose();

        //        KeyDelButton = new MirButton
        //        {
        //            Text = "Delete",
        //            HoverIndex = 304,
        //            Index = 303,
        //            Library = Libraries.Title,
        //            Location = new Point(140, 76),
        //            Parent = this,
        //            PressedIndex = 305,
        //            CenterText = true
        //        };
        //        KeyDelButton.Click += (o, e) => SecureKeyDelete();

        //        KeyEnterButton = new MirButton
        //        {
        //            Text = "Enter",
        //            HoverIndex = 307,
        //            Index = 306,
        //            Library = Libraries.Title,
        //            Location = new Point(140, 236),
        //            Parent = this,
        //            PressedIndex = 308,
        //            CenterText = true

        //        };
        //        KeyEnterButton.Click += (o, e) =>
        //        {
        //            KeyPressEventArgs arg = new KeyPressEventArgs((char)Keys.Enter);

        //            _loginDialog.TextBox_KeyPress(o, arg);
        //        };

        //        KeyRandButton = new MirButton
        //        {
        //            Text = "Random",
        //            HoverIndex = 310,
        //            Index = 309,
        //            Library = Libraries.Title,
        //            Location = new Point(76, 236),
        //            Parent = this,
        //            PressedIndex = 311,
        //            CenterText = true
        //        };
        //        KeyRandButton.Click += (o, e) =>
        //        {
        //            _letters = new string(_letters.OrderBy(s => Guid.NewGuid()).ToArray()).ToCharArray();
        //            _numbers = new string(_numbers.OrderBy(s => Guid.NewGuid()).ToArray()).ToCharArray();

        //            UpdateKeys();
        //        };

        //        UpdateKeys();
        //    }

        //    private void DisposeKeys()
        //    {
        //        foreach (MirButton button in _buttons)
        //        {
        //            if (button != null && !button.IsDisposed) button.Dispose();
        //        }
        //    }

        //    private void UpdateKeys()
        //    {
        //        DisposeKeys();

        //        for (int i = 0; i < _numbers.Length; i++)
        //        {
        //            char key = _numbers[i];

        //            MirButton numButton = new MirButton
        //            {
        //                HoverIndex = 1082,
        //                Index = 1081,
        //                Size = new Size(32, 30),
        //                Library = Libraries.Prguse,
        //                Location = new Point(12 + (i % 6 * 32), 44 + (i / 6 * 32)),
        //                Parent = this,
        //                PressedIndex = 1083,
        //                Text = _numbers[i].ToString(),
        //                CenterText = true
        //            };
        //            numButton.Click += (o, e) => SecureKeyPress(key);

        //            _buttons.Add(numButton);
        //        }

        //        for (int i = 0; i < _letters.Length; i++)
        //        {
        //            char key = _letters[i];

        //            MirButton alphButton = new MirButton
        //            {
        //                HoverIndex = 1082,
        //                Index = 1081,
        //                Size = new Size(32, 30),
        //                Library = Libraries.Prguse,
        //                Location = new Point(12 + (i % 6 * 32), 108 + (i / 6 * 32)),
        //                Parent = this,
        //                PressedIndex = 1083,
        //                Text = _letters[i].ToString(),
        //                CenterText = true
        //            };

        //            alphButton.Click += (o, e) => SecureKeyPress(key);

        //            _buttons.Add(alphButton);
        //        }
        //    }

        //    private void SecureKeyPress(char chr)
        //    {
        //        MirTextBox currentTextBox = GetFocussedTextBox();

        //        string keyToAdd = chr.ToString();

        //        if (CMain.IsKeyLocked(Keys.CapsLock))
        //            keyToAdd = keyToAdd.ToUpper();
        //        else
        //            keyToAdd = keyToAdd.ToLower();

        //        currentTextBox.Text += keyToAdd;
        //        currentTextBox.TextBox.SelectionLength = 0;
        //        currentTextBox.TextBox.SelectionStart = currentTextBox.Text.Length;
        //    }

        //    private void SecureKeyDelete()
        //    {
        //        MirTextBox currentTextBox = GetFocussedTextBox();

        //        if (currentTextBox.TextBox.SelectionLength > 0)
        //        {
        //            currentTextBox.Text = currentTextBox.Text.Remove(currentTextBox.TextBox.SelectionStart, currentTextBox.TextBox.SelectionLength);
        //        }
        //        else if (currentTextBox.Text.Length > 0)
        //        {
        //            currentTextBox.Text = currentTextBox.Text.Remove(currentTextBox.Text.Length - 1);
        //        }

        //        currentTextBox.TextBox.SelectionStart = currentTextBox.Text.Length;
        //    }

        //    private MirTextBox GetFocussedTextBox()
        //    {
        //        if (_loginDialog.AccountIDTextBox.TextBox.Focused)
        //            return _loginDialog.AccountIDTextBox;
        //        else
        //            return _loginDialog.PasswordTextBox;
        //    }

        //    #region Disposable
        //    protected override void Dispose(bool disposing)
        //    {
        //        base.Dispose(disposing);

        //        DisposeKeys();
        //    }
        //    #endregion
        //}

        //public sealed class NewAccountDialog : MirImageControl
        //{
        //    public MirButton OKButton, CancelButton;

        //    public MirTextBox AccountIDTextBox,
        //                      Password1TextBox,
        //                      Password2TextBox,
        //                      EMailTextBox,
        //                      UserNameTextBox,
        //                      BirthDateTextBox,
        //                      QuestionTextBox,
        //                      AnswerTextBox;

        //    public MirLabel Description;

        //    private bool _accountIDValid,
        //                 _password1Valid,
        //                 _password2Valid,
        //                 _eMailValid = true,
        //                 _userNameValid = true,
        //                 _birthDateValid = true,
        //                 _questionValid = true,
        //                 _answerValid = true;


        //    public NewAccountDialog()
        //    {
        //        Index = 63;
        //        Library = Libraries.Prguse;
        //        Size = new Size();
        //        Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

        //        CancelButton = new MirButton
        //        {
        //            HoverIndex = 204,
        //            Index = 203,
        //            Library = Libraries.Title,
        //            Location = new Point(409, 425),
        //            Parent = this,
        //            PressedIndex = 205
        //        };
        //        CancelButton.Click += (o, e) => Dispose();

        //        OKButton = new MirButton
        //        {
        //            Enabled = false,
        //            HoverIndex = 201,
        //            Index = 200,
        //            Library = Libraries.Title,
        //            Location = new Point(135, 425),
        //            Parent = this,
        //            PressedIndex = 202,
        //        };
        //        OKButton.Click += (o, e) => CreateAccount();


        //        AccountIDTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 103),
        //            MaxLength = Globals.MaxAccountIDLength,
        //            Parent = this,
        //            Size = new Size(136, 18),
        //        };
        //        AccountIDTextBox.SetFocus();
        //        AccountIDTextBox.TextBox.MaxLength = Globals.MaxAccountIDLength;
        //        AccountIDTextBox.TextBox.TextChanged += AccountIDTextBox_TextChanged;
        //        AccountIDTextBox.TextBox.GotFocus += AccountIDTextBox_GotFocus;

        //        Password1TextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 129),
        //            MaxLength = Globals.MaxPasswordLength,
        //            Parent = this,
        //            Password = true,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = Globals.MaxPasswordLength },
        //        };
        //        Password1TextBox.TextBox.TextChanged += Password1TextBox_TextChanged;
        //        Password1TextBox.TextBox.GotFocus += PasswordTextBox_GotFocus;

        //        Password2TextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 155),
        //            MaxLength = Globals.MaxPasswordLength,
        //            Parent = this,
        //            Password = true,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = Globals.MaxPasswordLength },
        //        };
        //        Password2TextBox.TextBox.TextChanged += Password2TextBox_TextChanged;
        //        Password2TextBox.TextBox.GotFocus += PasswordTextBox_GotFocus;

        //        UserNameTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 189),
        //            MaxLength = 20,
        //            Parent = this,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = 20 },
        //        };
        //        UserNameTextBox.TextBox.TextChanged += UserNameTextBox_TextChanged;
        //        UserNameTextBox.TextBox.GotFocus += UserNameTextBox_GotFocus;


        //        BirthDateTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 215),
        //            MaxLength = 10,
        //            Parent = this,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = 10 },
        //        };
        //        BirthDateTextBox.TextBox.TextChanged += BirthDateTextBox_TextChanged;
        //        BirthDateTextBox.TextBox.GotFocus += BirthDateTextBox_GotFocus;

        //        QuestionTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 250),
        //            MaxLength = 30,
        //            Parent = this,
        //            Size = new Size(190, 18),
        //            TextBox = { MaxLength = 30 },
        //        };
        //        QuestionTextBox.TextBox.TextChanged += QuestionTextBox_TextChanged;
        //        QuestionTextBox.TextBox.GotFocus += QuestionTextBox_GotFocus;

        //        AnswerTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 276),
        //            MaxLength = 30,
        //            Parent = this,
        //            Size = new Size(190, 18),
        //            TextBox = { MaxLength = 30 },
        //        };
        //        AnswerTextBox.TextBox.TextChanged += AnswerTextBox_TextChanged;
        //        AnswerTextBox.TextBox.GotFocus += AnswerTextBox_GotFocus;

        //        EMailTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(226, 311),
        //            MaxLength = 50,
        //            Parent = this,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = 50 },
        //        };
        //        EMailTextBox.TextBox.TextChanged += EMailTextBox_TextChanged;
        //        EMailTextBox.TextBox.GotFocus += EMailTextBox_GotFocus;


        //        Description = new MirLabel
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(15, 340),
        //            Parent = this,
        //            Size = new Size(300, 70),
        //            Visible = false
        //        };

        //    }


        //    private void AccountIDTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");

        //        if (string.IsNullOrEmpty(AccountIDTextBox.Text) || !reg.IsMatch(AccountIDTextBox.Text))
        //        {
        //            _accountIDValid = false;
        //            AccountIDTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _accountIDValid = true;
        //            AccountIDTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void Password1TextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

        //        if (string.IsNullOrEmpty(Password1TextBox.Text) || !reg.IsMatch(Password1TextBox.Text))
        //        {
        //            _password1Valid = false;
        //            Password1TextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _password1Valid = true;
        //            Password1TextBox.BorderColour = Color.Green;
        //        }
        //        Password2TextBox_TextChanged(sender, e);
        //    }
        //    private void Password2TextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

        //        if (string.IsNullOrEmpty(Password2TextBox.Text) || !reg.IsMatch(Password2TextBox.Text) ||
        //            Password1TextBox.Text != Password2TextBox.Text)
        //        {
        //            _password2Valid = false;
        //            Password2TextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _password2Valid = true;
        //            Password2TextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void EMailTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");
        //        if (string.IsNullOrEmpty(EMailTextBox.Text))
        //        {
        //            _eMailValid = true;
        //            EMailTextBox.BorderColour = Color.Gray;
        //        }
        //        else if (!reg.IsMatch(EMailTextBox.Text) || EMailTextBox.Text.Length > 50)
        //        {
        //            _eMailValid = false;
        //            EMailTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _eMailValid = true;
        //            EMailTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void UserNameTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        if (string.IsNullOrEmpty(UserNameTextBox.Text))
        //        {
        //            _userNameValid = true;
        //            UserNameTextBox.BorderColour = Color.Gray;
        //        }
        //        else if (UserNameTextBox.Text.Length > 20)
        //        {
        //            _userNameValid = false;
        //            UserNameTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _userNameValid = true;
        //            UserNameTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void BirthDateTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        DateTime dateTime;
        //        if (string.IsNullOrEmpty(BirthDateTextBox.Text))
        //        {
        //            _birthDateValid = true;
        //            BirthDateTextBox.BorderColour = Color.Gray;
        //        }
        //        else if (!DateTime.TryParse(BirthDateTextBox.Text, out dateTime) || BirthDateTextBox.Text.Length > 10)
        //        {
        //            _birthDateValid = false;
        //            BirthDateTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _birthDateValid = true;
        //            BirthDateTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void QuestionTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        if (string.IsNullOrEmpty(QuestionTextBox.Text))
        //        {
        //            _questionValid = true;
        //            QuestionTextBox.BorderColour = Color.Gray;
        //        }
        //        else if (QuestionTextBox.Text.Length > 30)
        //        {
        //            _questionValid = false;
        //            QuestionTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _questionValid = true;
        //            QuestionTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void AnswerTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        if (string.IsNullOrEmpty(AnswerTextBox.Text))
        //        {
        //            _answerValid = true;
        //            AnswerTextBox.BorderColour = Color.Gray;
        //        }
        //        else if (AnswerTextBox.Text.Length > 30)
        //        {
        //            _answerValid = false;
        //            AnswerTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _answerValid = true;
        //            AnswerTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }

        //    private void AccountIDTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text = " Description: Account ID.\n Accepted characters: a-z A-Z 0-9.\n Length: between " +
        //                           Globals.MinAccountIDLength + " and " + Globals.MaxAccountIDLength + " characters.";
        //    }
        //    private void PasswordTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text = " Description: Password.\n Accepted characters: a-z A-Z 0-9.\n Length: between " +
        //                           Globals.MinPasswordLength + " and " + Globals.MaxPasswordLength + " characters.";
        //    }
        //    private void EMailTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text =
        //            " Description: E-Mail Address.\n Format: Example@Example.Com.\n Max Length: 50 characters.\n Optional Field.";
        //    }
        //    private void UserNameTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text =
        //            " Description: User Name.\n Accepted characters:All.\n Length: between 0 and 20 characters.\n Optional Field.";
        //    }
        //    private void BirthDateTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text =
        //            string.Format(" Description: Birth Date.\n Format: {0}.\n Length: 10 characters.\n Optional Field.",
        //                          Thread.CurrentThread.CurrentCulture.DateTimeFormat.ShortDatePattern.ToUpper());
        //    }
        //    private void QuestionTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text =
        //            " Description: Secret Question.\n Accepted characters: All.\n Length: between 0 and 30 characters.\n Optional Field.";
        //    }
        //    private void AnswerTextBox_GotFocus(object sender, EventArgs e)
        //    {
        //        Description.Visible = true;
        //        Description.Text =
        //            " Description: Secret Answer.\n Accepted characters: All.\n Length: between 0 and 30 characters.\n Optional Field.";
        //    }

        //    private void RefreshConfirmButton()
        //    {
        //        OKButton.Enabled = _accountIDValid && _password1Valid && _password2Valid && _eMailValid &&
        //                                _userNameValid && _birthDateValid && _questionValid && _answerValid;
        //    }
        //    private void CreateAccount()
        //    {
        //        OKButton.Enabled = false;

        //        Network.Enqueue(new C.NewAccount
        //        {
        //            AccountID = AccountIDTextBox.Text,
        //            Password = Password1TextBox.Text,
        //            EMailAddress = EMailTextBox.Text,
        //            BirthDate = !string.IsNullOrEmpty(BirthDateTextBox.Text)
        //                                ? DateTime.Parse(BirthDateTextBox.Text)
        //                                : DateTime.MinValue,
        //            UserName = UserNameTextBox.Text,
        //            SecretQuestion = QuestionTextBox.Text,
        //            SecretAnswer = AnswerTextBox.Text,
        //        });
        //    }

        //    public override void Show()
        //    {
        //        if (Visible) return;
        //        Visible = true;
        //        AccountIDTextBox.SetFocus();
        //    }

        //    #region Disposable
        //    protected override void Dispose(bool disposing)
        //    {
        //        if (disposing)
        //        {
        //            OKButton = null;
        //            CancelButton = null;

        //            AccountIDTextBox = null;
        //            Password1TextBox = null;
        //            Password2TextBox = null;
        //            EMailTextBox = null;
        //            UserNameTextBox = null;
        //            BirthDateTextBox = null;
        //            QuestionTextBox = null;
        //            AnswerTextBox = null;

        //            Description = null;

        //        }

        //        base.Dispose(disposing);
        //    }
        //    #endregion
        //}

        //public sealed class ChangePasswordDialog : MirImageControl
        //{
        //    public readonly MirButton OKButton,
        //                              CancelButton;

        //    public readonly MirTextBox AccountIDTextBox,
        //                               CurrentPasswordTextBox,
        //                               NewPassword1TextBox,
        //                               NewPassword2TextBox;

        //    private bool _accountIDValid,
        //                 _currentPasswordValid,
        //                 _newPassword1Valid,
        //                 _newPassword2Valid;

        //    public ChangePasswordDialog()
        //    {
        //        Index = 50;
        //        Library = Libraries.Prguse;
        //        Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

        //        CancelButton = new MirButton
        //        {
        //            HoverIndex = 111,
        //            Index = 110,
        //            Library = Libraries.Title,
        //            Location = new Point(222, 236),
        //            Parent = this,
        //            PressedIndex = 112
        //        };
        //        CancelButton.Click += (o, e) => Dispose();

        //        OKButton = new MirButton
        //        {
        //            Enabled = false,
        //            HoverIndex = 108,
        //            Index = 107,
        //            Library = Libraries.Title,
        //            Location = new Point(80, 236),
        //            Parent = this,
        //            PressedIndex = 109,
        //        };
        //        OKButton.Click += (o, e) => ChangePassword();


        //        AccountIDTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(178, 75),
        //            MaxLength = Globals.MaxAccountIDLength,
        //            Parent = this,
        //            Size = new Size(136, 18),
        //        };
        //        AccountIDTextBox.SetFocus();
        //        AccountIDTextBox.TextBox.MaxLength = Globals.MaxAccountIDLength;
        //        AccountIDTextBox.TextBox.TextChanged += AccountIDTextBox_TextChanged;

        //        CurrentPasswordTextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(178, 113),
        //            MaxLength = Globals.MaxPasswordLength,
        //            Parent = this,
        //            Password = true,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = Globals.MaxPasswordLength },
        //        };
        //        CurrentPasswordTextBox.TextBox.TextChanged += CurrentPasswordTextBox_TextChanged;

        //        NewPassword1TextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(178, 151),
        //            MaxLength = Globals.MaxPasswordLength,
        //            Parent = this,
        //            Password = true,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = Globals.MaxPasswordLength },
        //        };
        //        NewPassword1TextBox.TextBox.TextChanged += NewPassword1TextBox_TextChanged;

        //        NewPassword2TextBox = new MirTextBox
        //        {
        //            Border = true,
        //            BorderColour = Color.Gray,
        //            Location = new Point(178, 188),
        //            MaxLength = Globals.MaxPasswordLength,
        //            Parent = this,
        //            Password = true,
        //            Size = new Size(136, 18),
        //            TextBox = { MaxLength = Globals.MaxPasswordLength },
        //        };
        //        NewPassword2TextBox.TextBox.TextChanged += NewPassword2TextBox_TextChanged;

        //    }

        //    void RefreshConfirmButton()
        //    {
        //        OKButton.Enabled = _accountIDValid && _currentPasswordValid && _newPassword1Valid && _newPassword2Valid;
        //    }

        //    private void AccountIDTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinAccountIDLength + "," + Globals.MaxAccountIDLength + "}$");

        //        if (string.IsNullOrEmpty(AccountIDTextBox.Text) || !reg.IsMatch(AccountIDTextBox.Text))
        //        {
        //            _accountIDValid = false;
        //            AccountIDTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _accountIDValid = true;
        //            AccountIDTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void CurrentPasswordTextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

        //        if (string.IsNullOrEmpty(CurrentPasswordTextBox.Text) || !reg.IsMatch(CurrentPasswordTextBox.Text))
        //        {
        //            _currentPasswordValid = false;
        //            CurrentPasswordTextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _currentPasswordValid = true;
        //            CurrentPasswordTextBox.BorderColour = Color.Green;
        //        }
        //        RefreshConfirmButton();
        //    }
        //    private void NewPassword1TextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        Regex reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinPasswordLength + "," + Globals.MaxPasswordLength + "}$");

        //        if (string.IsNullOrEmpty(NewPassword1TextBox.Text) || !reg.IsMatch(NewPassword1TextBox.Text))
        //        {
        //            _newPassword1Valid = false;
        //            NewPassword1TextBox.BorderColour = Color.Red;
        //        }
        //        else
        //        {
        //            _newPassword1Valid = true;
        //            NewPassword1TextBox.BorderColour = Color.Green;
        //        }
        //        NewPassword2TextBox_TextChanged(sender, e);
        //    }
        //    private void NewPassword2TextBox_TextChanged(object sender, EventArgs e)
        //    {
        //        if (NewPassword1TextBox.Text == NewPassword2TextBox.Text)
        //        {
        //            _newPassword2Valid = _newPassword1Valid;
        //            NewPassword2TextBox.BorderColour = NewPassword1TextBox.BorderColour;
        //        }
        //        else
        //        {
        //            _newPassword2Valid = false;
        //            NewPassword2TextBox.BorderColour = Color.Red;
        //        }
        //        RefreshConfirmButton();
        //    }

        //    private void ChangePassword()
        //    {
        //        OKButton.Enabled = false;

        //        Network.Enqueue(new C.ChangePassword
        //        {
        //            AccountID = AccountIDTextBox.Text,
        //            CurrentPassword = CurrentPasswordTextBox.Text,
        //            NewPassword = NewPassword1TextBox.Text
        //        });
        //    }

        //    public override void Show()
        //    {
        //        if (Visible) return;
        //        Visible = true;
        //        AccountIDTextBox.SetFocus();
        //    }
        //}

        #region Disposable
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _background = null;
                Version = null;

                //_login = null;
                //_account = null;
                //_password = null;

                //_connectBox = null;
            }

            base.Dispose(disposing);
        }
        #endregion
    }
}
