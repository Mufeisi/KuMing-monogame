using Client.MirScenes;
using Client.MirControls;

namespace Client.UI
{
    public static class UIManager
    {
        public static UIProfileId GetCurrentProfileId()
        {
            return TryParseProfileId(Settings.UIProfileId, out var id) ? id : UIProfileId.Classic;
        }

        public static bool TryParseProfileId(string? value, out UIProfileId id)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                id = UIProfileId.Classic;
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out id);
        }

        public static UIProfile GetProfile(UIProfileId id)
        {
            switch (id)
            {
                case UIProfileId.Compact:
                    return new UIProfile
                    {
                        Id = UIProfileId.Compact,
                        ChatPlacement = UIProfileChatPlacement.BottomRight,
                        ChatWindowSize = 0,
                        TransparentChat = true,
                    };
                case UIProfileId.Mobile:
                    return new UIProfile
                    {
                        Id = UIProfileId.Mobile,
                        ChatPlacement = UIProfileChatPlacement.BottomLeft,
                        ChatWindowSize = 2,
                        TransparentChat = false,
                    };
                case UIProfileId.Custom:
                    return new UIProfile { Id = UIProfileId.Custom };
                default:
                    return new UIProfile
                    {
                        Id = UIProfileId.Classic,
                        ChatPlacement = UIProfileChatPlacement.RelativeToMainDialog,
                    };
            }
        }

        public static void ApplyCurrentProfile(GameScene scene, bool save)
        {
            Apply(scene, GetCurrentProfileId(), save);
        }

        public static void Apply(GameScene scene, UIProfileId id, bool save)
        {
            if (scene == null) return;

            Apply(scene, GetProfile(id), save);
        }

        public static void Apply(GameScene scene, UIProfile profile, bool save)
        {
            if (scene == null) return;
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var uiStateBeforeApply = CaptureUiState(scene);

            Settings.UIProfileId = profile.Id.ToString();
            ApplyProfile(scene, profile);
            RestoreUiStateAfterApply(scene, profile, uiStateBeforeApply);

            if (save) Settings.Save();
        }

        public static void CycleNext(GameScene scene, bool save)
        {
            if (scene == null) return;

            var next = GetNextProfileId(GetCurrentProfileId());
            Apply(scene, next, save);

            try
            {
                scene.ChatDialog?.ReceiveChat($"[UI] 已切换布局：{next}", ChatType.Hint);
            }
            catch
            {
            }
        }

        private static UIProfileId GetNextProfileId(UIProfileId current)
        {
            return current switch
            {
                UIProfileId.Classic => UIProfileId.Compact,
                UIProfileId.Compact => UIProfileId.Mobile,
                UIProfileId.Mobile => UIProfileId.Classic,
                _ => UIProfileId.Classic,
            };
        }

        private static void ApplyProfile(GameScene scene, UIProfile profile)
        {
            if (profile.TransparentChat.HasValue)
            {
                Settings.TransparentChat = profile.TransparentChat.Value;

                if (profile.TransparentChat.Value)
                {
                    scene.ChatDialog.ForeColour = Color.FromArgb(15, 0, 0);
                    scene.ChatDialog.BackColour = Color.FromArgb(15, 0, 0);
                    scene.ChatDialog.Opacity = 0.8F;
                }
                else
                {
                    scene.ChatDialog.ForeColour = Color.White;
                    scene.ChatDialog.BackColour = Color.White;
                    scene.ChatDialog.Opacity = 1F;
                }
            }

            if (profile.ChatWindowSize.HasValue)
            {
                scene.ChatDialog.SetWindowSize(profile.ChatWindowSize.Value);
            }

            var mainLocation = ComputeMainDialogLocation(scene, profile);
            scene.MainDialog.Location = mainLocation;

            var chatLocation = ComputeChatDialogLocation(scene, profile, mainLocation);
            scene.ChatDialog.Location = chatLocation;

            if (scene.ChatControl != null)
            {
                scene.ChatControl.Location = new Point(chatLocation.X, chatLocation.Y - scene.ChatControl.Size.Height);
            }

            if (scene.BeltDialog != null && scene.BeltDialog.Index == 1932)
            {
                scene.BeltDialog.Location = ComputeBeltDialogLocation(scene, chatLocation, scene.ChatControl?.Location ?? Point.Empty);
            }
        }

        private static Point ComputeMainDialogLocation(GameScene scene, UIProfile profile)
        {
            var x = (Settings.ScreenWidth / 2) - (scene.MainDialog.Size.Width / 2);
            var y = Settings.ScreenHeight - scene.MainDialog.Size.Height;

            switch (profile.MainPlacement)
            {
                case UIProfileMainPlacement.BottomLeft:
                    x = 0;
                    break;
                case UIProfileMainPlacement.BottomRight:
                    x = Settings.ScreenWidth - scene.MainDialog.Size.Width;
                    break;
            }

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            return new Point(x, y);
        }

        private static Point ComputeChatDialogLocation(GameScene scene, UIProfile profile, Point mainLocation)
        {
            var bottomY = Settings.ScreenHeight - scene.ChatDialog.Size.Height - 1;
            if (bottomY < 0) bottomY = 0;

            var x = profile.ChatPlacement switch
            {
                UIProfileChatPlacement.BottomLeft => 0,
                UIProfileChatPlacement.BottomRight => Settings.ScreenWidth - scene.ChatDialog.Size.Width,
                _ => mainLocation.X + 230,
            };

            if (x < 0) x = 0;
            return new Point(x, bottomY);
        }

        private sealed class UiStateSnapshot
        {
            public Point MainDialogLocation { get; init; }
            public Point ChatDialogLocation { get; init; }
            public Point ChatControlLocation { get; init; }

            public Point BeltDialogLocation { get; init; }
            public int BeltDialogIndex { get; init; }
            public bool IsBeltDialogVisible { get; init; }

            public Dictionary<MirControl, ControlStateSnapshot> TopLevelControls { get; init; } = new();
        }

        private readonly struct ControlStateSnapshot
        {
            public ControlStateSnapshot(Point location, bool visible, bool movable)
            {
                Location = location;
                Visible = visible;
                Movable = movable;
            }

            public Point Location { get; }
            public bool Visible { get; }
            public bool Movable { get; }
        }

        private static UiStateSnapshot CaptureUiState(GameScene scene)
        {
            var snapshot = new UiStateSnapshot
            {
                MainDialogLocation = scene.MainDialog?.Location ?? Point.Empty,
                ChatDialogLocation = scene.ChatDialog?.Location ?? Point.Empty,
                ChatControlLocation = scene.ChatControl?.Location ?? Point.Empty,

                BeltDialogLocation = scene.BeltDialog?.Location ?? Point.Empty,
                BeltDialogIndex = scene.BeltDialog?.Index ?? 0,
                IsBeltDialogVisible = scene.BeltDialog?.Visible ?? false,
            };

            if (scene.Controls != null)
            {
                foreach (var control in scene.Controls)
                {
                    if (control == null) continue;
                    snapshot.TopLevelControls[control] = new ControlStateSnapshot(control.Location, control.Visible, control.Movable);
                }
            }

            if (scene.SkillBarDialogs != null)
            {
                foreach (var dialog in scene.SkillBarDialogs)
                {
                    if (dialog == null) continue;
                    snapshot.TopLevelControls.TryAdd(dialog, new ControlStateSnapshot(dialog.Location, dialog.Visible, dialog.Movable));
                }
            }

            return snapshot;
        }

        private static void RestoreUiStateAfterApply(GameScene scene, UIProfile profile, UiStateSnapshot before)
        {
            if (scene == null) return;
            if (before == null) return;

            var mainDelta = new Point(
                (scene.MainDialog?.Location.X ?? 0) - before.MainDialogLocation.X,
                (scene.MainDialog?.Location.Y ?? 0) - before.MainDialogLocation.Y);

            // 1) 已打开窗口不丢失：保持可见性不变（为后续“重建 UI”预留）
            foreach (var entry in before.TopLevelControls)
            {
                var control = entry.Key;
                if (control == null || control.IsDisposed) continue;

                var state = entry.Value;

                if (state.Visible && !control.Visible) control.Show();
                else if (!state.Visible && control.Visible) control.Hide();
            }

            // 2) 位置迁移：把“可移动窗口”的位置按 MainDialog 的位移整体平移（保留相对布局）
            foreach (var entry in before.TopLevelControls)
            {
                var control = entry.Key;
                if (control == null || control.IsDisposed) continue;

                if (!entry.Value.Movable) continue;
                if (!entry.Value.Visible) continue;

                if (ReferenceEquals(control, scene.MainDialog)) continue;
                if (ReferenceEquals(control, scene.ChatDialog)) continue;
                if (ReferenceEquals(control, scene.ChatControl)) continue;
                if (ReferenceEquals(control, scene.BeltDialog)) continue;

                var migrated = new Point(entry.Value.Location.X + mainDelta.X, entry.Value.Location.Y + mainDelta.Y);
                control.Location = ClampToScreen(control, migrated);
            }

            // 3) 快捷栏（Belt）：若保持水平样式（Index=1932），且之前处于“自动布局位置”，则随 Profile 重算；否则保持原位。
            if (scene.BeltDialog != null && !scene.BeltDialog.IsDisposed && before.IsBeltDialogVisible)
            {
                var belt = scene.BeltDialog;

                if (before.BeltDialogIndex == 1932 && belt.Index == 1932)
                {
                    var expectedOld = ComputeBeltDialogLocation(scene, before.ChatDialogLocation, before.ChatControlLocation);
                    var wasAutoPlaced = IsNear(before.BeltDialogLocation, expectedOld, tolerance: 2);

                    if (wasAutoPlaced)
                    {
                        var expectedNew = ComputeBeltDialogLocation(scene, scene.ChatDialog.Location, scene.ChatControl?.Location ?? Point.Empty);
                        belt.Location = ClampToScreen(belt, expectedNew);
                    }
                    else
                    {
                        belt.Location = ClampToScreen(belt, before.BeltDialogLocation);
                    }
                }
                else
                {
                    belt.Location = ClampToScreen(belt, before.BeltDialogLocation);
                }
            }
        }

        private static bool IsNear(Point a, Point b, int tolerance)
        {
            return Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        }

        private static Point ComputeBeltDialogLocation(GameScene scene, Point chatDialogLocation, Point chatControlLocation)
        {
            if (scene == null) return Point.Empty;
            if (scene.BeltDialog == null) return Point.Empty;

            var beltY = scene.ChatControl != null
                ? chatControlLocation.Y - scene.BeltDialog.Size.Height
                : chatDialogLocation.Y - scene.BeltDialog.Size.Height;

            if (beltY < 0) beltY = 0;
            var beltX = chatDialogLocation.X;
            if (beltX < 0) beltX = 0;

            return new Point(beltX, beltY);
        }

        private static Point ClampToScreen(MirControl control, Point location)
        {
            if (control == null) return location;

            var maxX = Settings.ScreenWidth - control.Size.Width;
            var maxY = Settings.ScreenHeight - control.Size.Height;
            if (maxX < 0) maxX = 0;
            if (maxY < 0) maxY = 0;

            var x = location.X;
            var y = location.Y;

            if (x < 0) x = 0;
            else if (x > maxX) x = maxX;

            if (y < 0) y = 0;
            else if (y > maxY) y = maxY;

            return new Point(x, y);
        }
    }
}
