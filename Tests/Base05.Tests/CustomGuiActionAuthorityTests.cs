using Server.CustomGui;
using Server.MirDatabase;
using Server.MirObjects;
using Shared.CustomGui;
using C = ClientPackets;
using Xunit;

namespace Base05.Tests;

public sealed class CustomGuiActionAuthorityTests
{
    [Fact]
    public void ValidActionChecksServerFactsBeforeAtomicCommit()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-14T10:00:00Z");
        PlayerObject player = Player(gold: 100, itemIds: new ulong[] { 9001 });
        int usage = 0;
        int commitCalls = 0;
        var authority = new CustomGuiActionAuthority(() => now);
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange",
            ActionId = "exchange.submit",
            Action = CustomGuiActionKind.SubmitItems,
            MinimumSubmittedItems = 1,
            MaximumSubmittedItems = 1,
            ActiveFromUtc = now.AddMinutes(-1),
            ActiveUntilUtc = now.AddMinutes(1),
            Currency = CustomGuiCurrencyKind.Gold,
            CurrencyCost = 25,
            MaximumUsageCount = 1,
            UsageCount = _ => usage,
            Prepare = (_, _) => new CustomGuiDelegateTransaction(
                commit: () =>
                {
                    commitCalls++;
                    player.Account.Gold -= 25;
                    usage++;
                    return "兑换成功";
                },
                rollback: () =>
                {
                    player.Account.Gold += 25;
                    usage--;
                })
        });

        ServerPackets.CustomGuiActionResult result = authority.Handle(
            player,
            Action("activity.exchange", "exchange.submit", CustomGuiActionKind.SubmitItems, items: new long[] { 9001 }),
            stateRevision: 3);

        Assert.Equal(CustomGuiActionResultKind.Accepted, result.Result);
        Assert.Equal("兑换成功", result.Message);
        Assert.Equal((uint)75, player.Account.Gold);
        Assert.Equal(1, usage);
        Assert.Equal(1, commitCalls);
        Assert.Equal((uint)3, result.StateRevision);
    }

    [Fact]
    public void UnknownOrMismatchedActionNeverPreparesTransaction()
    {
        PlayerObject player = Player();
        int prepareCalls = 0;
        var authority = new CustomGuiActionAuthority();
        authority.Register(BasicRule("known", CustomGuiActionKind.RequestAction, () => prepareCalls++));

        ServerPackets.CustomGuiActionResult unknown = authority.Handle(
            player, Action("activity.exchange", "missing", CustomGuiActionKind.RequestAction), 1);
        ServerPackets.CustomGuiActionResult wrongKind = authority.Handle(
            player, Action("activity.exchange", "known", CustomGuiActionKind.SubmitText, text: "x"), 1);

        Assert.Equal(CustomGuiActionResultKind.Rejected, unknown.Result);
        Assert.Contains("GUI09-AUTH-ACTION", unknown.Message);
        Assert.Equal(CustomGuiActionResultKind.Invalid, wrongKind.Result);
        Assert.Contains("GUI09-AUTH-KIND", wrongKind.Message);
        Assert.Equal(0, prepareCalls);
    }

    [Fact]
    public void TextAndSelectionRulesRejectTamperingBeforeCommit()
    {
        PlayerObject player = Player();
        int commits = 0;
        var authority = new CustomGuiActionAuthority();
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "text", Action = CustomGuiActionKind.SubmitText,
            MinimumTextCharacters = 2, MaximumTextCharacters = 4,
            TextValidator = value => value.All(char.IsLetter),
            Prepare = (_, _) => Transaction(() => commits++)
        });
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "select", Action = CustomGuiActionKind.SubmitSelection,
            MinimumSelections = 1, MaximumSelections = 1,
            AllowedSelections = new HashSet<string>(StringComparer.Ordinal) { "reward.a", "reward.b" },
            Prepare = (_, _) => Transaction(() => commits++)
        });

        ServerPackets.CustomGuiActionResult badText = authority.Handle(
            player, Action("activity.exchange", "text", CustomGuiActionKind.SubmitText, text: "A1"), 1);
        ServerPackets.CustomGuiActionResult longText = authority.Handle(
            player, Action("activity.exchange", "text", CustomGuiActionKind.SubmitText, text: "ABCDE"), 1);
        ServerPackets.CustomGuiActionResult badSelection = authority.Handle(
            player, Action("activity.exchange", "select", CustomGuiActionKind.SubmitSelection, selections: new[] { "reward.forged" }), 1);

        Assert.All(new[] { badText, longText }, result => Assert.Contains("GUI09-AUTH-TEXT", result.Message));
        Assert.Contains("GUI09-AUTH-SELECTION", badSelection.Message);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void ItemOwnershipNpcAndDeadPlayerChecksFailClosed()
    {
        PlayerObject player = Player(itemIds: new ulong[] { 100 });
        int commits = 0;
        var authority = new CustomGuiActionAuthority();
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "item", Action = CustomGuiActionKind.SubmitItems,
            MinimumSubmittedItems = 1, MaximumSubmittedItems = 1,
            Prepare = (_, _) => Transaction(() => commits++)
        });
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "npc", Action = CustomGuiActionKind.RequestAction,
            RequiredNpcInfoIndex = 77, MaximumNpcDistance = 3,
            Prepare = (_, _) => Transaction(() => commits++)
        });

        ServerPackets.CustomGuiActionResult forgedItem = authority.Handle(
            player, Action("activity.exchange", "item", CustomGuiActionKind.SubmitItems, items: new long[] { 999 }), 1);
        ServerPackets.CustomGuiActionResult missingNpc = authority.Handle(
            player, Action("activity.exchange", "npc", CustomGuiActionKind.RequestAction), 1);
        player.Dead = true;
        ServerPackets.CustomGuiActionResult dead = authority.Handle(
            player, Action("activity.exchange", "item", CustomGuiActionKind.SubmitItems, items: new long[] { 100 }), 1);

        Assert.Contains("GUI09-AUTH-ITEM", forgedItem.Message);
        Assert.Contains("GUI09-AUTH-NPC", missingNpc.Message);
        Assert.Contains("GUI09-AUTH-PLAYER", dead.Message);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void ActivityCurrencyAndUsageAreReadFromServerRuleAndPlayerState()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-14T10:00:00Z");
        PlayerObject player = Player(gold: 9);
        int usage = 1;
        int commits = 0;
        var authority = new CustomGuiActionAuthority(() => now);
        CustomGuiActionRule rule = BasicRule("submit", CustomGuiActionKind.RequestAction, () => commits++);
        rule.ActiveFromUtc = now.AddMinutes(-1);
        rule.ActiveUntilUtc = now.AddMinutes(1);
        rule.Currency = CustomGuiCurrencyKind.Gold;
        rule.CurrencyCost = 10;
        rule.MaximumUsageCount = 1;
        rule.UsageCount = _ => usage;
        authority.Register(rule);

        ServerPackets.CustomGuiActionResult currency = authority.Handle(
            player, Action("activity.exchange", "submit", CustomGuiActionKind.RequestAction), 1);
        player.Account.Gold = 10;
        ServerPackets.CustomGuiActionResult count = authority.Handle(
            player, Action("activity.exchange", "submit", CustomGuiActionKind.RequestAction), 1);
        usage = 0;
        now = now.AddMinutes(2);
        ServerPackets.CustomGuiActionResult expired = authority.Handle(
            player, Action("activity.exchange", "submit", CustomGuiActionKind.RequestAction), 1);

        Assert.Contains("GUI09-AUTH-CURRENCY", currency.Message);
        Assert.Contains("GUI09-AUTH-USAGE", count.Message);
        Assert.Contains("GUI09-AUTH-ACTIVITY", expired.Message);
        Assert.Equal(0, commits);
    }

    [Fact]
    public void CommitFailureRollsBackPlayerFactsAndReturnsBoundedError()
    {
        PlayerObject player = Player(gold: 50);
        string? observedCode = null;
        Type? observedType = null;
        var authority = new CustomGuiActionAuthority(errorSink: (code, error) =>
        {
            observedCode = code;
            observedType = error.GetType();
        });
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "rollback", Action = CustomGuiActionKind.RequestAction,
            Prepare = (_, _) => new CustomGuiDelegateTransaction(
                commit: () =>
                {
                    player.Account.Gold -= 20;
                    throw new InvalidOperationException("提交失败");
                },
                rollback: () => player.Account.Gold += 20)
        });

        ServerPackets.CustomGuiActionResult result = authority.Handle(
            player, Action("activity.exchange", "rollback", CustomGuiActionKind.RequestAction), 1);

        Assert.Equal(CustomGuiActionResultKind.Rejected, result.Result);
        Assert.Contains("GUI09-AUTH-TRANSACTION", result.Message);
        Assert.Equal((uint)50, player.Account.Gold);
        Assert.Equal("GUI09-AUTH-TRANSACTION", observedCode);
        Assert.Equal(typeof(InvalidOperationException), observedType);
    }

    [Fact]
    public void RegistrationSnapshotsWhitelistAndRejectsRuleDowngrade()
    {
        PlayerObject player = Player();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "reward.a" };
        var authority = new CustomGuiActionAuthority();
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", DocumentRevision = 2, PackageSequence = 5,
            ActionId = "select", Action = CustomGuiActionKind.SubmitSelection,
            MinimumSelections = 1, MaximumSelections = 1, AllowedSelections = allowed,
            Prepare = (_, _) => Transaction(() => { })
        });
        allowed.Add("reward.forged");

        C.CustomGuiAction forged = Action("activity.exchange", "select", CustomGuiActionKind.SubmitSelection,
            selections: new[] { "reward.forged" });
        forged.DocumentRevision = 2;
        forged.PackageSequence = 5;
        Assert.Contains("GUI09-AUTH-SELECTION", authority.Handle(player, forged, 1).Message);

        CustomGuiActionRule downgrade = new()
        {
            DocumentId = "activity.exchange", DocumentRevision = 1, PackageSequence = 5,
            ActionId = "select", Action = CustomGuiActionKind.SubmitSelection,
            MinimumSelections = 1, MaximumSelections = 1, AllowedSelections = allowed,
            Prepare = (_, _) => Transaction(() => { })
        };
        Assert.Throws<InvalidOperationException>(() => authority.Register(downgrade));

        downgrade.PackageSequence = 6;
        authority.Register(downgrade);
        Assert.Equal(CustomGuiActionResultKind.Stale, authority.Handle(player, forged, 1).Result);
    }

    [Fact]
    public void SessionGateDispatchesAcceptedActionThroughAuthority()
    {
        long nowMilliseconds = 100_000;
        PlayerObject player = Player();
        int commits = 0;
        List<Packet> sent = new();
        var authority = new CustomGuiActionAuthority();
        authority.Register(BasicRule("submit", CustomGuiActionKind.RequestAction, () => commits++));
        var sessions = new CustomGuiSessionController(
            sent.Add,
            () => true,
            () => nowMilliseconds,
            () => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            () => 99,
            (action, revision) => authority.Handle(player, action, revision));
        ServerPackets.CustomGuiOpen opened = sessions.Open(
            "activity.exchange", 1, 1, nowMilliseconds + 1_000, 4, new());
        sent.Clear();
        C.CustomGuiAction action = Action("activity.exchange", "submit", CustomGuiActionKind.RequestAction);
        action.WindowInstanceId = opened.WindowInstanceId;
        action.SessionNonce = opened.SessionNonce;

        CustomGuiSessionDecision decision = sessions.Handle(action);

        Assert.True(decision.Accepted);
        ServerPackets.CustomGuiActionResult result = Assert.IsType<ServerPackets.CustomGuiActionResult>(Assert.Single(sent));
        Assert.Equal(CustomGuiActionResultKind.Accepted, result.Result);
        Assert.Equal((uint)4, result.StateRevision);
        Assert.Equal(1, commits);
    }

    [Fact]
    public void InvalidSuccessMessageRollsBackCommittedState()
    {
        PlayerObject player = Player(gold: 50);
        var authority = new CustomGuiActionAuthority();
        authority.Register(new CustomGuiActionRule
        {
            DocumentId = "activity.exchange", ActionId = "message", Action = CustomGuiActionKind.RequestAction,
            Prepare = (_, _) => new CustomGuiDelegateTransaction(
                commit: () =>
                {
                    player.Account.Gold -= 10;
                    return new string('x', CustomGuiProtocolLimits.MaximumMessageCharacters + 1);
                },
                rollback: () => player.Account.Gold += 10)
        });

        ServerPackets.CustomGuiActionResult result = authority.Handle(
            player, Action("activity.exchange", "message", CustomGuiActionKind.RequestAction), 1);

        Assert.Equal(CustomGuiActionResultKind.Rejected, result.Result);
        Assert.Contains("GUI09-AUTH-TRANSACTION", result.Message);
        Assert.Equal((uint)50, player.Account.Gold);
    }

    private static CustomGuiActionRule BasicRule(string actionId, CustomGuiActionKind kind, Action committed) => new()
    {
        DocumentId = "activity.exchange",
        ActionId = actionId,
        Action = kind,
        Prepare = (_, _) => Transaction(committed)
    };

    private static CustomGuiDelegateTransaction Transaction(Action committed) =>
        new(() => { committed(); return string.Empty; }, () => { });

    private static PlayerObject Player(uint gold = 0, IEnumerable<ulong>? itemIds = null)
    {
        var player = new PlayerObject
        {
            Info = new CharacterInfo { Name = "测试玩家" },
            Account = new AccountInfo { Gold = gold }
        };
        if (itemIds != null)
        {
            int index = 0;
            foreach (ulong itemId in itemIds)
                player.Info.Inventory[index++] = new UserItem(new ItemInfo { Index = index, Name = "测试物品" }) { UniqueID = itemId };
        }
        return player;
    }

    private static C.CustomGuiAction Action(
        string documentId,
        string actionId,
        CustomGuiActionKind kind,
        string text = "",
        IEnumerable<string>? selections = null,
        IEnumerable<long>? items = null) => new()
    {
        WindowInstanceId = 1,
        DocumentId = documentId,
        DocumentRevision = 1,
        PackageSequence = 1,
        SessionNonce = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        RequestSequence = 1,
        Action = kind,
        ActionId = actionId,
        TextValue = text,
        SelectionIds = selections?.ToList() ?? new(),
        ItemIds = items?.ToList() ?? new()
    };
}
