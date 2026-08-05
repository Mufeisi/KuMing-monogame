using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using MonoShare;
using MonoShare.MirControls;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirSounds;
using C = ClientPackets;
using S = ServerPackets;
using System.Threading;
using MonoShare.Share.Extensions;
using Microsoft.Xna.Framework.Input;
namespace MonoShare.MirScenes
{
    public class SelectScene : MirScene
    {

        public MirImageControl Background, Title;
        private NewCharacterDialog _character;

        private MirMessageBox _mobilePrewarmStartGameBox;
        private bool _mobileStartGamePending;
        private int _mobileStartGamePendingCharacterIndex;

        public MirLabel ServerLabel;
        public MirAnimatedControl CharacterDisplay;
        public MirButton StartGameButton, NewCharacterButton, DeleteCharacterButton, CreditsButton, ExitGame;
        public CharacterButton[] CharacterButtons;
	        public MirLabel LastAccessLabel, LastAccessLabelLabel;
	        public List<SelectInfo> Characters = new List<SelectInfo>();
	        private int _selected;

 	        private bool _smokeTestSelectionResolved;
 	        private bool _smokeTestWaitingLibrariesLogged;
	        private bool _smokeTestCreateCharacterSent;
 	        private bool _smokeTestStartGameSent;

	        public SelectScene(List<SelectInfo> characters)
	        {
	            SoundManager.PlaySound(SoundList.SelectMusic, true);
	            Disposing += (o, e) => SoundManager.StopSound(SoundList.SelectMusic);

	            Characters = characters;
	            SortList();

	            if (Settings.LogErrors)
	                CMain.SaveLog($"进入选角界面：Characters={Characters?.Count ?? 0}");

            //KeyPress += SelectScene_KeyPress;

            Background = new MirImageControl
            {
                Index = 64,
                Library = Libraries.Prguse,
                Parent = this,
            };
            Background.Location = new Point((Settings.ScreenWidth - Background.Size.Width) / 2,
                (Settings.ScreenHeight - Background.Size.Height) / 2);

            Title = new MirImageControl
            {
                Index = 40,
                Library = Libraries.Title,
                Parent = this,
                Location = new Point((Settings.ScreenWidth - 84) / 2, 15),
            };

            ServerLabel = new MirLabel
            {
                Location = new Point(360 + Background.Location.X, 48 + Background.Location.Y),
                Parent = Background,
                Size = new Size(155, 17),
                Text = GameLanguage.GameName,
                //DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            };

            var xPoint = ((Background.Size.Width - 200) / 5);

            StartGameButton = new MirButton
            {
                Enabled = false,
                HoverIndex = 341,
                Index = 340,
                Library = Libraries.Title,
                Location = new Point(100 + (xPoint * 1) - (xPoint / 2) - 50, Background.Size.Height - 32),
                Parent = Background,
                PressedIndex = 342
            };
            StartGameButton.Click += (o, e) => StartGame();

            NewCharacterButton = new MirButton
            {
                HoverIndex = 344,
                Index = 343,
                Library = Libraries.Title,
                Location = new Point(100 + (xPoint * 2) - (xPoint / 2) - 50, Background.Size.Height - 32),
                Parent = Background,
                PressedIndex = 345,
            };
            NewCharacterButton.Click += (o, e) => _character = new NewCharacterDialog { Parent = this };

            DeleteCharacterButton = new MirButton
            {
                HoverIndex = 347,
                Index = 346,
                Library = Libraries.Title,
                Location = new Point(100 + (xPoint * 3) - (xPoint / 2) - 50, Background.Size.Height - 32),
                Parent = Background,
                PressedIndex = 348
            };
            DeleteCharacterButton.Click += (o, e) => DeleteCharacter();


            CreditsButton = new MirButton
            {
                HoverIndex = 350,
                Index = 349,
                Library = Libraries.Title,
                Location = new Point(100 + (xPoint * 4) - (xPoint / 2) - 50, Background.Size.Height - 32),
                Parent = Background,
                PressedIndex = 351
            };
            CreditsButton.Click += (o, e) =>
            {

            };

            ExitGame = new MirButton
            {
                HoverIndex = 353,
                Index = 352,
                Library = Libraries.Title,
                Location = new Point(100 + (xPoint * 5) - (xPoint / 2) - 50, Background.Size.Height - 32),
                Parent = Background,
                PressedIndex = 354
            };
            //ExitGame.Click += (o, e) => Program.Form.Close();


            CharacterDisplay = new MirAnimatedControl
            {
                Animated = true,
                AnimationCount = 16,
                AnimationDelay = 250,
                FadeIn = true,
                FadeInDelay = 75,
                FadeInRate = 0.1F,
                Index = 220,
                Library = Libraries.ChrSel,
                Location = new Point(200, 350),
                Parent = Background,
                UseOffSet = true,
                Visible = false
            };
            CharacterDisplay.AfterDraw += (o, e) =>
            {
                // if (_selected >= 0 && _selected < Characters.Count && characters[_selected].Class == MirClass.Wizard)
                Libraries.ChrSel.DrawBlend(CharacterDisplay.Index + 560, CharacterDisplay.DisplayLocationWithoutOffSet, Color.White, true);
            };

            CharacterButtons = new CharacterButton[4];
            int offset = 9;
            CharacterButtons[0] = new CharacterButton
            {
                Location = new Point(447, 122 * 1 - offset * 0),
                Parent = Background,
                Sound = SoundList.ButtonA,
            };
            CharacterButtons[0].Click += (o, e) =>
            {
                if (characters.Count <= 0) return;

                _selected = 0;
                UpdateInterface();
            };

            CharacterButtons[1] = new CharacterButton
            {
                Location = new Point(447, 122 * 2 - offset * 2),
                Parent = Background,
                Sound = SoundList.ButtonA,
            };
            CharacterButtons[1].Click += (o, e) =>
            {
                if (characters.Count <= 1) return;
                _selected = 1;
                UpdateInterface();
            };

            CharacterButtons[2] = new CharacterButton
            {
                Location = new Point(447, 122 * 3 - offset * 4),
                Parent = Background,
                Sound = SoundList.ButtonA,
            };
            CharacterButtons[2].Click += (o, e) =>
            {
                if (characters.Count <= 2) return;

                _selected = 2;
                UpdateInterface();
            };

            CharacterButtons[3] = new CharacterButton
            {
                Location = new Point(447, 122 * 4 - offset * 6),
                Parent = Background,
                Sound = SoundList.ButtonA,
            };
            CharacterButtons[3].Click += (o, e) =>
            {
                if (characters.Count <= 3) return;

                _selected = 3;
                UpdateInterface();
            };



            foreach (var item in CharacterButtons)
            {
                item.NameLabel.Location = new Point(item.Location.X + item.NameLabel.Location.X + Background.Location.X, item.Location.Y + item.NameLabel.Location.Y + Background.Location.Y);
                item.LevelLabel.Location = new Point(item.Location.X + item.LevelLabel.Location.X + Background.Location.X, item.Location.Y + item.LevelLabel.Location.Y + Background.Location.Y);
                item.ClassLabel.Location = new Point(item.Location.X + item.ClassLabel.Location.X + Background.Location.X, item.Location.Y + item.ClassLabel.Location.Y + Background.Location.Y);
            }

            LastAccessLabel = new MirLabel
            {
                Location = new Point(200 + Background.Location.X, 515 + Background.Location.Y),
                Parent = Background,
                Size = new Size(180, 21),
                //DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter,
                Border = true,
            };
	            LastAccessLabelLabel = new MirLabel
	            {
	                Location = new Point(Background.Location.X + LastAccessLabel.Location.X - 65, Background.Location.X + LastAccessLabel.Location.Y),
	                Parent = LastAccessLabel,
	                Text = "最后登录:",
                Size = new Size(100, 21),
                Border = true,
	            };
	            UpdateInterface();
	            ResolveSmokeTestSelection();
	        }

        //private void SelectScene_KeyPress(object sender, KeyPressEventArgs e)
        //{
        //    if (e.KeyChar != (char)Keys.Enter) return;
        //    if (StartGameButton.Enabled)
        //        StartGame();
        //    e.Handled = true;
        //}


        public void SortList()
        {
            if (Characters != null)
                Characters.Sort((c1, c2) => c2.LastAccess.CompareTo(c1.LastAccess));
        }


        public void StartGame()
        {
            if (!Libraries.Loaded)
            {
                //MirMessageBox message = new MirMessageBox(string.Format("请稍后, 游戏正在加载中... {0:##0}%", Libraries.Progress / (double)Libraries.Count * 100), MirMessageBoxButtons.Cancel);

                //message.BeforeDraw += (o, e) => message.Label.Text = string.Format("请稍后, 游戏正在加载中... {0:##0}%", Libraries.Progress / (double)Libraries.Count * 100);

                //message.AfterDraw += (o, e) =>
                //{
                //    if (!Libraries.Loaded) return;
                //    message.Dispose();
                //    StartGame();
                //};

                //message.Show();

                return;
            }
            StartGameButton.Enabled = false;

            int characterIndex = Characters[_selected].Index;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MobileMainHudPrewarm.EnsureStarted();

            Network.Enqueue(new C.StartGame
            {
                CharacterIndex = characterIndex
            });
        }

        public override void Process()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                MobileMainHudPrewarm.Tick();

            TryAutoCreateCharacter();
            TryAutoStartGame();
        }

	        private void ResolveSmokeTestSelection()
	        {
	            if (_smokeTestSelectionResolved)
	                return;

	            _smokeTestSelectionResolved = true;

	            string desired = Settings.SmokeTestCharacterName ?? string.Empty;
	            if (string.IsNullOrWhiteSpace(desired))
	                return;

	            if (Characters == null || Characters.Count == 0)
	                return;

	            int hit = -1;
	            for (int i = 0; i < Characters.Count; i++)
	            {
	                string name = Characters[i]?.Name ?? string.Empty;
	                if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
	                {
	                    hit = i;
	                    break;
	                }
	            }

	            if (hit < 0)
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveLog($"SmokeTest 选角未命中：CharacterName={desired}（本次列表={string.Join(",", Characters.Select(c => c?.Name ?? string.Empty))}）。");
	                return;
	            }

	            _selected = hit;
	            UpdateInterface();

	            if (Settings.LogErrors)
	                CMain.SaveLog($"SmokeTest 选角命中：CharacterName={desired}，SelectedIndex={_selected}。");
	        }

	        private void TryAutoCreateCharacter()
	        {
	            if (_smokeTestCreateCharacterSent)
	                return;

	            if (!Settings.SmokeTestAutoCreateCharacter)
	                return;

	            string desired = Settings.SmokeTestCharacterName ?? string.Empty;
	            if (string.IsNullOrWhiteSpace(desired))
	                return;

	            if (Characters != null && Characters.Any(c => string.Equals(c?.Name ?? string.Empty, desired, StringComparison.OrdinalIgnoreCase)))
	                return;

	            try
	            {
	                _smokeTestCreateCharacterSent = true;

	                if (Settings.LogErrors)
	                    CMain.SaveLog($"SmokeTest 自动建角：发送 NewCharacter（Name={desired}）。");

	                Network.Enqueue(new C.NewCharacter
	                {
	                    Name = desired,
	                    Class = MirClass.Warrior,
	                    Gender = MirGender.Male,
	                });
	            }
	            catch (System.Exception ex)
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveError($"SmokeTest 自动建角失败：{ex}");
	            }
	        }

	        private void TryAutoStartGame()
	        {
	            if (_smokeTestStartGameSent)
	                return;

	            if (!Settings.SmokeTestAutoStartGame)
	                return;

	            if (Characters == null || Characters.Count == 0)
	                return;

	            if (!Libraries.Loaded)
	            {
	                if (!_smokeTestWaitingLibrariesLogged && Settings.LogErrors)
	                {
	                    _smokeTestWaitingLibrariesLogged = true;
	                    CMain.SaveLog("SmokeTest 等待资源库加载完成（Libraries.Loaded=false）。");
	                }
	                return;
	            }

	            if (_selected < 0 || _selected >= Characters.Count)
	                _selected = 0;

	            try
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveLog($"SmokeTest 自动进图：发送 StartGame（SelectedIndex={_selected}, CharacterIndex={Characters[_selected].Index}）。");

	                _smokeTestStartGameSent = true;
	                StartGame();
	            }
	            catch (System.Exception ex)
	            {
	                if (Settings.LogErrors)
	                    CMain.SaveError($"SmokeTest 自动进图失败：{ex}");
	            }
	        }
        public override void ProcessPacket(Packet p)
        {
            switch (p.Index)
            {
                case (short)ServerPacketIds.NewCharacter:
                    NewCharacter((S.NewCharacter)p);
                    break;
                case (short)ServerPacketIds.NewCharacterSuccess:
                    NewCharacter((S.NewCharacterSuccess)p);
                    break;
                case (short)ServerPacketIds.DeleteCharacter:
                    DeleteCharacter((S.DeleteCharacter)p);
                    break;
                case (short)ServerPacketIds.DeleteCharacterSuccess:
                    DeleteCharacter((S.DeleteCharacterSuccess)p);
                    break;
                case (short)ServerPacketIds.StartGame:
                    StartGame((S.StartGame)p);
                    break;
                case (short)ServerPacketIds.StartGameBanned:
                    StartGame((S.StartGameBanned)p);
                    break;
                case (short)ServerPacketIds.StartGameDelay:
                    StartGame((S.StartGameDelay)p);
                    break;
                default:
                    base.ProcessPacket(p);
                    break;
            }
        }

        private void NewCharacter(S.NewCharacter p)
        {
            if (_character != null && !_character.IsDisposed)
                _character.OKButton.Enabled = true;

            if (_character == null || _character.IsDisposed)
            {
                if (_smokeTestCreateCharacterSent && Settings.LogErrors)
                    CMain.SaveError($"SmokeTest 建角失败：Result={p.Result}（0=禁用 1=名字格式 2=性别 3=职业 4=数量上限 5=重名）。");
                return;
            }

            switch (p.Result)
            {
                case 0:
                    //MirMessageBox.Show("当前已禁用创建新角色.");
                    _character.Dispose();
                    break;
                case 1:
                    //MirMessageBox.Show("您的角色名填项有误.");
                    _character.NameTextBox?.SetFocus();
                    break;
                case 2:
                    //MirMessageBox.Show("您选择的性别不存在.\n 联系游戏管理员帮助.");
                    break;
                case 3:
                    //MirMessageBox.Show("您选择的职业类型不存在.\n 联系游戏管理员帮助.");
                    break;
                case 4:
                    //MirMessageBox.Show("您不错创建超过" + Globals.MaxCharacterCount + "个角色.");
                    _character.Dispose();
                    break;
                case 5:
                    //MirMessageBox.Show("角色名称已存在.");
                    _character.NameTextBox?.SetFocus();
                    break;
            }


        }
        private void NewCharacter(S.NewCharacterSuccess p)
        {
            if (_character != null && !_character.IsDisposed)
                _character.Dispose();
            //MirMessageBox.Show("您的角色创建成功.");

            Characters.Insert(0, p.CharInfo);
            _selected = 0;
            UpdateInterface();

            if (_smokeTestCreateCharacterSent && Settings.LogErrors)
                CMain.SaveLog($"SmokeTest 建角成功：Name={p.CharInfo?.Name ?? string.Empty} Index={p.CharInfo?.Index ?? 0}。");
        }

        private void DeleteCharacter()
        {
            if (_selected < 0 || _selected >= Characters.Count) return;

            MirMessageBox message = new MirMessageBox(string.Format("您确定要删除当前角色：{0}?", Characters[_selected].Name), MirMessageBoxButtons.YesNo);
            int index = Characters[_selected].Index;

            //message.YesButton.Click += (o, e) =>
            //{
            //    DeleteCharacterButton.Enabled = false;
            //    Network.Enqueue(new C.DeleteCharacter { CharacterIndex = index });
            //};

            message.Show();
        }

        private void DeleteCharacter(S.DeleteCharacter p)
        {
            DeleteCharacterButton.Enabled = true;
            switch (p.Result)
            {
                case 0:
                    //MirMessageBox.Show("删除角色失败.");
                    break;
                case 1:
                    //MirMessageBox.Show("您选择的角色不存在.\n 联系游戏管理员帮助.");
                    break;
            }
        }
        private void DeleteCharacter(S.DeleteCharacterSuccess p)
        {
            DeleteCharacterButton.Enabled = true;
            //MirMessageBox.Show("您的角色删除成功.");

            for (int i = 0; i < Characters.Count; i++)
                if (Characters[i].Index == p.CharacterIndex)
                {
                    Characters.RemoveAt(i);
                    break;
                }

            UpdateInterface();
        }

        private void StartGame(S.StartGameDelay p)
        {
            StartGameButton.Enabled = true;

            long time = CMain.Time + p.Milliseconds;

            //MirMessageBox message = new MirMessageBox(string.Format("您不能登录当前角色{0}秒.", Math.Ceiling(p.Milliseconds / 1000M)));

            //message.BeforeDraw += (o, e) => message.Label.Text = string.Format("您不能登录当前角色{0}秒.", Math.Ceiling((time - CMain.Time) / 1000M));


            //message.AfterDraw += (o, e) =>
            //{
            //    if (CMain.Time <= time) return;
            //    message.Dispose();
            //    StartGame();
            //};

            //message.Show();
        }
        public void StartGame(S.StartGameBanned p)
        {
            StartGameButton.Enabled = true;

            TimeSpan d = p.ExpiryDate - CMain.Now;
            //MirMessageBox.Show(string.Format("当前账号已被禁止.\n\n原因: {0}\n过期时间: {1}\n持续时间: {2:#,##0} Hours, {3} Minutes, {4} Seconds", p.Reason,
            //                                 p.ExpiryDate, Math.Floor(d.TotalHours), d.Minutes, d.Seconds));
        }
	        public void StartGame(S.StartGame p)
	        {
	            StartGameButton.Enabled = true;

	            switch (p.Result)
	            {
                case 0:
                    //MirMessageBox.Show("当前已禁用启动游戏.");
                    break;
                case 1:
                    //MirMessageBox.Show("您还没有登录.");
                    break;
                case 2:
                    //MirMessageBox.Show("找不到您的角色.");
                    break;
                case 3:
                    //MirMessageBox.Show("未找到活动地图和/或起点.");
                    break;
	                case 4:

	                    if (p.Resolution < Settings.Resolution || Settings.Resolution == 0) Settings.Resolution = p.Resolution;

                    switch (Settings.Resolution)
                    {
                        default:
                        case 800:
                            Settings.Resolution = 800;
                            //CMain.SetResolution(800, 600);
                            break;
                        case 1024:
                            //CMain.SetResolution(1024, 768);
                            break;
                        case 1280:
                            //CMain.SetResolution(1280, 800);
                            break;
                        case 1366:
                            //CMain.SetResolution(1366, 768);
                            break;
                        case 1920:
                            //CMain.SetResolution(1920, 1080);
                            break;
                    }

	                    if (Settings.LogErrors)
	                        CMain.SaveLog("进入地图：StartGame=OK，创建 GameScene。");

                        if (Settings.LogErrors && Environment.OSVersion.Platform != PlatformID.Win32NT)
                            CMain.SaveLog("进入地图：准备 new GameScene（v20260328-asynclog）。");
 
	                    ActiveScene = new GameScene();

                        if (Settings.LogErrors && Environment.OSVersion.Platform != PlatformID.Win32NT)
                            CMain.SaveLog("进入地图：GameScene 构造已返回，准备释放 SelectScene。");

	                    Dispose();

                        if (Settings.LogErrors && Environment.OSVersion.Platform != PlatformID.Win32NT)
                            CMain.SaveLog("进入地图：SelectScene 已释放。");
	                    break;
	            }
	        }
        private void UpdateInterface()
        {
            for (int i = 0; i < CharacterButtons.Length; i++)
            {
                CharacterButtons[i].Selected = i == _selected;
                CharacterButtons[i].Update(i >= Characters.Count ? null : Characters[i]);
            }

            if (_selected >= 0 && _selected < Characters.Count)
            {
                CharacterDisplay.Visible = true;
                //CharacterDisplay.Index = ((byte)Characters[_selected].Class + 1) * 20 + (byte)Characters[_selected].Gender * 280; 

                switch ((MirClass)Characters[_selected].Class)
                {
                    case MirClass.Warrior:
                        CharacterDisplay.Index = (byte)Characters[_selected].Gender == 0 ? 20 : 300; //220 : 500;
                        break;
                    case MirClass.Wizard:
                        CharacterDisplay.Index = (byte)Characters[_selected].Gender == 0 ? 40 : 320; //240 : 520;
                        break;
                    case MirClass.Taoist:
                        CharacterDisplay.Index = (byte)Characters[_selected].Gender == 0 ? 60 : 340; //260 : 540;
                        break;
                    case MirClass.Assassin:
                        CharacterDisplay.Index = (byte)Characters[_selected].Gender == 0 ? 80 : 360; //280 : 560;
                        break;
                    case MirClass.Archer:
                        CharacterDisplay.Index = (byte)Characters[_selected].Gender == 0 ? 100 : 140; //160 : 180;
                        break;
                }

                LastAccessLabel.Text = Characters[_selected].LastAccess == DateTime.MinValue ? "Never" : Characters[_selected].LastAccess.ToString();
                LastAccessLabel.Visible = true;
                LastAccessLabelLabel.Visible = true;
                StartGameButton.Enabled = true;
            }
            else
            {
                CharacterDisplay.Visible = false;
                LastAccessLabel.Visible = false;
                LastAccessLabelLabel.Visible = false;
                StartGameButton.Enabled = false;
            }
        }


        #region Disposable
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Background = null;
                _character = null;

                ServerLabel = null;
                CharacterDisplay = null;
                StartGameButton = null;
                NewCharacterButton = null;
                DeleteCharacterButton = null;
                CreditsButton = null;
                ExitGame = null;
                CharacterButtons = null;
                LastAccessLabel = null; LastAccessLabelLabel = null;
                Characters = null;
                _selected = 0;
            }

            base.Dispose(disposing);
        }
        #endregion
        public sealed class NewCharacterDialog : MirImageControl
        {
            private static readonly Regex Reg = new Regex(@"^[A-Za-z0-9]{" + Globals.MinCharacterNameLength + "," + Globals.MaxCharacterNameLength + "}$");

            public MirImageControl TitleLabel;
            public MirAnimatedControl CharacterDisplay;

            public MirButton OKButton,
                             CancelButton,
                             WarriorButton,
                             WizardButton,
                             TaoistButton,
                             AssassinButton,
                             ArcherButton,
                             MaleButton,
                             FemaleButton;

            public MirTextBox NameTextBox;

            public MirLabel Description;

            private MirClass _class;
            private MirGender _gender;
            private Rectangle _layoutSafeArea;

            #region Descriptions
            public const string WarriorDescription = "战士是个拥有强大力量和体力的职业,他们不会在战斗中被轻易地杀死,而且能够使用各种重型武器和装甲,所以战士们更喜欢基于物理伤害的近身攻击,尽管他们的远程攻击力薄弱,但是专门为战士开发的各种装备弥补了他们在面对远程战斗时的弱点。";
            //"Warriors are a class of great strength and vitality. They are not easily killed in battle and have the advantage of being able to use" +
            //" a variety of heavy weapons and Armour. Therefore, Warriors favor attacks that are based on melee physical damage. They are weak in ranged" +
            //" attacks, however the variety of equipment that are developed specifically for Warriors complement their weakness in ranged combat.";

            public const string WizardDescription = "法师是个力量和体力都很低的职业,但是他们能够使用强大的法术作为弥补,他们的攻击法术可以非常有效的打击敌人,但由于释放这些法术需要花费时间,所以身体虚弱的法师们总是时刻警惕着并与敌人保持距离。";
            //"Wizards are a class of low strength and stamina, but have the ability to use powerful spells. Their offensive spells are very effective, but" +
            //" because it takes time to cast these spells, they're likely to leave themselves open for enemy's attacks. Therefore, the physically weak wizards" +
            //" must aim to attack their enemies from a safe distance.";

            public const string TaoistDescription = "道士在天文学,医药学等方面都有很深的造诣.相对于直接与敌人战斗,他们更善于支持协助队友.道士们能够召唤出强大的生物,而且他们对魔法有着很高的抵抗力,所以道士是一个攻守兼备的职业。";
            //"Taoists are well disciplined in the study of Astronomy, Medicine, and others aside from Mu-Gong. Rather then directly engaging the enemies, their" +
            //" specialty lies in assisting their allies with support. Taoists can summon powerful creatures and have a high resistance to magic, and is a class" +
            //" with well balanced offensive and defensive abilities.";

            public const string AssassinDescription = "刺客来自一个神秘的组织,没有人了解他们的过去.他们可以隐藏自己,并在他人看不见的情况下发动攻击,所以他们更善于快速致死敌人.因力量和体力有限,刺客们通常会刻意避免陷入众多敌人的包围。";
            //"Assassins are members of a secret organization and their history is relatively unknown. They're capable of hiding themselves and performing attacks" +
            //" while being unseen by others, which naturally makes them excellent at making fast kills. It is necessary for them to avoid being in battles with" +
            //" multiple enemies due to their weak vitality and strength.";

            public const string ArcherDescription = "弓手是个攻击精准且力量强大的职业,他们在一定距离外使用强力的弓箭技能造成巨大伤害.和法师类似,虽然弓手们往往会刻意与敌人保持距离并依靠敏锐的本能预判躲避攻击,但他们的实力和致命打击早已让敌人闻风丧胆。";
            //"Archers are a class of great accuracy and strength, using their powerful skills with bows to deal extraordinary damage from range. Much like" +
            //" wizards, they rely on their keen instincts to dodge oncoming attacks as they tend to leave themselves open to frontal attacks. However, their" +
            //" physical prowess and deadly aim allows them to instil fear into anyone they hit.";

            #endregion

            public NewCharacterDialog()
            {
                Index = 73;
                Library = Libraries.Prguse;
                Modal = true;

                TitleLabel = new MirImageControl
                {
                    Index = 20,
                    Library = Libraries.Title,
                    Location = new Point(206, 11),
                    Parent = this,
                };

                CancelButton = new MirButton
                {
                    HoverIndex = 281,
                    Index = 280,
                    Library = Libraries.Title,
                    Location = new Point(425, 425),
                    Parent = this,
                    PressedIndex = 282
                };
                CancelButton.Click += (o, e) => Dispose();


                OKButton = new MirButton
                {
                    Enabled = false,
                    HoverIndex = 361,
                    Index = 360,
                    Library = Libraries.Title,
                    Location = new Point(160, 425),
                    Parent = this,
                    PressedIndex = 362,
                };
                OKButton.Click += (o, e) => CreateCharacter();

                NameTextBox = new MirTextBox
                {
                    Location = new Point(325, 268),
                    Parent = this,
                    Size = new Size(240, 22),
                    MaxLength = Globals.MaxCharacterNameLength,
                    SoftKeyboardTitle = "角色名",
                    SoftKeyboardDescription = $"仅字母数字，{Globals.MinCharacterNameLength}-{Globals.MaxCharacterNameLength} 位",
                };
                NameTextBox.TextChanged += CharacterNameTextBox_TextChanged;
                NameTextBox.EnterPressed += (o, e) =>
                {
                    if (OKButton.Enabled)
                        CreateCharacter();
                };

                CharacterDisplay = new MirAnimatedControl
                {
                    Animated = true,
                    AnimationCount = 16,
                    AnimationDelay = 250,
                    Index = 20,
                    Library = Libraries.ChrSel,
                    Location = new Point(120, 250),
                    Parent = this,
                    UseOffSet = true,
                };
                CharacterDisplay.AfterDraw += (o, e) =>
                {
                    if (_class == MirClass.Wizard)
                        Libraries.ChrSel.DrawBlend(CharacterDisplay.Index + 560, CharacterDisplay.DisplayLocationWithoutOffSet, Color.White, true);
                };


                WarriorButton = new MirButton
                {
                    HoverIndex = 2427,
                    Index = 2427,
                    Library = Libraries.Prguse,
                    Location = new Point(323, 296),
                    Parent = this,
                    PressedIndex = 2428,
                    Sound = SoundList.ButtonA,
                };
                WarriorButton.Click += (o, e) =>
                {
                    _class = MirClass.Warrior;
                    UpdateInterface();
                };


                WizardButton = new MirButton
                {
                    HoverIndex = 2430,
                    Index = 2429,
                    Library = Libraries.Prguse,
                    Location = new Point(373, 296),
                    Parent = this,
                    PressedIndex = 2431,
                    Sound = SoundList.ButtonA,
                };
                WizardButton.Click += (o, e) =>
                {
                    _class = MirClass.Wizard;
                    UpdateInterface();
                };


                TaoistButton = new MirButton
                {
                    HoverIndex = 2433,
                    Index = 2432,
                    Library = Libraries.Prguse,
                    Location = new Point(423, 296),
                    Parent = this,
                    PressedIndex = 2434,
                    Sound = SoundList.ButtonA,
                };
                TaoistButton.Click += (o, e) =>
                {
                    _class = MirClass.Taoist;
                    UpdateInterface();
                };

                AssassinButton = new MirButton
                {
                    HoverIndex = 2436,
                    Index = 2435,
                    Library = Libraries.Prguse,
                    Location = new Point(473, 296),
                    Parent = this,
                    PressedIndex = 2437,
                    Sound = SoundList.ButtonA,
                };
                AssassinButton.Click += (o, e) =>
                {
                    _class = MirClass.Assassin;
                    UpdateInterface();
                };

                ArcherButton = new MirButton
                {
                    HoverIndex = 2439,
                    Index = 2438,
                    Library = Libraries.Prguse,
                    Location = new Point(523, 296),
                    Parent = this,
                    PressedIndex = 2440,
                    Sound = SoundList.ButtonA,
                };
                ArcherButton.Click += (o, e) =>
                {
                    _class = MirClass.Archer;
                    UpdateInterface();
                };


                MaleButton = new MirButton
                {
                    HoverIndex = 2421,
                    Index = 2421,
                    Library = Libraries.Prguse,
                    Location = new Point(323, 343),
                    Parent = this,
                    PressedIndex = 2422,
                    Sound = SoundList.ButtonA,
                };
                MaleButton.Click += (o, e) =>
                {
                    _gender = MirGender.Male;
                    UpdateInterface();
                };

                FemaleButton = new MirButton
                {
                    HoverIndex = 2424,
                    Index = 2423,
                    Library = Libraries.Prguse,
                    Location = new Point(373, 343),
                    Parent = this,
                    PressedIndex = 2425,
                    Sound = SoundList.ButtonA,
                };
                FemaleButton.Click += (o, e) =>
                {
                    _gender = MirGender.Female;
                    UpdateInterface();
                };

                Description = new MirLabel
                {
                    Border = true,
                    Location = new Point(279, 70),
                    Parent = this,
                    Size = new Size(278, 170),
                    Text = WarriorDescription,
                };
            }

            protected override void OnParentChanged()
            {
                base.OnParentChanged();

                if (Parent == null)
                    return;

                NameTextBox?.SetFocus();
                EnsureLayout();
                CharacterNameTextBox_TextChanged(NameTextBox, EventArgs.Empty);
            }

            public override void Event()
            {
                EnsureLayout();
                base.Event();
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

            //private void TextBox_KeyPress(object sender, KeyPressEventArgs e)
            //{
            //    if (sender == null) return;
            //    if (e.KeyChar != (char)Keys.Enter) return;
            //    e.Handled = true;

            //    if (OKButton.Enabled)
            //        OKButton.InvokeMouseClick(null);
            //}
            private void CharacterNameTextBox_TextChanged(object sender, EventArgs e)
            {
                if (NameTextBox == null || OKButton == null)
                    return;

                if (string.IsNullOrEmpty(NameTextBox.Text))
                {
                    OKButton.Enabled = false;
                    NameTextBox.Border = false;
                }
                else if (!Reg.IsMatch(NameTextBox.Text))
                {
                    OKButton.Enabled = false;
                    NameTextBox.Border = true;
                    NameTextBox.BorderColour = Color.Red;
                }
                else
                {
                    OKButton.Enabled = true;
                    NameTextBox.Border = true;
                    NameTextBox.BorderColour = Color.Green;
                }
            }

            private void CreateCharacter()
            {
                OKButton.Enabled = false;

                Network.Enqueue(new C.NewCharacter
                {
                    Name = NameTextBox.Text,
                    Class = _class,
                    Gender = _gender
                });
            }

            private void UpdateInterface()
            {
                MaleButton.Index = 2420;
                FemaleButton.Index = 2423;

                WarriorButton.Index = 2426;
                WizardButton.Index = 2429;
                TaoistButton.Index = 2432;
                AssassinButton.Index = 2435;
                ArcherButton.Index = 2438;

                switch (_gender)
                {
                    case MirGender.Male:
                        MaleButton.Index = 2421;
                        break;
                    case MirGender.Female:
                        FemaleButton.Index = 2424;
                        break;
                }

                switch (_class)
                {
                    case MirClass.Warrior:
                        WarriorButton.Index = 2427;
                        Description.Text = WarriorDescription;
                        CharacterDisplay.Index = (byte)_gender == 0 ? 20 : 300; //220 : 500;
                        break;
                    case MirClass.Wizard:
                        WizardButton.Index = 2430;
                        Description.Text = WizardDescription;
                        CharacterDisplay.Index = (byte)_gender == 0 ? 40 : 320; //240 : 520;
                        break;
                    case MirClass.Taoist:
                        TaoistButton.Index = 2433;
                        Description.Text = TaoistDescription;
                        CharacterDisplay.Index = (byte)_gender == 0 ? 60 : 340; //260 : 540;
                        break;
                    case MirClass.Assassin:
                        AssassinButton.Index = 2436;
                        Description.Text = AssassinDescription;
                        CharacterDisplay.Index = (byte)_gender == 0 ? 80 : 360; //280 : 560;
                        break;
                    case MirClass.Archer:
                        ArcherButton.Index = 2439;
                        Description.Text = ArcherDescription;
                        CharacterDisplay.Index = (byte)_gender == 0 ? 100 : 140; //160 : 180;
                        break;
                }

                //CharacterDisplay.Index = ((byte)_class + 1) * 20 + (byte)_gender * 280;
            }
        }
        public sealed class CharacterButton : MirImageControl
        {
            public MirLabel NameLabel, LevelLabel, ClassLabel;
            public bool Selected;

            public CharacterButton()
            {
                Index = 44; //45 locked
                Library = Libraries.Prguse;
                Sound = SoundList.ButtonA;

                NameLabel = new MirLabel
                {
                    Location = new Point(107, 9),
                    Parent = this,
                    NotControl = true,
                    Size = new Size(170, 18)
                };

                LevelLabel = new MirLabel
                {
                    Location = new Point(107, 28),
                    Parent = this,
                    NotControl = true,
                    Size = new Size(30, 18)
                };

                ClassLabel = new MirLabel
                {
                    Location = new Point(178, 28),
                    Parent = this,
                    NotControl = true,
                    Size = new Size(100, 18)
                };
            }
            public override void Event()
            {
                if (this.GetType().Name == "CharacterButton")
                {
                    if (DisplayRectangle.Contains(CMain.currentMouseState.Position.ToDrawPoint()))
                    {
                        if (CMain.currentMouseState.LeftButton == ButtonState.Pressed && CMain.previousMouseState.LeftButton == ButtonState.Released)
                        {
                            ActiveControl = this;
                            InvokeMouseClick(EventArgs.Empty);
                        }
                        else
                        {
                            MouseControl = this;
                            ActiveControl = null;
                        }
                    }
                    else if (MouseControl == this)
                    {
                        MouseControl = null;
                        ActiveControl = null;
                    }
                }
            }
            public void Update(SelectInfo info)
            {
                if (info == null)
                {
                    Index = 44;
                    Library = Libraries.Prguse;
                    NameLabel.Text = string.Empty;
                    LevelLabel.Text = string.Empty;
                    ClassLabel.Text = string.Empty;

                    NameLabel.Visible = false;
                    LevelLabel.Visible = false;
                    ClassLabel.Visible = false;

                    return;
                }

                Library = Libraries.Title;

                Index = 660 + (byte)info.Class;

                if (Selected) Index += 5;


                NameLabel.Text = info.Name;
                LevelLabel.Text = info.Level.ToString();
                ClassLabel.Text = info.Class.ToString();

                NameLabel.Visible = true;
                LevelLabel.Visible = true;
                ClassLabel.Visible = true;
            }
        }
    }
}
