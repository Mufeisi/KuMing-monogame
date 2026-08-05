using System.Drawing;
using System.Linq;
using MonoShare.MirGraphics;
using MonoShare.MirNetwork;
using MonoShare.MirScenes;
using S = ServerPackets;
using C = ClientPackets;
using System;
using Microsoft.Xna.Framework.Input;

namespace MonoShare.MirControls
{
    public abstract class MirScene : MirControl
    {
        public static MirScene ActiveScene;

        private static ButtonState _buttons;
        private static long _lastClickTime;
        private static MirControl _clickedControl;

        protected MirScene()
        {
            //DrawControlTexture = true;
            //BackColour = Color.Magenta;
            //Size = new Size(Settings.ScreenWidth, Settings.ScreenHeight);
        }

        //public override sealed Size Size
        //{
        //    get { return base.Size; }
        //    set { base.Size = value; }
        //}

        //public override void Draw()
        //{
        //    if (IsDisposed || !Visible)
        //        return;

        //    OnBeforeShown();

        //    DrawControl();

        //    if (CMain.DebugBaseLabel != null && !CMain.DebugBaseLabel.IsDisposed)
        //        CMain.DebugBaseLabel.Draw();

        //    if (CMain.HintBaseLabel != null && !CMain.HintBaseLabel.IsDisposed)
        //        CMain.HintBaseLabel.Draw();

        //    OnShown();
        //}

        //protected override void CreateTexture()
        //{
        //    if (ControlTexture != null && !ControlTexture.Disposed && Size != TextureSize)
        //        ControlTexture.Dispose();

        //    if (ControlTexture == null || ControlTexture.Disposed)
        //    {
        //        DXManager.ControlList.Add(this);
        //        ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
        //        TextureSize = Size;
        //    }
        //    Surface oldSurface = DXManager.CurrentSurface;
        //    Surface surface = ControlTexture.GetSurfaceLevel(0);
        //    DXManager.SetSurface(surface);

        //    DXManager.Device.Clear(ClearFlags.Target, BackColour, 0, 0);

        //    BeforeDrawControl();
        //    DrawChildControls();
        //    AfterDrawControl();

        //    DXManager.Sprite.Flush();


        //    DXManager.SetSurface(oldSurface);
        //    TextureValid = true;
        //    surface.Dispose();
        //}

        //public override void OnMouseDown(MouseEventArgs e)
        //{
        //    if (!Enabled)
        //        return;

        //    if (MouseControl != null && MouseControl != this)
        //        MouseControl.OnMouseDown(e);
        //    else
        //        base.OnMouseDown(e);
        //}
        //public override void OnMouseUp(MouseEventArgs e)
        //{
        //    if (!Enabled)
        //        return;
        //    if (MouseControl != null && MouseControl != this)
        //        MouseControl.OnMouseUp(e);
        //    else
        //        base.OnMouseUp(e);
        //}
        //public override void OnMouseMove(MouseEventArgs e)
        //{
        //    if (!Enabled)
        //        return;

        //    if (MouseControl != null && MouseControl != this && MouseControl.Moving)
        //        MouseControl.OnMouseMove(e);
        //    else
        //        base.OnMouseMove(e);
        //}
        //public override void OnMouseWheel(MouseEventArgs e)
        //{
        //    if (!Enabled)
        //        return;

        //    if (MouseControl != null && MouseControl != this)
        //        MouseControl.OnMouseWheel(e);
        //    else
        //        base.OnMouseWheel(e);
        //}

        public override void OnMouseClick(EventArgs e)
        {
            if (!Enabled)
                return;
            if (_buttons == CMain.currentMouseState.LeftButton)
            {
                if (_lastClickTime + 5 >= CMain.Time)
                {
                    OnMouseDoubleClick(e);
                    return;
                }
            }
            else
                _lastClickTime = 0;

            if (ActiveControl != null && ActiveControl.IsMouseOver(CMain.MPoint) && ActiveControl != this)
                ActiveControl.OnMouseClick(e);
            else
                base.OnMouseClick(e);

            _clickedControl = ActiveControl;

            _lastClickTime = CMain.Time;
            _buttons = CMain.currentMouseState.LeftButton;
        }

        //public override void OnMouseDoubleClick(MouseEventArgs e)
        //{
        //    if (!Enabled)
        //        return;
        //    _lastClickTime = 0;
        //    _buttons = MouseButtons.None;

        //    if (ActiveControl != null && ActiveControl.IsMouseOver(CMain.MPoint) && ActiveControl != this)
        //    {
        //        if (ActiveControl == _clickedControl)
        //            ActiveControl.OnMouseDoubleClick(e);
        //        else
        //            ActiveControl.OnMouseClick(e);
        //    }
        //    else
        //    {
        //        if (ActiveControl == _clickedControl)
        //            base.OnMouseDoubleClick(e);
        //        else
        //            base.OnMouseClick(e);
        //    }
        //}

        //public override void Redraw()
        //{
        //    TextureValid = false;
        //}

        public virtual void ProcessPacket(Packet p)
        {
            switch (p.Index)
            {
                case (short)ServerPacketIds.Disconnect: // Disconnected
                    string disconnectMessage = GetDisconnectMessage((S.Disconnect)p);
                    Network.Disconnect();
                    ReturnToLoginScene(disconnectMessage);
                    break;
                case (short)ServerPacketIds.NewItemInfo:
                    NewItemInfo((S.NewItemInfo)p);
                    break;
                case (short)ServerPacketIds.NewChatItem:
                    NewChatItem((S.NewChatItem)p);
                    break;
                case (short)ServerPacketIds.NewQuestInfo:
                    NewQuestInfo((S.NewQuestInfo)p);
                    break;
                case (short)ServerPacketIds.NewRecipeInfo:
                    NewRecipeInfo((S.NewRecipeInfo)p);
                    break;
            }
        }

        private void NewItemInfo(S.NewItemInfo info)
        {
            GameScene.ItemInfoList.Add(info.Info);
        }

        private void NewChatItem(S.NewChatItem p)
        {
            if (GameScene.ChatItemList.Any(x => x.UniqueID == p.Item.UniqueID)) return;

            GameScene.Bind(p.Item);
            GameScene.ChatItemList.Add(p.Item);
        }

        private void NewQuestInfo(S.NewQuestInfo info)
        {
            GameScene.QuestInfoList.Add(info.Info);
        }

        private void NewRecipeInfo(S.NewRecipeInfo info)
        {
            GameScene.RecipeInfoList.Add(info.Info);

            GameScene.Bind(info.Info.Item);

            for (int j = 0; j < info.Info.Tools.Count; j++)
                GameScene.Bind(info.Info.Tools[j]);

            for (int j = 0; j < info.Info.Ingredients.Count; j++)
                GameScene.Bind(info.Info.Ingredients[j]);
        }

        private static string GetDisconnectMessage(S.Disconnect p)
        {
            string message;
            switch (p.Reason)
            {
                case 0:
                    message = GameLanguage.ShuttingDown;
                    break;
                case 1:
                    message = "断开连接：你的账号被其他人登录。";
                    break;
                case 2:
                    message = "断开连接：数据包错误。";
                    break;
                case 3:
                    message = "断开连接：服务器崩溃。";
                    break;
                case 4:
                    message = "断开连接：被管理员踢下线。";
                    break;
                case 5:
                    message = "断开连接：已达到最大连接数。";
                    break;
                default:
                    message = "断开连接：未知原因。";
                    break;
            }

            GameScene.LogTime = 0;
            return message;
        }

        public static void ReturnToLoginScene(string message = null)
        {
            try
            {
                MirScene current = ActiveScene;

                if (current is LoginScene)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                        MirMessageBox.Show(message);
                    return;
                }

                ActiveScene = new LoginScene();

                if (current != null && !current.IsDisposed)
                    current.Dispose();

                if (!string.IsNullOrWhiteSpace(message))
                    MirMessageBox.Show(message);
            }
            catch
            {
            }
        }

        public abstract void Process();

        #region Disposable

        protected override void Dispose(bool disposing)
        {

            base.Dispose(disposing);

            if (!disposing) return;

            if (ActiveScene == this) ActiveScene = null;

            _buttons = 0;
            _lastClickTime = 0;
            _clickedControl = null;
        }

        #endregion
    }
}
