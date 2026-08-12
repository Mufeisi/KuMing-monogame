using System;
using System.Collections.Generic;
using System.Text;
using FairyGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoShare.MirGraphics;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static readonly Dictionary<int, NTexture> MobileStateStateItemTextureCache = new Dictionary<int, NTexture>();
        private static readonly Dictionary<int, NTexture> MobileStatePrguseTextureCache = new Dictionary<int, NTexture>();

        // 移动端装备页（EquipCell0..13）的槽位编号应与 EquipmentSlot 对齐。
        // 注意：移动端装备页格子是按“全局坐标排序后取前 14 个”绑定的，顺序与 EquipmentSlot 枚举并非完全一致。
        // 以当前 UI 布局表现为准：若出现“武器/衣服、项链/头盔位置颠倒”，需要在此做一次映射修正。
        private static readonly EquipmentSlot[] MobileStateEquipmentCellToSlot =
        {
            EquipmentSlot.Armour,     // EquipCell0
            EquipmentSlot.Weapon,     // EquipCell1
            EquipmentSlot.Necklace,   // EquipCell2
            EquipmentSlot.Torch,      // EquipCell3
            EquipmentSlot.Helmet,     // EquipCell4
            EquipmentSlot.BraceletL,  // EquipCell5
            EquipmentSlot.BraceletR,  // EquipCell6
            EquipmentSlot.RingL,      // EquipCell7
            EquipmentSlot.RingR,      // EquipCell8
            EquipmentSlot.Amulet,     // EquipCell9
            EquipmentSlot.Belt,       // EquipCell10
            EquipmentSlot.Boots,      // EquipCell11
            EquipmentSlot.Stone,      // EquipCell12
            EquipmentSlot.Mount,      // EquipCell13
        };

        private static int ResolveMobileStateEquipmentIndex(int cellIndex)
        {
            if (cellIndex < 0)
                return -1;

            if (cellIndex < MobileStateEquipmentCellToSlot.Length)
                return (int)MobileStateEquipmentCellToSlot[cellIndex];

            // 兜底：若 UI 超出 14 个槽位，则按枚举顺序回退
            return cellIndex;
        }

        private sealed class MobileStateWindowBinding
        {
            public GComponent Window;
            public string ResolveInfo;
            public bool Bound;

            public Controller TabController;
            public readonly GButton[] Tabs = new GButton[6];
            public readonly EventCallback0[] TabClickCallbacks = new EventCallback0[6];
            public readonly GComponent[] Pages = new GComponent[6];

            public int SelectedTabIndex;

            public GTextField UserName;
            public GTextField GuildName;

            public GComponent EquipmentPage;
            public GComponent EquipmentRoleRoot;
            public GLoader EquipmentRoleLoader;
            public readonly GComponent[] EquipmentSlotCells = new GComponent[14];
            public readonly EventCallback0[] EquipmentSlotClickCallbacks = new EventCallback0[14];
            public readonly EventCallback1[] EquipmentSlotDropCallbacks = new EventCallback1[14];
            public readonly MobileLongPressTipBinding[] EquipmentSlotTipBindings = new MobileLongPressTipBinding[14];
            public readonly GLoader[] EquipmentSlotIcons = new GLoader[14];

            public GComponent PaperDollRoot;
            public GLoader PaperDollArmourLayer;
            public GLoader PaperDollWeaponLayer;
            public GLoader PaperDollHeadLayer;

            public GTextField RoleStateAttribEdit;
            public GTextField RoleAttribBasEdit;
            public GTextField RoleAttribAdvanEdit;

            public GTextField FashionHintText;
            public GComponent FashionRoleRoot;
            public GLoader FashionRoleLoader;
            public readonly GComponent[] FashionSlotCells = new GComponent[3];
            public readonly EventCallback0[] FashionSlotClickCallbacks = new EventCallback0[3];
            public readonly MobileLongPressTipBinding[] FashionSlotTipBindings = new MobileLongPressTipBinding[3];
            public readonly GLoader[] FashionSlotIcons = new GLoader[3];

            public GComponent FashionPaperDollRoot;
            public GLoader FashionPaperDollArmourLayer;
            public GLoader FashionPaperDollWeaponLayer;
            public GLoader FashionPaperDollHeadLayer;

            public GTextField TitleTextEdit;

            public GList SkillGrid;
            public ListItemRenderer SkillItemRenderer;
        }

        private static MobileStateWindowBinding _mobileStateBinding;
        private static DateTime _nextMobileStateBindAttemptUtc = DateTime.MinValue;

        private static void ResetMobileStateBindings()
        {
            MobileStateWindowBinding binding = _mobileStateBinding;
            _mobileStateBinding = null;
            _nextMobileStateBindAttemptUtc = DateTime.MinValue;

            if (binding == null)
                return;

            try
            {
                if (binding.EquipmentSlotCells != null)
                {
                    for (int i = 0; i < binding.EquipmentSlotCells.Length; i++)
                    {
                        GComponent cell = binding.EquipmentSlotCells[i];
                        EventCallback0 callback = binding.EquipmentSlotClickCallbacks[i];
                        EventCallback1 dropCallback = binding.EquipmentSlotDropCallbacks[i];
                        MobileLongPressTipBinding tipBinding = binding.EquipmentSlotTipBindings[i];

                        if (tipBinding != null)
                        {
                            try
                            {
                                UnbindMobileLongPressItemTips(tipBinding);
                            }
                            catch
                            {
                            }

                            binding.EquipmentSlotTipBindings[i] = null;
                        }

                        if (cell == null || cell._disposed)
                        {
                            binding.EquipmentSlotClickCallbacks[i] = null;
                            binding.EquipmentSlotDropCallbacks[i] = null;
                            continue;
                        }

                        if (dropCallback != null)
                        {
                            try
                            {
                                cell.RemoveEventListener("onDrop", dropCallback);
                            }
                            catch
                            {
                            }

                            binding.EquipmentSlotDropCallbacks[i] = null;
                        }

                        if (callback == null)
                            continue;

                        try
                        {
                            cell.onClick.Remove(callback);
                        }
                        catch
                        {
                        }

                        binding.EquipmentSlotClickCallbacks[i] = null;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (binding.FashionSlotCells != null)
                {
                    for (int i = 0; i < binding.FashionSlotCells.Length; i++)
                    {
                        GComponent cell = binding.FashionSlotCells[i];
                        EventCallback0 callback = binding.FashionSlotClickCallbacks[i];
                        MobileLongPressTipBinding tipBinding = binding.FashionSlotTipBindings[i];

                        if (tipBinding != null)
                        {
                            try
                            {
                                UnbindMobileLongPressItemTips(tipBinding);
                            }
                            catch
                            {
                            }

                            binding.FashionSlotTipBindings[i] = null;
                        }

                        if (cell == null || cell._disposed)
                        {
                            binding.FashionSlotClickCallbacks[i] = null;
                            continue;
                        }

                        if (callback == null)
                            continue;

                        try
                        {
                            cell.onClick.Remove(callback);
                        }
                        catch
                        {
                        }

                        binding.FashionSlotClickCallbacks[i] = null;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (binding.SkillGrid != null && !binding.SkillGrid._disposed)
                    binding.SkillGrid.itemRenderer = null;
            }
            catch
            {
            }
        }

        private static void TryBindMobileStateWindowIfDue(string windowKey, GComponent window, string resolveInfo)
        {
            if (window == null || window._disposed)
                return;

            MobileStateWindowBinding binding = _mobileStateBinding;
            if (binding != null && (binding.Window == null || binding.Window._disposed))
            {
                ResetMobileStateBindings();
                binding = null;
            }

            if (binding == null || !ReferenceEquals(binding.Window, window))
            {
                binding = new MobileStateWindowBinding
                {
                    Window = window,
                    ResolveInfo = resolveInfo,
                    Bound = false,
                    SelectedTabIndex = 0,
                };
                _mobileStateBinding = binding;
                _nextMobileStateBindAttemptUtc = DateTime.MinValue;
            }

            if (binding.Bound)
                return;

            if (DateTime.UtcNow < _nextMobileStateBindAttemptUtc)
                return;

            _nextMobileStateBindAttemptUtc = DateTime.UtcNow.AddMilliseconds(650);

            try
            {
                binding.UserName = TryFindChildByNameRecursive(window, "DUserName") as GTextField;
            }
            catch
            {
                binding.UserName = null;
            }

            // Tabs + Pages（装备/时装/状态/属性/称号/技能）
            for (int i = 0; i < 6; i++)
            {
                int tabIndex = i;

                try
                {
                    binding.Tabs[i] = TryFindChildByNameRecursive(window, "DA2ETabButton" + (i + 1)) as GButton;
                }
                catch
                {
                    binding.Tabs[i] = null;
                }

                try
                {
                    binding.Pages[i] = TryFindChildByNameRecursive(window, "DA2EPage" + (i + 1)) as GComponent;
                }
                catch
                {
                    binding.Pages[i] = null;
                }

                try
                {
                    if (binding.Tabs[i] != null && !binding.Tabs[i]._disposed)
                    {
                        if (binding.TabClickCallbacks[i] == null)
                            binding.TabClickCallbacks[i] = () => SelectMobileStateTab(tabIndex);

                        try
                        {
                            binding.Tabs[i].enabled = true;
                        }
                        catch
                        {
                        }

                        try
                        {
                            binding.Tabs[i].onClick.Remove(binding.TabClickCallbacks[i]);
                        }
                        catch
                        {
                        }

                        binding.Tabs[i].onClick.Add(binding.TabClickCallbacks[i]);
                    }
                }
                catch
                {
                }
            }

            // 绑定 controller/page（如 publish 内配置了 Controller，则优先切 controller；否则 fallback 到手动 visible）。
            try
            {
                binding.TabController = FindBestMobileStateTabController(window);
            }
            catch
            {
                binding.TabController = null;
            }

            // Page1: 用户名/行会名 + 装备格子(EquipCell0..13)
            try
            {
                if (binding.Pages[0] != null && !binding.Pages[0]._disposed)
                {
                    binding.EquipmentPage = binding.Pages[0];
                    binding.GuildName = TryFindChildByNameRecursive(binding.Pages[0], "GuildName") as GTextField;

                    binding.EquipmentRoleRoot = binding.EquipmentPage;
                    binding.EquipmentRoleLoader = null;
                    try
                    {
                        GObject roleObj = TryFindChildByNameRecursive(binding.Pages[0], "role");
                        if (roleObj != null && !roleObj._disposed)
                        {
                            if (roleObj is GComponent comp && !comp._disposed)
                            {
                                binding.EquipmentRoleRoot = comp;

                                foreach (GObject obj in Enumerate(comp))
                                {
                                    if (obj == null || obj._disposed)
                                        continue;

                                    if (obj is GLoader loader && !loader._disposed &&
                                        string.Equals(loader.name, "role", StringComparison.OrdinalIgnoreCase))
                                    {
                                        binding.EquipmentRoleLoader = loader;
                                        break;
                                    }
                                }

                                if (binding.EquipmentRoleLoader == null)
                                {
                                    foreach (GObject obj in Enumerate(comp))
                                    {
                                        if (obj == null || obj._disposed)
                                            continue;

                                        if (obj is GLoader loader && !loader._disposed)
                                        {
                                            binding.EquipmentRoleLoader = loader;
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (roleObj is GLoader loader && !loader._disposed)
                            {
                                binding.EquipmentRoleLoader = loader;
                                if (loader.parent is GComponent parent && parent != null && !parent._disposed)
                                    binding.EquipmentRoleRoot = parent;
                            }
                        }
                    }
                    catch
                    {
                        binding.EquipmentRoleRoot = binding.EquipmentPage;
                        binding.EquipmentRoleLoader = null;
                    }

                    for (int slot = 0; slot < binding.EquipmentSlotIcons.Length; slot++)
                    {
                        binding.EquipmentSlotCells[slot] = null;
                        binding.EquipmentSlotIcons[slot] = null;
                    }

                    // 优先：按导出名 EquipCell0..13 精确绑定（Page1 内可能混入大量额外 EquipCell*，按坐标取前 14 个会绑错）
                    int namedHitCount = 0;
                    for (int slot = 0; slot < binding.EquipmentSlotIcons.Length; slot++)
                    {
                        GComponent cell = null;

                        try
                        {
                            cell = binding.Pages[0].GetChild("EquipCell" + slot) as GComponent;
                        }
                        catch
                        {
                            cell = null;
                        }

                        if (cell == null || cell._disposed)
                        {
                            try
                            {
                                cell = TryFindChildByNameRecursive(binding.Pages[0], "EquipCell" + slot) as GComponent;
                            }
                            catch
                            {
                                cell = null;
                            }
                        }

                        if (cell == null || cell._disposed)
                            continue;

                        namedHitCount++;

                        binding.EquipmentSlotCells[slot] = cell;

                        try
                        {
                            binding.EquipmentSlotIcons[slot] = cell.GetChild("item") as GLoader;
                        }
                        catch
                        {
                            binding.EquipmentSlotIcons[slot] = null;
                        }

                        // 点击装备弹 Tips（点空白关闭由 MobileItemTips 统一处理）
                        try
                        {
                            int captured = slot;

                            try
                            {
                                if (binding.EquipmentSlotClickCallbacks[captured] != null)
                                {
                                    cell.onClick.Remove(binding.EquipmentSlotClickCallbacks[captured]);
                                    binding.EquipmentSlotClickCallbacks[captured] = null;
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                UnbindMobileLongPressItemTips(binding.EquipmentSlotTipBindings[captured]);
                            }
                            catch
                            {
                            }

                                 binding.EquipmentSlotTipBindings[captured] = BindMobileLongPressItemTips(cell, () =>
                                 {
                                     try
                                     {
                                    UserItem[] equipment = GameScene.User?.Equipment;
                                    int index = ResolveMobileStateEquipmentIndex(captured);
                                    if (equipment == null || index < 0 || index >= equipment.Length)
                                        return null;
                                    return equipment[index];
                                }
                                catch
                                {
                                    return null;
                                 }
                             });

                             try
                             {
                                 if (binding.EquipmentSlotDropCallbacks[captured] != null)
                                 {
                                     cell.RemoveEventListener("onDrop", binding.EquipmentSlotDropCallbacks[captured]);
                                     binding.EquipmentSlotDropCallbacks[captured] = null;
                                 }
                             }
                             catch
                             {
                             }

                             try
                             {
                                 int toSlot = ResolveMobileStateEquipmentIndex(captured);
                                 binding.EquipmentSlotDropCallbacks[captured] = ctx => OnMobileItemDroppedOnEquipmentSlot(toSlot, ctx);
                                 cell.AddEventListener("onDrop", binding.EquipmentSlotDropCallbacks[captured]);
                             }
                             catch
                             {
                                 binding.EquipmentSlotDropCallbacks[captured] = null;
                             }
                         }
                         catch
                         {
                         }
                    }

                    // 兜底：若找不到命名槽位，则按组件名/位置绑定。
                    if (namedHitCount == 0)
                    {
                        var equipCells = new System.Collections.Generic.List<GComponent>(24);
                        foreach (GObject obj in Enumerate(binding.Pages[0]))
                        {
                            if (obj == null || obj._disposed || obj is not GComponent comp || comp._disposed)
                                continue;

                            string itemName = comp.packageItem?.name ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(itemName))
                                continue;

                            if (itemName.IndexOf("EquipCell", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            equipCells.Add(comp);
                        }

                        SortGComponentsByGlobalPosition(equipCells);

                        int take = Math.Min(binding.EquipmentSlotIcons.Length, equipCells.Count);
                        for (int slot = 0; slot < take; slot++)
                        {
                            GComponent cell = equipCells[slot];
                            if (cell == null || cell._disposed)
                                continue;

                            binding.EquipmentSlotCells[slot] = cell;

                            try
                            {
                                binding.EquipmentSlotIcons[slot] = cell.GetChild("item") as GLoader;
                            }
                            catch
                            {
                                binding.EquipmentSlotIcons[slot] = null;
                            }

                            try
                            {
                                int captured = slot;

                                try
                                {
                                    if (binding.EquipmentSlotClickCallbacks[captured] != null)
                                    {
                                        cell.onClick.Remove(binding.EquipmentSlotClickCallbacks[captured]);
                                        binding.EquipmentSlotClickCallbacks[captured] = null;
                                    }
                                }
                                catch
                                {
                                }

                                try
                                {
                                    UnbindMobileLongPressItemTips(binding.EquipmentSlotTipBindings[captured]);
                                }
                                catch
                                {
                                }

                                binding.EquipmentSlotTipBindings[captured] = BindMobileLongPressItemTips(cell, () =>
                                {
                                    try
                                    {
                                        UserItem[] equipment = GameScene.User?.Equipment;
                                        int index = ResolveMobileStateEquipmentIndex(captured);
                                        if (equipment == null || index < 0 || index >= equipment.Length)
                                            return null;
                                        return equipment[index];
                                    }
                                    catch
                                    {
                                        return null;
                                     }
                                 });

                                 try
                                 {
                                     if (binding.EquipmentSlotDropCallbacks[captured] != null)
                                     {
                                         cell.RemoveEventListener("onDrop", binding.EquipmentSlotDropCallbacks[captured]);
                                         binding.EquipmentSlotDropCallbacks[captured] = null;
                                     }
                                 }
                                 catch
                                 {
                                 }

                                 try
                                 {
                                     int toSlot = ResolveMobileStateEquipmentIndex(captured);
                                     binding.EquipmentSlotDropCallbacks[captured] = ctx => OnMobileItemDroppedOnEquipmentSlot(toSlot, ctx);
                                     cell.AddEventListener("onDrop", binding.EquipmentSlotDropCallbacks[captured]);
                                 }
                                 catch
                                 {
                                     binding.EquipmentSlotDropCallbacks[captured] = null;
                                 }
                             }
                             catch
                             {
                             }
                        }
                    }

                    EnsureMobileStatePaperDollLayers(binding);
                }
            }
            catch
            {
                binding.GuildName = null;
            }

            // Page3: 状态文本
            try
            {
                if (binding.Pages[2] != null && !binding.Pages[2]._disposed)
                    binding.RoleStateAttribEdit = TryFindChildByNameRecursive(binding.Pages[2], "RoleStateAttribEdit") as GTextField;
            }
            catch
            {
                binding.RoleStateAttribEdit = null;
            }

            // Page4: 属性文本（基础/高级）
            try
            {
                if (binding.Pages[3] != null && !binding.Pages[3]._disposed)
                {
                    binding.RoleAttribBasEdit = TryFindChildByNameRecursive(binding.Pages[3], "RoleAttribBasEdit") as GTextField;
                    binding.RoleAttribAdvanEdit = TryFindChildByNameRecursive(binding.Pages[3], "RoleAttribAdvanEdit") as GTextField;
                }
            }
            catch
            {
                binding.RoleAttribBasEdit = null;
                binding.RoleAttribAdvanEdit = null;
            }

            // Page5: 称号文本
            try
            {
                if (binding.Pages[4] != null && !binding.Pages[4]._disposed)
                    binding.TitleTextEdit = TryFindChildByNameRecursive(binding.Pages[4], "TitleTextEdit") as GTextField;
            }
            catch
            {
                binding.TitleTextEdit = null;
            }

            // Page6: 技能列表
            try
            {
                if (binding.Pages[5] != null && !binding.Pages[5]._disposed)
                    binding.SkillGrid = TryFindChildByNameRecursive(binding.Pages[5], "DSkillGrid") as GList;
            }
            catch
            {
                binding.SkillGrid = null;
            }

            // Page2: 时装/外显（纸娃娃 + 槽位）
            try
            {
                binding.FashionHintText = null;
                binding.FashionRoleRoot = null;

                for (int i = 0; i < binding.FashionSlotCells.Length; i++)
                {
                    binding.FashionSlotCells[i] = null;
                    binding.FashionSlotIcons[i] = null;
                }

                if (binding.Pages[1] != null && !binding.Pages[1]._disposed)
                {
                    binding.FashionHintText = TryFindChildByNameRecursive(binding.Pages[1], "ChatEdit44") as GTextField;
                    binding.FashionRoleRoot = TryFindChildByNameRecursive(binding.Pages[1], "role") as GComponent;

                    // Page2(role): 外层是 Component(role)，内部一般还有 Loader(role) 作为纸娃娃实际绘制区域
                    binding.FashionRoleLoader = null;
                    try
                    {
                        if (binding.FashionRoleRoot != null && !binding.FashionRoleRoot._disposed)
                        {
                            foreach (GObject obj in Enumerate(binding.FashionRoleRoot))
                            {
                                if (obj == null || obj._disposed)
                                    continue;

                                if (obj is GLoader loader && !loader._disposed &&
                                    string.Equals(loader.name, "role", StringComparison.OrdinalIgnoreCase))
                                {
                                    binding.FashionRoleLoader = loader;
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        binding.FashionRoleLoader = null;
                    }

                    var fashionCells = new List<GComponent>(6);
                    if (binding.FashionRoleRoot != null && !binding.FashionRoleRoot._disposed)
                    {
                        foreach (GObject obj in Enumerate(binding.FashionRoleRoot))
                        {
                            if (obj == null || obj._disposed || obj is not GComponent comp || comp._disposed)
                                continue;

                            string itemName = comp.packageItem?.name ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(itemName))
                                continue;

                            if (itemName.IndexOf("EquipCell", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            fashionCells.Add(comp);
                        }
                    }

                    SortGComponentsByGlobalPosition(fashionCells);

                    int take = Math.Min(binding.FashionSlotIcons.Length, fashionCells.Count);
                    for (int slot = 0; slot < take; slot++)
                    {
                        int captured = slot;
                        GComponent cell = fashionCells[slot];
                        if (cell == null || cell._disposed)
                            continue;

                        binding.FashionSlotCells[captured] = cell;

                        try
                        {
                            binding.FashionSlotIcons[captured] = cell.GetChild("item") as GLoader;
                        }
                        catch
                        {
                            binding.FashionSlotIcons[captured] = null;
                        }

                        // 点击弹 Tips（点空白关闭由 MobileItemTips 统一处理）
                        try
                        {
                            try
                            {
                                if (binding.FashionSlotClickCallbacks[captured] != null)
                                {
                                    cell.onClick.Remove(binding.FashionSlotClickCallbacks[captured]);
                                    binding.FashionSlotClickCallbacks[captured] = null;
                                }
                            }
                            catch
                            {
                            }

                            try
                            {
                                UnbindMobileLongPressItemTips(binding.FashionSlotTipBindings[captured]);
                            }
                            catch
                            {
                            }

                            binding.FashionSlotTipBindings[captured] = BindMobileLongPressItemTips(cell, () =>
                            {
                                try
                                {
                                    var user = GameScene.User;
                                    if (user == null || user.Equipment == null)
                                        return null;

                                    EquipmentSlot equipSlot;
                                    switch (captured)
                                    {
                                        case 0:
                                            equipSlot = EquipmentSlot.Weapon;
                                            break;
                                        case 1:
                                            equipSlot = EquipmentSlot.Armour;
                                            break;
                                        case 2:
                                            equipSlot = EquipmentSlot.Helmet;
                                            break;
                                        default:
                                            return null;
                                    }

                                    int index = (int)equipSlot;
                                    if (index < 0 || index >= user.Equipment.Length)
                                        return null;

                                    return user.Equipment[index];
                                }
                                catch
                                {
                                    return null;
                                }
                            });
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
                binding.FashionHintText = null;
                binding.FashionRoleRoot = null;
            }

            try
            {
                if (binding.SkillGrid != null && !binding.SkillGrid._disposed)
                {
                    if (binding.SkillItemRenderer == null)
                        binding.SkillItemRenderer = RenderMobileStateSkillListItem;

                    binding.SkillGrid.itemRenderer = binding.SkillItemRenderer;

                    try
                    {
                        binding.SkillGrid.SetVirtual();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            binding.Bound = true;
            ApplyMobileStateTabVisibility(binding, binding.SelectedTabIndex, force: true);

            if (Settings.LogErrors)
                CMain.SaveLog("FairyGUI: State 窗口绑定完成：" + (binding.ResolveInfo ?? DescribeObject(window, window)));
        }

        private static Controller FindBestMobileStateTabController(GComponent window)
        {
            if (window == null || window._disposed)
                return null;

            Controller best = null;
            int bestScore = 0;

            for (int i = 0; i < 32; i++)
            {
                Controller controller = null;
                try
                {
                    controller = window.GetControllerAt(i);
                }
                catch
                {
                    controller = null;
                }

                if (controller == null)
                    break;

                int score = 0;
                try
                {
                    if (controller.pageCount >= 6)
                        score += 100;
                    if (controller.pageCount == 6)
                        score += 20;
                }
                catch
                {
                }

                try
                {
                    string name = controller.name ?? string.Empty;
                    if (name.IndexOf("tab", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 10;
                    if (name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 5;
                }
                catch
                {
                }

                if (score > bestScore)
                {
                    best = controller;
                    bestScore = score;
                }
            }

            try
            {
                if (best != null && best.pageCount == 6)
                    return best;
            }
            catch
            {
            }

            return null;
        }

        private static void SelectMobileStateTab(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= 6)
                return;

            MobileStateWindowBinding binding = _mobileStateBinding;
            if (binding == null || binding.Window == null || binding.Window._disposed)
                return;

            binding.SelectedTabIndex = tabIndex;
            ApplyMobileStateTabVisibility(binding, tabIndex, force: false);
            TryRefreshMobileStateIfDue(force: true);
        }

        private static void ApplyMobileStateTabVisibility(MobileStateWindowBinding binding, int tabIndex, bool force)
        {
            if (binding == null)
                return;

            try
            {
                if (binding.TabController != null && binding.TabController.pageCount == 6)
                    binding.TabController.selectedIndex = tabIndex;
            }
            catch
            {
            }

            for (int i = 0; i < 6; i++)
            {
                try
                {
                    if (binding.Pages[i] != null && !binding.Pages[i]._disposed)
                        binding.Pages[i].visible = i == tabIndex;
                }
                catch
                {
                }

                try
                {
                    if (binding.Tabs[i] != null && !binding.Tabs[i]._disposed)
                        SetJoystickButtonPressed(binding.Tabs[i], pressed: i == tabIndex);
                }
                catch
                {
                }
            }
        }

        private static void TryRefreshMobileStateIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (!MobileWindows.TryGetValue("State", out GComponent window) || window == null || window._disposed)
            {
                if (_mobileStateBinding != null)
                    ResetMobileStateBindings();
                return;
            }

            if (!window.visible)
                return;

            TryBindMobileStateWindowIfDue("State", window, resolveInfo: null);

            MobileStateWindowBinding binding = _mobileStateBinding;
            if (binding == null || !binding.Bound || binding.Window == null || binding.Window._disposed)
            {
                ResetMobileStateBindings();
                return;
            }

            var user = GameScene.User;
            if (user == null)
                return;

            // 顶部用户名
            try
            {
                if (binding.UserName != null && !binding.UserName._disposed)
                    binding.UserName.text = user.Name ?? string.Empty;
            }
            catch
            {
            }

            // 装备页：行会名 + EquipCell0..13 贴图
            if (force || binding.SelectedTabIndex == 0)
            {
                try
                {
                    if (binding.GuildName != null && !binding.GuildName._disposed)
                        binding.GuildName.text = user.GuildName ?? string.Empty;
                }
                catch
                {
                }

                TryRefreshMobileStatePaperDoll(binding, user);

                try
                {
                    UserItem[] equipment = user.Equipment;
                    if (equipment != null && binding.EquipmentSlotIcons != null)
                    {
                        int cellCount = binding.EquipmentSlotIcons.Length;
                        for (int cell = 0; cell < cellCount; cell++)
                        {
                            GLoader icon = binding.EquipmentSlotIcons[cell];
                            if (icon == null || icon._disposed)
                                continue;

                            int index = ResolveMobileStateEquipmentIndex(cell);
                            UserItem item = index >= 0 && index < equipment.Length ? equipment[index] : null;
                            if (item == null || item.Info == null)
                            {
                                icon.showErrorSign = false;
                                icon.texture = null;
                                icon.url = string.Empty;
                                continue;
                            }

                            ushort iconIndex = item.Image;
                            Libraries.Items.Touch(iconIndex);
                            NTexture texture = GetOrCreateItemIconTexture(iconIndex);

                            icon.showErrorSign = false;
                            icon.url = string.Empty;
                            icon.texture = texture;
                        }
                    }
                }
                catch
                {
                }
            }

            // 状态页：状态文本
            if (force || binding.SelectedTabIndex == 2)
            {
                try
                {
                    if (binding.RoleStateAttribEdit != null && !binding.RoleStateAttribEdit._disposed)
                        binding.RoleStateAttribEdit.text = BuildMobileStateStatusText(user);
                }
                catch
                {
                }
            }

            // 时装页：纸娃娃外显（时装/武器/帽子等）
            if (force || binding.SelectedTabIndex == 1)
                TryRefreshMobileStateFashion(binding, user);

            // 属性页：基础/高级
            if (force || binding.SelectedTabIndex == 3)
            {
                try
                {
                    if (binding.RoleAttribBasEdit != null && !binding.RoleAttribBasEdit._disposed)
                        binding.RoleAttribBasEdit.text = BuildMobileStateBaseAttribText(user);
                }
                catch
                {
                }

                try
                {
                    if (binding.RoleAttribAdvanEdit != null && !binding.RoleAttribAdvanEdit._disposed)
                        binding.RoleAttribAdvanEdit.text = BuildMobileStateAdvanAttribText(user);
                }
                catch
                {
                }
            }

            // 称号页：占位（后续接服务端数据）
            if (force || binding.SelectedTabIndex == 4)
            {
                try
                {
                    if (binding.TitleTextEdit != null && !binding.TitleTextEdit._disposed && string.IsNullOrWhiteSpace(binding.TitleTextEdit.text))
                        binding.TitleTextEdit.text = "暂无称号数据";
                }
                catch
                {
                }
            }

            // 技能页：列表
            if (force || binding.SelectedTabIndex == 5)
                TryRefreshMobileStateSkillGrid(binding, user, force);
        }

        private static void TryRefreshMobileStateSkillGrid(MobileStateWindowBinding binding, UserObject user, bool force)
        {
            if (binding == null)
                return;

            if (binding.SkillGrid == null || binding.SkillGrid._disposed)
                return;

            int count = 0;
            try
            {
                count = user?.Magics?.Count ?? 0;
            }
            catch
            {
                count = 0;
            }

            try
            {
                if (binding.SkillItemRenderer == null)
                    binding.SkillItemRenderer = RenderMobileStateSkillListItem;

                if (binding.SkillGrid.itemRenderer != binding.SkillItemRenderer)
                    binding.SkillGrid.itemRenderer = binding.SkillItemRenderer;

                try
                {
                    binding.SkillGrid.SetVirtual();
                }
                catch
                {
                }

                if (force || binding.SkillGrid.numItems != count)
                    binding.SkillGrid.numItems = count;
            }
            catch
            {
            }
        }

        private static void RenderMobileStateSkillListItem(int index, GObject obj)
        {
            if (obj == null || obj._disposed)
                return;

            var user = GameScene.User;
            if (user == null || user.Magics == null)
                return;

            if (index < 0 || index >= user.Magics.Count)
                return;

            ClientMagic magic = user.Magics[index];
            if (magic == null)
                return;

            if (obj is not GComponent comp || comp._disposed)
                return;

            try
            {
                if (comp.GetChild("DSkillIconLoader") is GLoader icon && icon != null && !icon._disposed)
                {
                    byte iconByte = magic.Icon;
                    Libraries.MagIcon2.Touch(iconByte);
                    NTexture texture = GetOrCreateMagicIconTexture(iconByte);

                    icon.showErrorSign = false;
                    icon.url = string.Empty;
                    icon.texture = texture;

                    // 技能页：长按拖拽技能到右侧圆形快捷施法栏
                    try
                    {
                        icon.touchable = true;
                        SetTouchableRecursive(icon, touchable: true);

                        if (icon.data is MobileLongPressMagicDragBinding existing)
                        {
                            existing.ResolvePayload = () =>
                            {
                                try
                                {
                                    var u = GameScene.User;
                                    if (u?.Magics == null || index < 0 || index >= u.Magics.Count)
                                        return null;

                                    ClientMagic m = u.Magics[index];
                                    if (m == null)
                                        return null;

                                    return new MobileMagicDragPayload
                                    {
                                        HotKey = m.Key,
                                        Icon = m.Icon,
                                        Spell = m.Spell,
                                    };
                                }
                                catch
                                {
                                    return null;
                                }
                            };
                        }
                        else
                        {
                            icon.data = BindMobileLongPressMagicDrag(
                                icon,
                                resolvePayload: () =>
                                {
                                    try
                                    {
                                        var u = GameScene.User;
                                        if (u?.Magics == null || index < 0 || index >= u.Magics.Count)
                                            return null;

                                        ClientMagic m = u.Magics[index];
                                        if (m == null)
                                            return null;

                                        return new MobileMagicDragPayload
                                        {
                                            HotKey = m.Key,
                                            Icon = m.Icon,
                                            Spell = m.Spell,
                                        };
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                });
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (comp.GetChild("ChatEdit41") is GTextField name && name != null && !name._disposed)
                    name.text = magic.Name ?? string.Empty;
            }
            catch
            {
            }

            try
            {
                if (comp.GetChild("ChatEdit42") is GTextField level && level != null && !level._disposed)
                    level.text = "Lv." + magic.Level;
            }
            catch
            {
            }

            try
            {
                if (comp.GetChild("DSkillKey") is GTextField key && key != null && !key._disposed)
                    key.text = magic.Key > 0 ? magic.Key.ToString() : string.Empty;
            }
            catch
            {
            }
        }

        private static void OnMobileStateEquipmentSlotClicked(int slotIndex)
        {
            var user = GameScene.User;
            if (user == null || user.Equipment == null)
                return;

            if (slotIndex < 0 || slotIndex >= user.Equipment.Length)
                return;

            UserItem item = user.Equipment[slotIndex];
            if (item == null || item.Info == null)
            {
                HideMobileItemTips();
                return;
            }

            ShowMobileItemTips(item);
        }

        private static void OnMobileStateFashionSlotClicked(int slotIndex)
        {
            var user = GameScene.User;
            if (user == null || user.Equipment == null)
                return;

            EquipmentSlot equipSlot;
            switch (slotIndex)
            {
                case 0:
                    equipSlot = EquipmentSlot.Weapon;
                    break;
                case 1:
                    equipSlot = EquipmentSlot.Armour;
                    break;
                case 2:
                    equipSlot = EquipmentSlot.Helmet;
                    break;
                default:
                    return;
            }

            int index = (int)equipSlot;
            if (index < 0 || index >= user.Equipment.Length)
                return;

            UserItem item = user.Equipment[index];
            if (item == null || item.Info == null)
            {
                HideMobileItemTips();
                return;
            }

            ShowMobileItemTips(item);
        }

        private static void EnsureMobileStateFashionPaperDollLayers(MobileStateWindowBinding binding)
        {
            if (binding == null)
                return;

            if (binding.FashionRoleRoot == null || binding.FashionRoleRoot._disposed)
                return;

            if (binding.FashionPaperDollRoot != null && binding.FashionPaperDollRoot._disposed)
                binding.FashionPaperDollRoot = null;

            // 每次都同步一次区域（某些 publish 会动态调整 role Loader 的大小/位置）
            float targetX = 0F;
            float targetY = 0F;
            float targetW = binding.FashionRoleRoot.width;
            float targetH = binding.FashionRoleRoot.height;
            int insertIndex = 0;

            // 避免 publish 内置纸娃娃与代码纸娃娃叠加（出现“双纸娃娃”）
            try
            {
                GLoader roleToHide = binding.FashionRoleLoader;
                if (roleToHide != null && !roleToHide._disposed)
                {
                    roleToHide.showErrorSign = false;
                    roleToHide.url = string.Empty;
                    roleToHide.texture = null;
                }
            }
            catch
            {
            }

            try
            {
                GLoader role = binding.FashionRoleLoader;
                if (role != null && !role._disposed)
                {
                    if (ReferenceEquals(role.parent, binding.FashionRoleRoot))
                    {
                        targetX = role.x;
                        targetY = role.y;
                        targetW = role.width;
                        targetH = role.height;

                        try
                        {
                            insertIndex = binding.FashionRoleRoot.GetChildIndex(role) + 1;
                        }
                        catch
                        {
                            insertIndex = 0;
                        }
                    }
                    else
                    {
                        Vector2 roleGlobal = role.LocalToGlobal(Vector2.Zero);
                        Vector2 rootGlobal = binding.FashionRoleRoot.LocalToGlobal(Vector2.Zero);
                        targetX = roleGlobal.X - rootGlobal.X;
                        targetY = roleGlobal.Y - rootGlobal.Y;
                        targetW = role.width;
                        targetH = role.height;
                        insertIndex = 0;
                    }
                }
            }
            catch
            {
                targetX = 0F;
                targetY = 0F;
                targetW = binding.FashionRoleRoot.width;
                targetH = binding.FashionRoleRoot.height;
                insertIndex = 0;
            }

            if (binding.FashionPaperDollRoot != null)
            {
                try
                {
                    if (!ReferenceEquals(binding.FashionPaperDollRoot.parent, binding.FashionRoleRoot))
                    {
                        try { binding.FashionPaperDollRoot.RemoveFromParent(); } catch { }
                        try
                        {
                            insertIndex = Math.Clamp(insertIndex, 0, binding.FashionRoleRoot.numChildren);
                            binding.FashionRoleRoot.AddChildAt(binding.FashionPaperDollRoot, insertIndex);
                        }
                        catch
                        {
                            try { binding.FashionRoleRoot.AddChild(binding.FashionPaperDollRoot); } catch { }
                        }
                    }

                    binding.FashionPaperDollRoot.SetPosition(targetX, targetY);
                    binding.FashionPaperDollRoot.SetSize(targetW, targetH);
                }
                catch
                {
                }
                return;
            }

            var root = new GComponent
            {
                name = "MobileFashionPaperDollRoot",
                touchable = false,
                opaque = false,
            };

            try
            {
                root.SetPosition(targetX, targetY);
                root.SetSize(targetW, targetH);

                insertIndex = Math.Clamp(insertIndex, 0, binding.FashionRoleRoot.numChildren);
                binding.FashionRoleRoot.AddChildAt(root, insertIndex);
            }
            catch
            {
                try
                {
                    binding.FashionRoleRoot.AddChild(root);
                    binding.FashionRoleRoot.SetChildIndex(root, 0);
                }
                catch
                {
                    return;
                }
            }

            binding.FashionPaperDollRoot = root;

            binding.FashionPaperDollArmourLayer = CreatePaperDollLayer("FashionPaperDoll.Armour");
            binding.FashionPaperDollWeaponLayer = CreatePaperDollLayer("FashionPaperDoll.Weapon");
            binding.FashionPaperDollHeadLayer = CreatePaperDollLayer("FashionPaperDoll.Head");

            try { root.AddChild(binding.FashionPaperDollArmourLayer); } catch { }
            try { root.AddChild(binding.FashionPaperDollWeaponLayer); } catch { }
            try { root.AddChild(binding.FashionPaperDollHeadLayer); } catch { }
        }

        private static void EnsureMobileStatePaperDollLayers(MobileStateWindowBinding binding)
        {
            if (binding == null)
                return;

            if (binding.EquipmentPage == null || binding.EquipmentPage._disposed)
                return;

            if (binding.PaperDollRoot != null && binding.PaperDollRoot._disposed)
                binding.PaperDollRoot = null;

            // 每次都同步一次区域（Page1 的 role 是 Loader，纸娃娃应对齐到该区域并位于 EquipCell 之下）
            float targetX = 0F;
            float targetY = 0F;
            float targetW = binding.EquipmentPage.width;
            float targetH = binding.EquipmentPage.height;
            int insertIndex = 0;

            GComponent container = null;
            try
            {
                container = binding.EquipmentRoleRoot;
                if (container != null && container._disposed)
                    container = null;
            }
            catch
            {
                container = null;
            }

            container ??= binding.EquipmentPage;

            if (!ReferenceEquals(container, binding.EquipmentPage))
            {
                try
                {
                    targetW = container.width;
                    targetH = container.height;
                }
                catch
                {
                    targetW = binding.EquipmentPage.width;
                    targetH = binding.EquipmentPage.height;
                }
            }

            // 避免 publish 内置纸娃娃与代码纸娃娃叠加（出现“双纸娃娃”）
            try
            {
                GLoader roleToHide = binding.EquipmentRoleLoader;
                if (roleToHide != null && !roleToHide._disposed)
                {
                    roleToHide.showErrorSign = false;
                    roleToHide.url = string.Empty;
                    roleToHide.texture = null;
                }
            }
            catch
            {
            }

            try
            {
                GLoader role = binding.EquipmentRoleLoader;
                if (role != null && !role._disposed)
                {
                    if (ReferenceEquals(role.parent, container))
                    {
                        targetX = role.x;
                        targetY = role.y;
                        targetW = role.width;
                        targetH = role.height;

                        try
                        {
                            insertIndex = container.GetChildIndex(role) + 1;
                        }
                        catch
                        {
                            insertIndex = 0;
                        }
                    }
                    else
                    {
                        Vector2 roleGlobal = role.LocalToGlobal(Vector2.Zero);
                        Vector2 rootGlobal = container.LocalToGlobal(Vector2.Zero);
                        targetX = roleGlobal.X - rootGlobal.X;
                        targetY = roleGlobal.Y - rootGlobal.Y;
                        targetW = role.width;
                        targetH = role.height;
                        insertIndex = 0;
                    }
                }
            }
            catch
            {
                targetX = 0F;
                targetY = 0F;
                targetW = container.width;
                targetH = container.height;
                insertIndex = 0;
            }

            if (ReferenceEquals(container, binding.EquipmentPage))
            {
                try
                {
                    int minEquipIndex = int.MaxValue;

                    if (binding.EquipmentSlotCells != null)
                    {
                        for (int i = 0; i < binding.EquipmentSlotCells.Length; i++)
                        {
                            GComponent cell = binding.EquipmentSlotCells[i];
                            if (cell == null || cell._disposed || !ReferenceEquals(cell.parent, binding.EquipmentPage))
                                continue;

                            int idx = binding.EquipmentPage.GetChildIndex(cell);
                            if (idx >= 0 && idx < minEquipIndex)
                                minEquipIndex = idx;
                        }
                    }

                    if (minEquipIndex != int.MaxValue)
                        insertIndex = minEquipIndex;
                }
                catch
                {
                }
            }

            if (binding.PaperDollRoot != null)
            {
                try
                {
                    if (!ReferenceEquals(binding.PaperDollRoot.parent, container))
                    {
                        try { binding.PaperDollRoot.RemoveFromParent(); } catch { }
                        try
                        {
                            insertIndex = Math.Clamp(insertIndex, 0, container.numChildren);
                            container.AddChildAt(binding.PaperDollRoot, insertIndex);
                        }
                        catch
                        {
                            try { container.AddChild(binding.PaperDollRoot); } catch { }
                        }
                    }

                    binding.PaperDollRoot.SetPosition(targetX, targetY);
                    binding.PaperDollRoot.SetSize(targetW, targetH);
                }
                catch
                {
                }
                return;
            }

            // 纸娃娃渲染层：用多层 GLoader 叠加，避免离屏合成开销。
            var root = new GComponent
            {
                name = "MobilePaperDollRoot",
                touchable = false,
                opaque = false,
            };

            try
            {
                root.SetPosition(targetX, targetY);
                root.SetSize(targetW, targetH);

                insertIndex = Math.Clamp(insertIndex, 0, container.numChildren);
                container.AddChildAt(root, insertIndex);
            }
            catch
            {
                try
                {
                    container.AddChild(root);
                    container.SetChildIndex(root, 0);
                }
                catch
                {
                    return;
                }
            }

            binding.PaperDollRoot = root;

            binding.PaperDollArmourLayer = CreatePaperDollLayer("PaperDoll.Armour");
            binding.PaperDollWeaponLayer = CreatePaperDollLayer("PaperDoll.Weapon");
            binding.PaperDollHeadLayer = CreatePaperDollLayer("PaperDoll.Head");

            try { root.AddChild(binding.PaperDollArmourLayer); } catch { }
            try { root.AddChild(binding.PaperDollWeaponLayer); } catch { }
            try { root.AddChild(binding.PaperDollHeadLayer); } catch { }
        }

        private static GLoader CreatePaperDollLayer(string name)
        {
            var loader = new GLoader
            {
                name = name,
                touchable = false,
                autoSize = true,
                showErrorSign = false,
            };

            try
            {
                loader.url = string.Empty;
                loader.texture = null;
            }
            catch
            {
            }

            return loader;
        }

        private static void CenterMobileStatePaperDollLayers(GComponent root, params GLoader[] layers)
        {
            if (root == null || root._disposed || layers == null || layers.Length == 0)
                return;

            float minX = 0F;
            float minY = 0F;
            float maxX = 0F;
            float maxY = 0F;
            int used = 0;

            for (int i = 0; i < layers.Length; i++)
            {
                GLoader layer = layers[i];
                if (layer == null || layer._disposed)
                    continue;

                NTexture texture = null;
                try { texture = layer.texture; } catch { texture = null; }
                if (texture == null)
                    continue;

                float w = 0F;
                float h = 0F;
                try
                {
                    w = texture.width;
                    h = texture.height;
                }
                catch
                {
                    try { w = layer.width; h = layer.height; } catch { w = 0F; h = 0F; }
                }

                if (w <= 1F || h <= 1F)
                    continue;

                float x = layer.x;
                float y = layer.y;

                if (used == 0)
                {
                    minX = x;
                    minY = y;
                    maxX = x + w;
                    maxY = y + h;
                }
                else
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x + w > maxX) maxX = x + w;
                    if (y + h > maxY) maxY = y + h;
                }

                used++;
            }

            if (used == 0)
                return;

            float boxW = maxX - minX;
            float boxH = maxY - minY;

            float rootW = 0F;
            float rootH = 0F;
            try { rootW = root.width; rootH = root.height; } catch { rootW = 0F; rootH = 0F; }

            if (boxW <= 1F || boxH <= 1F || rootW <= 1F || rootH <= 1F)
                return;

            float dx = (rootW - boxW) * 0.5F - minX;
            float dy = (rootH - boxH) * 0.5F - minY;

            for (int i = 0; i < layers.Length; i++)
            {
                GLoader layer = layers[i];
                if (layer == null || layer._disposed)
                    continue;

                NTexture texture = null;
                try { texture = layer.texture; } catch { texture = null; }
                if (texture == null)
                    continue;

                try
                {
                    layer.x += dx;
                    layer.y += dy;
                }
                catch
                {
                }
            }
        }

        private static void TryRefreshMobileStatePaperDoll(MobileStateWindowBinding binding, UserObject user)
        {
            if (binding == null || user == null)
                return;

            if (binding.SelectedTabIndex != 0)
                return;

            EnsureMobileStatePaperDollLayers(binding);

            if (binding.PaperDollRoot == null || binding.PaperDollRoot._disposed)
                return;

            UserItem[] equipment = user.Equipment;

            UserItem armourItem = null;
            UserItem weaponItem = null;
            UserItem helmetItem = null;

            try
            {
                if (equipment != null)
                {
                    if (equipment.Length > (int)EquipmentSlot.Armour)
                        armourItem = equipment[(int)EquipmentSlot.Armour];
                    if (equipment.Length > (int)EquipmentSlot.Weapon)
                        weaponItem = equipment[(int)EquipmentSlot.Weapon];
                    if (equipment.Length > (int)EquipmentSlot.Helmet)
                        helmetItem = equipment[(int)EquipmentSlot.Helmet];
                }
            }
            catch
            {
                armourItem = null;
                weaponItem = null;
                helmetItem = null;
            }

            // 1) 盔甲（纸娃娃底图）
            try
            {
                int imageIndex = 0;
                if (armourItem != null && armourItem.Info != null)
                {
                    ItemInfo real = Functions.GetRealItem(armourItem.Info, user.Level, user.Class, GameScene.ItemInfoList);
                    imageIndex = real?.Image ?? 0;
                }

                ApplyPaperDollStateItemLayer(binding.PaperDollArmourLayer, imageIndex);
            }
            catch
            {
            }

            // 2) 武器（叠加）
            try
            {
                int imageIndex = 0;
                if (weaponItem != null && weaponItem.Info != null)
                {
                    ItemInfo real = Functions.GetRealItem(weaponItem.Info, user.Level, user.Class, GameScene.ItemInfoList);
                    imageIndex = real?.Image ?? 0;
                }

                ApplyPaperDollStateItemLayer(binding.PaperDollWeaponLayer, imageIndex);
            }
            catch
            {
            }

            // 3) 头盔或头发（叠加）
            try
            {
                if (helmetItem != null && helmetItem.Info != null)
                {
                    ApplyPaperDollStateItemLayer(binding.PaperDollHeadLayer, helmetItem.Info.Image);
                }
                else
                {
                    ApplyPaperDollHairLayer(binding.PaperDollHeadLayer, user);
                }
            }
            catch
            {
            }

            // 将纸娃娃整体居中到 role 区域（避免偏下/偏移）
            try
            {
                CenterMobileStatePaperDollLayers(binding.PaperDollRoot, binding.PaperDollArmourLayer, binding.PaperDollWeaponLayer, binding.PaperDollHeadLayer);
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileStateFashion(MobileStateWindowBinding binding, UserObject user)
        {
            if (binding == null || user == null)
                return;

            if (binding.SelectedTabIndex != 1)
                return;

            // 1) 纸娃娃外显（时装/武器/帽子等）
            TryRefreshMobileStateFashionPaperDoll(binding, user);

            // 2) 槽位图标：按顺序显示 盔甲/武器/头盔（槽位不足时显示前 N 个）
            try
            {
                if (binding.FashionSlotIcons == null)
                    return;

                // Page2(role) 当前 UI 只有 1 个槽位（左侧武器位）；若未来 UI 扩展为 2/3 个槽位，仍保持“左到右”顺序：武器、衣服、头盔。
                EquipmentSlot[] mapping = { EquipmentSlot.Weapon, EquipmentSlot.Armour, EquipmentSlot.Helmet };

                for (int i = 0; i < binding.FashionSlotIcons.Length; i++)
                {
                    GLoader icon = binding.FashionSlotIcons[i];
                    if (icon == null || icon._disposed)
                        continue;

                    if (i < 0 || i >= mapping.Length)
                    {
                        icon.showErrorSign = false;
                        icon.texture = null;
                        icon.url = string.Empty;
                        continue;
                    }

                    int equipIndex = (int)mapping[i];
                    UserItem item = (user.Equipment != null && equipIndex >= 0 && equipIndex < user.Equipment.Length) ? user.Equipment[equipIndex] : null;

                    if (item == null || item.Info == null)
                    {
                        icon.showErrorSign = false;
                        icon.texture = null;
                        icon.url = string.Empty;
                        continue;
                    }

                    ushort iconIndex = item.Image;
                    Libraries.Items.Touch(iconIndex);
                    NTexture texture = GetOrCreateItemIconTexture(iconIndex);

                    icon.showErrorSign = false;
                    icon.url = string.Empty;
                    icon.texture = texture;
                }
            }
            catch
            {
            }

            // 3) 占位提示：若纸娃娃/槽位已可用，清空提示文本
            try
            {
                if (binding.FashionHintText != null && !binding.FashionHintText._disposed)
                    binding.FashionHintText.text = string.Empty;
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileStateFashionPaperDoll(MobileStateWindowBinding binding, UserObject user)
        {
            if (binding == null || user == null)
                return;

            if (binding.SelectedTabIndex != 1)
                return;

            EnsureMobileStateFashionPaperDollLayers(binding);

            if (binding.FashionPaperDollRoot == null || binding.FashionPaperDollRoot._disposed)
                return;

            UserItem[] equipment = user.Equipment;

            UserItem armourItem = null;
            UserItem weaponItem = null;
            UserItem helmetItem = null;

            try
            {
                if (equipment != null)
                {
                    if (equipment.Length > (int)EquipmentSlot.Armour)
                        armourItem = equipment[(int)EquipmentSlot.Armour];
                    if (equipment.Length > (int)EquipmentSlot.Weapon)
                        weaponItem = equipment[(int)EquipmentSlot.Weapon];
                    if (equipment.Length > (int)EquipmentSlot.Helmet)
                        helmetItem = equipment[(int)EquipmentSlot.Helmet];
                }
            }
            catch
            {
                armourItem = null;
                weaponItem = null;
                helmetItem = null;
            }

            // 1) 盔甲（纸娃娃底图）
            try
            {
                int imageIndex = 0;
                if (armourItem != null && armourItem.Info != null)
                {
                    ItemInfo real = Functions.GetRealItem(armourItem.Info, user.Level, user.Class, GameScene.ItemInfoList);
                    imageIndex = real?.Image ?? 0;
                }

                ApplyPaperDollStateItemLayer(binding.FashionPaperDollArmourLayer, imageIndex);
            }
            catch
            {
            }

            // 2) 武器（叠加）
            try
            {
                int imageIndex = 0;
                if (weaponItem != null && weaponItem.Info != null)
                {
                    ItemInfo real = Functions.GetRealItem(weaponItem.Info, user.Level, user.Class, GameScene.ItemInfoList);
                    imageIndex = real?.Image ?? 0;
                }

                ApplyPaperDollStateItemLayer(binding.FashionPaperDollWeaponLayer, imageIndex);
            }
            catch
            {
            }

            // 3) 头盔或头发（叠加）
            try
            {
                if (helmetItem != null && helmetItem.Info != null)
                {
                    ApplyPaperDollStateItemLayer(binding.FashionPaperDollHeadLayer, helmetItem.Info.Image);
                }
                else
                {
                    ApplyPaperDollHairLayer(binding.FashionPaperDollHeadLayer, user);
                }
            }
            catch
            {
            }

            // 将纸娃娃整体居中到 role 区域（避免偏下/偏移）
            try
            {
                CenterMobileStatePaperDollLayers(binding.FashionPaperDollRoot, binding.FashionPaperDollArmourLayer, binding.FashionPaperDollWeaponLayer, binding.FashionPaperDollHeadLayer);
            }
            catch
            {
            }
        }

        private static void ApplyPaperDollStateItemLayer(GLoader layer, int imageIndex)
        {
            if (layer == null || layer._disposed)
                return;

            if (imageIndex <= 0)
            {
                try
                {
                    layer.texture = null;
                    layer.url = string.Empty;
                }
                catch
                {
                }
                return;
            }

            try
            {
                Libraries.StateItems.Touch(imageIndex);
            }
            catch
            {
            }

            NTexture texture = GetOrCreateMobileStateStateItemTexture(imageIndex);
            if (texture == null)
            {
                try
                {
                    layer.texture = null;
                    layer.url = string.Empty;
                }
                catch
                {
                }
                return;
            }

            System.Drawing.Point offset;
            try
            {
                offset = Libraries.StateItems.GetOffSet(imageIndex);
            }
            catch
            {
                offset = System.Drawing.Point.Empty;
            }

            try
            {
                layer.showErrorSign = false;
                layer.url = string.Empty;
                layer.texture = texture;
                layer.x = offset.X;
                layer.y = offset.Y;
            }
            catch
            {
            }
        }

        private static void ApplyPaperDollHairLayer(GLoader layer, UserObject user)
        {
            if (layer == null || layer._disposed)
                return;

            if (user == null)
            {
                try
                {
                    layer.texture = null;
                    layer.url = string.Empty;
                }
                catch
                {
                }
                return;
            }

            int hair = 441 + user.Hair + (user.Class == MirClass.Assassin ? 20 : 0) + (user.Gender == MirGender.Male ? 0 : 40);
            int offSetX = user.Class == MirClass.Assassin ? (user.Gender == MirGender.Male ? 6 : 4) : 0;
            int offSetY = user.Class == MirClass.Assassin ? (user.Gender == MirGender.Male ? 25 : 18) : 0;

            try
            {
                Libraries.Prguse.Touch(hair);
            }
            catch
            {
            }

            NTexture texture = GetOrCreateMobileStatePrguseTexture(hair);
            if (texture == null)
            {
                try
                {
                    layer.texture = null;
                    layer.url = string.Empty;
                }
                catch
                {
                }
                return;
            }

            System.Drawing.Point offset;
            try
            {
                offset = Libraries.Prguse.GetOffSet(hair);
            }
            catch
            {
                offset = System.Drawing.Point.Empty;
            }

            try
            {
                layer.showErrorSign = false;
                layer.url = string.Empty;
                layer.texture = texture;
                layer.x = offset.X + offSetX;
                layer.y = offset.Y + offSetY;
            }
            catch
            {
            }
        }

        private static NTexture GetOrCreateMobileStateStateItemTexture(int imageIndex)
        {
            if (imageIndex <= 0)
                return null;

            Texture2D texture;
            try
            {
                texture = Libraries.StateItems.GetTexture(imageIndex);
            }
            catch
            {
                texture = null;
            }

            if (texture == null || texture.IsDisposed)
                return null;

            if (MobileStateStateItemTextureCache.TryGetValue(imageIndex, out NTexture cached) && cached != null)
            {
                Texture2D native = cached.nativeTexture;
                if (native != null && !native.IsDisposed && ReferenceEquals(native, texture))
                    return cached;
            }

            NTexture created = new NTexture(texture);
            MobileStateStateItemTextureCache[imageIndex] = created;
            return created;
        }

        private static NTexture GetOrCreateMobileStatePrguseTexture(int imageIndex)
        {
            if (imageIndex <= 0)
                return null;

            Texture2D texture;
            try
            {
                texture = Libraries.Prguse.GetTexture(imageIndex);
            }
            catch
            {
                texture = null;
            }

            if (texture == null || texture.IsDisposed)
                return null;

            if (MobileStatePrguseTextureCache.TryGetValue(imageIndex, out NTexture cached) && cached != null)
            {
                Texture2D native = cached.nativeTexture;
                if (native != null && !native.IsDisposed && ReferenceEquals(native, texture))
                    return cached;
            }

            NTexture created = new NTexture(texture);
            MobileStatePrguseTextureCache[imageIndex] = created;
            return created;
        }

        private static string BuildMobileStateStatusText(UserObject user)
        {
            try
            {
                int maxHp = 0;
                int maxMp = 0;
                try { maxHp = user.Stats[Stat.HP]; } catch { maxHp = 0; }
                try { maxMp = user.Stats[Stat.MP]; } catch { maxMp = 0; }

                var builder = new StringBuilder(256);
                builder.Append("等级：").Append(user.Level).Append("  ");
                builder.Append("职业：").Append(user.Class).AppendLine();

                builder.Append("生命：").Append(user.HP).Append('/').Append(maxHp).Append("  ");
                builder.Append("魔法：").Append(user.MP).Append('/').Append(maxMp).AppendLine();

                int handMax = 0, wearMax = 0, bagMax = 0;
                try { handMax = user.Stats[Stat.HandWeight]; } catch { handMax = 0; }
                try { wearMax = user.Stats[Stat.WearWeight]; } catch { wearMax = 0; }
                try { bagMax = user.Stats[Stat.BagWeight]; } catch { bagMax = 0; }

                builder.Append("负重：手 ").Append(user.CurrentHandWeight).Append('/').Append(handMax).Append("  ");
                builder.Append("穿 ").Append(user.CurrentWearWeight).Append('/').Append(wearMax).Append("  ");
                builder.Append("包 ").Append(user.CurrentBagWeight).Append('/').Append(bagMax).AppendLine();

                builder.Append("经验：").Append(user.Experience).Append('/').Append(user.MaxExperience).AppendLine();
                builder.Append("攻速：").Append(user.AttackSpeed);

                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildMobileStateBaseAttribText(UserObject user)
        {
            try
            {
                int minAc = 0, maxAc = 0, minMac = 0, maxMac = 0;
                int minDc = 0, maxDc = 0, minMc = 0, maxMc = 0, minSc = 0, maxSc = 0;
                int accuracy = 0, agility = 0;

                try { minAc = user.Stats[Stat.MinAC]; } catch { minAc = 0; }
                try { maxAc = user.Stats[Stat.MaxAC]; } catch { maxAc = 0; }
                try { minMac = user.Stats[Stat.MinMAC]; } catch { minMac = 0; }
                try { maxMac = user.Stats[Stat.MaxMAC]; } catch { maxMac = 0; }
                try { minDc = user.Stats[Stat.MinDC]; } catch { minDc = 0; }
                try { maxDc = user.Stats[Stat.MaxDC]; } catch { maxDc = 0; }
                try { minMc = user.Stats[Stat.MinMC]; } catch { minMc = 0; }
                try { maxMc = user.Stats[Stat.MaxMC]; } catch { maxMc = 0; }
                try { minSc = user.Stats[Stat.MinSC]; } catch { minSc = 0; }
                try { maxSc = user.Stats[Stat.MaxSC]; } catch { maxSc = 0; }
                try { accuracy = user.Stats[Stat.Accuracy]; } catch { accuracy = 0; }
                try { agility = user.Stats[Stat.Agility]; } catch { agility = 0; }

                var builder = new StringBuilder(256);
                builder.Append("防御：").Append(minAc).Append('-').Append(maxAc).AppendLine();
                builder.Append("魔御：").Append(minMac).Append('-').Append(maxMac).AppendLine();
                builder.Append("攻击：").Append(minDc).Append('-').Append(maxDc).AppendLine();
                builder.Append("魔法：").Append(minMc).Append('-').Append(maxMc).AppendLine();
                builder.Append("道术：").Append(minSc).Append('-').Append(maxSc).AppendLine();
                builder.Append("准确：").Append(accuracy).Append("  ");
                builder.Append("敏捷：").Append(agility);

                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildMobileStateAdvanAttribText(UserObject user)
        {
            try
            {
                int attackSpeed = 0;
                try { attackSpeed = user.Stats[Stat.AttackSpeed]; } catch { attackSpeed = 0; }

                var builder = new StringBuilder(192);
                builder.Append("攻速加成：").Append(attackSpeed).AppendLine();

                // 先把常用项占位，后续按服务端/策划口径补齐字段
                builder.Append("（更多高级属性待对接）");

                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
