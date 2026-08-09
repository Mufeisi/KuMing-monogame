using MonoShare.MirScenes;
using Xunit;

namespace Base05.Tests;

public sealed class GameShopStateTests
{
    [Fact]
    public void Info_packets_upsert_by_game_shop_index()
    {
        var state = new GameShopState();
        var first = CreateItem(gIndex: 42, stock: 8);
        var refreshed = CreateItem(gIndex: 42, stock: 3);

        Assert.True(state.ApplyInfo(first, first.Stock));
        Assert.True(state.ApplyInfo(refreshed, refreshed.Stock));

        Assert.Equal(1, state.Count);
        Assert.Same(refreshed, state[0]);
        Assert.Equal(3, state[0].Stock);
    }

    [Fact]
    public void Stock_delta_updates_then_removes_product_at_zero()
    {
        var state = new GameShopState();
        var item = CreateItem(gIndex: 7, stock: 8);
        state.ApplyInfo(item, item.Stock);

        Assert.True(state.ApplyStock(item.Info.Index, 2));
        Assert.Equal(2, state[0].Stock);
        Assert.True(state.ApplyStock(item.Info.Index, 0));
        Assert.Equal(0, state.Count);
        Assert.False(state.ApplyStock(item.Info.Index, 1));
    }

    [Fact]
    public void Invalid_info_and_unknown_stock_do_not_mutate_empty_state()
    {
        var state = new GameShopState();

        Assert.False(state.ApplyInfo(null, 1));
        Assert.False(state.ApplyInfo(new GameShopItem(), 1));
        Assert.False(state.ApplyStock(404, 1));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void Clear_returns_shop_to_empty_state_for_empty_refresh()
    {
        var state = new GameShopState();
        var item = CreateItem(gIndex: 12, stock: 1);
        state.ApplyInfo(item, item.Stock);

        state.Clear();

        Assert.Equal(0, state.Count);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void Session_reset_discards_products_from_previous_login()
    {
        var state = new GameShopState();
        var item = CreateItem(gIndex: 21, stock: 4);
        state.ApplyInfo(item, item.Stock);

        state.ResetForSession();

        Assert.Equal(0, state.Count);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void Stock_delta_uses_item_info_index_only_when_identifiers_collide()
    {
        var state = new GameShopState();
        var target = CreateItem(gIndex: 101, stock: 8, infoIndex: 500);
        var collision = CreateItem(gIndex: 500, stock: 9, infoIndex: 600);
        state.ApplyInfo(target, target.Stock);
        state.ApplyInfo(collision, collision.Stock);

        Assert.True(state.ApplyStock(500, 2));

        Assert.Equal(2, target.Stock);
        Assert.Equal(9, collision.Stock);
    }

    [Theory]
    [InlineData(false, 0, false, 0, MobileShopPaymentOptions.None)]
    [InlineData(true, 10, false, 0, MobileShopPaymentOptions.CreditOnly)]
    [InlineData(false, 0, true, 20, MobileShopPaymentOptions.GoldOnly)]
    [InlineData(true, 10, true, 20, MobileShopPaymentOptions.CreditAndGold)]
    [InlineData(true, 0, true, 20, MobileShopPaymentOptions.GoldOnly)]
    public void Payment_policy_requires_an_explicit_choice_for_dual_currency_items(
        bool canCredit,
        uint creditPrice,
        bool canGold,
        uint goldPrice,
        MobileShopPaymentOptions expected)
    {
        GameShopItem item = CreateItem(gIndex: 1, stock: 1);
        item.CanBuyCredit = canCredit;
        item.CreditPrice = creditPrice;
        item.CanBuyGold = canGold;
        item.GoldPrice = goldPrice;

        Assert.Equal(expected, MobileShopPurchasePolicy.GetPaymentOptions(item));
    }

    [Theory]
    [InlineData(0, 15, 0)]
    [InlineData(1, 15, 0)]
    [InlineData(15, 15, 0)]
    [InlineData(16, 15, 1)]
    [InlineData(30, 15, 1)]
    [InlineData(31, 15, 2)]
    public void Fixed_grid_calculates_the_last_page(int count, int pageSize, int expected)
    {
        Assert.Equal(expected, MobileShopPurchasePolicy.GetLastPage(count, pageSize));
    }

    [Theory]
    [InlineData(0, 0, 15, 16, 0)]
    [InlineData(0, 14, 15, 16, 14)]
    [InlineData(1, 0, 15, 16, 15)]
    [InlineData(1, 1, 15, 16, -1)]
    [InlineData(-1, 0, 15, 16, -1)]
    [InlineData(0, 15, 15, 16, -1)]
    public void Fixed_grid_maps_only_visible_slots(int page, int slot, int pageSize, int count, int expected)
    {
        Assert.Equal(expected, MobileShopPurchasePolicy.GetItemIndex(page, slot, pageSize, count));
    }

    private static GameShopItem CreateItem(int gIndex, int stock, int? infoIndex = null)
    {
        return new GameShopItem
        {
            GIndex = gIndex,
            ItemIndex = gIndex + 1000,
            Info = new ItemInfo { Index = infoIndex ?? gIndex + 2000, Name = "商品" },
            Stock = stock,
        };
    }
}
