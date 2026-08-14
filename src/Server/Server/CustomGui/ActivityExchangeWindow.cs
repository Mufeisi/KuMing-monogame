using Server.MirObjects;
using Server.Scripting;
using Shared.CustomGui;
using C = ClientPackets;
using S = ServerPackets;

namespace Server.CustomGui;

/// <summary>
/// LEG-07 动态窗口样板：以角色持久化 Flag 1998 记录一次性兑换资格。
/// </summary>
public static class ActivityExchangeWindow
{
    public const int ClaimFlagIndex = Globals.FlagIndexCount - 1;
    public const uint GoldCost = 1_000;
    public const uint CreditReward = 10;

    public static void Register(
        CustomGuiScriptRegistry registry,
        Action<PlayerObject, C.CustomGuiAction, IReadOnlyList<CustomGuiStateEntry>> publishState = null,
        long packageSequence = 1)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (packageSequence <= 0) throw new ArgumentOutOfRangeException(nameof(packageSequence));
        publishState ??= PublishProductionState;

        registry.Register(new CustomGuiScriptWindowDefinition
        {
            DocumentId = CustomGuiActivityExchangeTemplate.DocumentId,
            DocumentRevision = 1,
            PackageSequence = packageSequence,
            InitialStateRevision = 1,
            Lifetime = TimeSpan.FromMinutes(10),
            ProvideState = (_, player) => BuildState(player),
            Actions =
            [
                new CustomGuiActionRule
                {
                    ActionId = CustomGuiActivityExchangeTemplate.SubmitActionId,
                    Action = CustomGuiActionKind.SubmitSelection,
                    MinimumSelections = 1,
                    MaximumSelections = 1,
                    AllowedSelections = new HashSet<string>(StringComparer.Ordinal)
                    {
                        CustomGuiActivityExchangeTemplate.OfferId
                    },
                    Currency = CustomGuiCurrencyKind.Gold,
                    CurrencyCost = GoldCost,
                    MaximumUsageCount = 1,
                    UsageCount = player => IsClaimed(player) ? 1 : 0,
                    Prepare = (player, action) => Prepare(player, action, publishState)
                }
            ]
        });
    }

    public static IReadOnlyList<CustomGuiStateEntry> BuildState(PlayerObject player)
    {
        EnsurePlayerFacts(player);
        bool claimed = IsClaimed(player);
        bool available = !claimed && player.Account.Gold >= GoldCost;
        string status = claimed
            ? "兑换已完成，本角色不可重复领取"
            : available ? "活动可用，请选择兑换项" : "金币不足，暂时无法兑换";
        return
        [
            CustomGuiStateEntry.Text("exchange.title", "限时兑换"),
            CustomGuiStateEntry.Text("exchange.status", status),
            CustomGuiStateEntry.Text("exchange.balance", $"金币：{player.Account.Gold}　信用点：{player.Account.Credit}"),
            CustomGuiStateEntry.List("exchange.options",
            [
                new CustomGuiStateListItem(
                    CustomGuiActivityExchangeTemplate.OfferId,
                    $"{GoldCost} 金币兑换 {CreditReward} 信用点",
                    "每个角色限一次",
                    string.Empty)
            ]),
            CustomGuiStateEntry.Progress("exchange.progress", claimed ? 1 : 0, 1),
            CustomGuiStateEntry.ButtonVisible("exchange.submit.visible", true),
            CustomGuiStateEntry.ButtonEnabled("exchange.submit.enabled", available)
        ];
    }

    private static ICustomGuiActionTransaction Prepare(
        PlayerObject player,
        C.CustomGuiAction action,
        Action<PlayerObject, C.CustomGuiAction, IReadOnlyList<CustomGuiStateEntry>> publishState)
    {
        EnsurePlayerFacts(player);
        if (IsClaimed(player))
            throw new InvalidOperationException("GUI12-EXCHANGE-USAGE：本角色已完成兑换");
        if (player.Account.Gold < GoldCost)
            throw new InvalidOperationException("GUI12-EXCHANGE-CURRENCY：金币余额不足");
        if (player.Account.Credit > uint.MaxValue - CreditReward)
            throw new InvalidOperationException("GUI12-EXCHANGE-CREDIT：信用点余额已达上限");

        uint originalGold = player.Account.Gold;
        uint originalCredit = player.Account.Credit;
        bool originalClaimed = player.Info.Flags[ClaimFlagIndex];
        return new CustomGuiDelegateTransaction(
            commit: () =>
            {
                if (player.Account.Gold != originalGold || player.Account.Credit != originalCredit ||
                    player.Info.Flags[ClaimFlagIndex] != originalClaimed)
                    throw new InvalidOperationException("GUI12-EXCHANGE-STALE：兑换事实已发生变化");
                player.Account.Gold = originalGold - GoldCost;
                player.Account.Credit = originalCredit + CreditReward;
                player.Info.Flags[ClaimFlagIndex] = true;
                publishState(player, action, BuildState(player));
                return "兑换成功：获得 10 信用点";
            },
            rollback: () =>
            {
                player.Account.Gold = originalGold;
                player.Account.Credit = originalCredit;
                player.Info.Flags[ClaimFlagIndex] = originalClaimed;
            });
    }

    private static void PublishProductionState(
        PlayerObject player,
        C.CustomGuiAction action,
        IReadOnlyList<CustomGuiStateEntry> state)
    {
        player.Connection.UpdateCustomGuiScriptState(
            action.WindowInstanceId, expectedStateRevision: 1, state.ToList());
        player.Enqueue(new S.LoseGold { Gold = GoldCost });
        player.Enqueue(new S.GainedCredit { Credit = CreditReward });
    }

    private static bool IsClaimed(PlayerObject player) =>
        player?.Info?.Flags != null && player.Info.Flags.Length > ClaimFlagIndex &&
        player.Info.Flags[ClaimFlagIndex];

    private static void EnsurePlayerFacts(PlayerObject player)
    {
        if (player?.Info?.Flags == null || player.Info.Flags.Length <= ClaimFlagIndex || player.Account == null)
            throw new InvalidOperationException("GUI12-EXCHANGE-PLAYER：玩家持久化事实不可用");
    }
}
