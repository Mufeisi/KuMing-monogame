using Server.MirDatabase;

namespace Server.Scripting
{
    internal enum LingFengShopCurrency
    {
        Credit = 0,
        Gold = 1,
        UnsupportedCurrency3 = 3,
        UnsupportedCurrency4 = 4
    }

    internal sealed record LingFengRecipeIngredient(string ItemName, ushort Count);

    internal sealed record LingFengRecipeDefinition(
        string OutputItemName,
        IReadOnlyList<LingFengRecipeIngredient> Ingredients);

    internal sealed record LingFengShopProductDefinition(
        int Category,
        string DisplayName,
        int ItemIndex,
        uint UnitPrice,
        LingFengShopCurrency Currency,
        ushort Count,
        string Description,
        bool Enabled,
        bool IndividualStock,
        int Stock);

    internal sealed class LingFengCommerceSnapshot
    {
        internal LingFengCommerceSnapshot(
            IRecipeProvider recipeProvider,
            IReadOnlyList<GameShopItem> gameShopItems)
        {
            RecipeProvider = recipeProvider;
            GameShopItems = gameShopItems;
        }

        internal IRecipeProvider RecipeProvider { get; }
        internal IReadOnlyList<GameShopItem> GameShopItems { get; }
    }

    internal sealed class LingFengCommerceContentProvider
    {
        private readonly LingFengRecipeDefinition[] _recipes;
        private readonly LingFengShopProductDefinition[] _shopProducts;
        private readonly bool _hasShopSource;
        private readonly bool _hasRecipeSource;
        private readonly IReadOnlyList<string> _compatibilityDiagnostics;

        private LingFengCommerceContentProvider(
            IEnumerable<LingFengRecipeDefinition> recipes,
            IEnumerable<LingFengShopProductDefinition> shopProducts,
            bool hasShopSource,
            bool hasRecipeSource)
        {
            _recipes = recipes.ToArray();
            _shopProducts = shopProducts.ToArray();
            _hasShopSource = hasShopSource;
            _hasRecipeSource = hasRecipeSource;
            var compatibilityDiagnostics = new List<string>();
            int virtualProductCount = _shopProducts.Count(value => value.ItemIndex == 0);
            int unsupportedCurrency3Count = _shopProducts.Count(value =>
                value.Currency == LingFengShopCurrency.UnsupportedCurrency3);
            int unsupportedCurrency4Count = _shopProducts.Count(value =>
                value.Currency == LingFengShopCurrency.UnsupportedCurrency4);
            if (virtualProductCount > 0)
                compatibilityDiagnostics.Add(
                    $"LFENV13-SHOP-VIRTUAL：保留 {virtualProductCount} 个 ItemIndex=0 服务型商品事实，当前物品商城不激活购买。");
            if (unsupportedCurrency3Count > 0)
                compatibilityDiagnostics.Add(
                    $"LFENV13-SHOP-CURRENCY3：保留 {unsupportedCurrency3Count} 个货币类型 3 商品事实，当前缺少等价货币模型，不激活购买。");
            if (unsupportedCurrency4Count > 0)
                compatibilityDiagnostics.Add(
                    $"LFENV13-SHOP-CURRENCY4：保留 {unsupportedCurrency4Count} 个货币类型 4 商品事实，当前缺少等价货币模型，不激活购买。");
            _compatibilityDiagnostics = compatibilityDiagnostics.AsReadOnly();
        }

        internal IReadOnlyList<LingFengRecipeDefinition> Recipes => Array.AsReadOnly(_recipes);
        internal IReadOnlyList<LingFengShopProductDefinition> ShopProducts => Array.AsReadOnly(_shopProducts);
        internal bool HasShopSource => _hasShopSource;
        internal bool HasRecipeSource => _hasRecipeSource;
        internal IReadOnlyList<string> CompatibilityDiagnostics => _compatibilityDiagnostics;

        internal IEnumerable<LingFengDependencyRequirement> GetDependencyRequirements()
        {
            foreach (LingFengRecipeDefinition recipe in _recipes)
            {
                yield return new LingFengDependencyRequirement(
                    LingFengDependencyKind.ItemName, recipe.OutputItemName, LingFengDependencyLevel.E1,
                    "Commerce/MakeItem");
                foreach (LingFengRecipeIngredient ingredient in recipe.Ingredients)
                    yield return new LingFengDependencyRequirement(
                        LingFengDependencyKind.ItemName, ingredient.ItemName, LingFengDependencyLevel.E1,
                        "Commerce/MakeItem");
            }
            foreach (LingFengShopProductDefinition product in _shopProducts)
            {
                if (product.ItemIndex > 0)
                    yield return new LingFengDependencyRequirement(
                        LingFengDependencyKind.ItemIndex, product.ItemIndex.ToString(), LingFengDependencyLevel.E1,
                        "Commerce/ShopItemList");
                if (product.Currency is LingFengShopCurrency.UnsupportedCurrency3 or
                    LingFengShopCurrency.UnsupportedCurrency4)
                    yield return new LingFengDependencyRequirement(
                        LingFengDependencyKind.DomainAdapter,
                        $"LingFeng/ShopCurrency/{(int)product.Currency}", LingFengDependencyLevel.E2,
                        "Commerce/ShopItemList");
            }
        }

        internal static bool TryCreate(
            TextFileDefinition shopItems,
            TextFileDefinition makeItems,
            out LingFengCommerceContentProvider provider,
            out IReadOnlyList<string> errors)
        {
            var diagnostics = new List<string>();
            List<LingFengRecipeDefinition> recipes = ParseRecipes(makeItems, diagnostics);
            List<LingFengShopProductDefinition> products = ParseShopProducts(shopItems, diagnostics);
            if (diagnostics.Count > 0)
            {
                provider = null;
                errors = diagnostics.AsReadOnly();
                return false;
            }
            provider = new LingFengCommerceContentProvider(
                recipes, products, shopItems != null, makeItems != null);
            errors = Array.Empty<string>();
            return true;
        }

        internal bool TryBuildSnapshot(
            Func<string, ItemInfo> itemByName,
            Func<int, ItemInfo> itemByIndex,
            out LingFengCommerceSnapshot snapshot,
            out IReadOnlyList<string> errors)
        {
            var diagnostics = new List<string>();
            var recipeDefinitions = new Dictionary<string, RecipeDefinition>(StringComparer.Ordinal);
            foreach (LingFengRecipeDefinition source in _recipes)
            {
                ItemInfo output = itemByName?.Invoke(source.OutputItemName);
                if (output == null)
                {
                    diagnostics.Add($"LFENV13-DEPENDENCY：配方产出物品不存在：{source.OutputItemName}");
                    continue;
                }
                var definition = new RecipeDefinition("Recipe/" + source.OutputItemName)
                {
                    OutputItemName = source.OutputItemName
                };
                foreach (LingFengRecipeIngredient ingredient in source.Ingredients)
                {
                    if (itemByName?.Invoke(ingredient.ItemName) == null)
                    {
                        diagnostics.Add($"LFENV13-DEPENDENCY：配方 {source.OutputItemName} 的材料不存在：{ingredient.ItemName}");
                        continue;
                    }
                    definition.Ingredients.Add(new RecipeIngredientDefinition(
                        ingredient.ItemName, ingredient.Count));
                }
                recipeDefinitions.Add(definition.Key, definition);
            }

            var gameShopItems = new List<GameShopItem>();
            int gameShopIndex = 1;
            foreach (LingFengShopProductDefinition source in _shopProducts)
            {
                if (!source.Enabled || source.ItemIndex == 0 ||
                    source.Currency == LingFengShopCurrency.UnsupportedCurrency3 ||
                    source.Currency == LingFengShopCurrency.UnsupportedCurrency4) continue;
                ItemInfo item = itemByIndex?.Invoke(source.ItemIndex);
                if (item == null)
                {
                    diagnostics.Add($"LFENV13-DEPENDENCY：商城物品索引不存在：{source.ItemIndex}（{source.DisplayName}）");
                    continue;
                }
                if (!string.Equals(item.Name, source.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.FriendlyName, source.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"LFENV13-DEPENDENCY：商城物品索引 {source.ItemIndex} 与名称 {source.DisplayName} 不一致。");
                    continue;
                }
                gameShopItems.Add(new GameShopItem
                {
                    ItemIndex = item.Index,
                    GIndex = gameShopIndex++,
                    Info = item,
                    GoldPrice = source.Currency == LingFengShopCurrency.Gold ? source.UnitPrice : 0,
                    CreditPrice = source.Currency == LingFengShopCurrency.Credit ? source.UnitPrice : 0,
                    Count = source.Count,
                    Class = source.Category.ToString(),
                    Category = source.Description,
                    Stock = source.Stock,
                    iStock = source.IndividualStock,
                    Date = DateTime.MinValue,
                    CanBuyGold = source.Currency == LingFengShopCurrency.Gold,
                    CanBuyCredit = source.Currency == LingFengShopCurrency.Credit
                });
            }

            if (diagnostics.Count > 0)
            {
                snapshot = null;
                errors = diagnostics.AsReadOnly();
                return false;
            }
            snapshot = new LingFengCommerceSnapshot(
                new CSharpRecipeProvider(recipeDefinitions),
                Array.AsReadOnly(gameShopItems.ToArray()));
            errors = Array.Empty<string>();
            return true;
        }

        private static List<LingFengRecipeDefinition> ParseRecipes(
            TextFileDefinition source,
            ICollection<string> errors)
        {
            var result = new List<LingFengRecipeDefinition>();
            if (source == null) return result;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string output = null;
            List<LingFengRecipeIngredient> ingredients = null;
            foreach (string raw in source.Lines)
            {
                string line = Clean(raw);
                if (line.Length == 0) continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    FlushRecipe(output, ingredients, result, errors);
                    output = line[1..^1].Trim();
                    ingredients = new List<LingFengRecipeIngredient>();
                    if (output.Length == 0)
                        errors.Add("LFENV13-RECIPE-SYNTAX：配方产出名称不能为空。");
                    else if (!names.Add(output))
                        errors.Add($"LFENV13-RECIPE-DUPLICATE：重复配方：{output}");
                    continue;
                }
                if (output == null)
                {
                    errors.Add($"LFENV13-RECIPE-SYNTAX：材料行缺少配方节：{line}");
                    continue;
                }
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !ushort.TryParse(fields[^1], out ushort count) || count == 0)
                {
                    errors.Add($"LFENV13-RECIPE-SYNTAX：材料格式无效：{line}");
                    continue;
                }
                string itemName = string.Join(' ', fields[..^1]).Trim();
                if (itemName.Length == 0 || ingredients.Any(value =>
                        value.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"LFENV13-RECIPE-DUPLICATE：配方 {output} 的材料重复或为空：{itemName}");
                    continue;
                }
                ingredients.Add(new LingFengRecipeIngredient(itemName, count));
            }
            FlushRecipe(output, ingredients, result, errors);
            return result;
        }

        private static void FlushRecipe(
            string output,
            List<LingFengRecipeIngredient> ingredients,
            ICollection<LingFengRecipeDefinition> result,
            ICollection<string> errors)
        {
            if (output == null) return;
            if (ingredients == null || ingredients.Count == 0)
            {
                errors.Add($"LFENV13-RECIPE-SYNTAX：配方没有材料：{output}");
                return;
            }
            result.Add(new LingFengRecipeDefinition(output,
                Array.AsReadOnly(ingredients.ToArray())));
        }

        private static List<LingFengShopProductDefinition> ParseShopProducts(
            TextFileDefinition source,
            ICollection<string> errors)
        {
            var result = new List<LingFengShopProductDefinition>();
            if (source == null) return result;
            foreach (string raw in source.Lines)
            {
                string line = Clean(raw);
                if (line.Length == 0) continue;
                string[] fields = line.Split('\t');
                if (fields.Length != 10 ||
                    !int.TryParse(fields[0].Trim(), out int category) || category < 0 ||
                    string.IsNullOrWhiteSpace(fields[1]) ||
                    !int.TryParse(fields[2].Trim(), out int itemIndex) || itemIndex < 0 ||
                    !ushort.TryParse(fields[5].Trim(), out ushort count) || count == 0 ||
                    !TryFlag(fields[7], out bool enabled) ||
                    !TryFlag(fields[8], out bool individualStock) ||
                    !int.TryParse(fields[9].Trim(), out int stock) || stock < 0)
                {
                    errors.Add($"LFENV13-SHOP-SYNTAX：商城行必须是合法的 10 列记录：{line}");
                    continue;
                }
                string[] price = fields[3].Split('|');
                if (price.Length != 2 || !uint.TryParse(price[0].Trim(), out uint unitPrice) ||
                    !int.TryParse(price[1].Trim(), out int currencyValue))
                {
                    errors.Add($"LFENV13-SHOP-SYNTAX：商城价格格式无效：{fields[3]}");
                    continue;
                }
                if (!Enum.IsDefined(typeof(LingFengShopCurrency), currencyValue))
                {
                    errors.Add($"LFENV13-SHOP-CURRENCY：未知商城货币类型：{currencyValue}");
                    continue;
                }
                result.Add(new LingFengShopProductDefinition(
                    category, fields[1].Trim(), itemIndex, unitPrice,
                    (LingFengShopCurrency)currencyValue, count, fields[6].Trim(),
                    enabled, individualStock, stock));
            }
            return result;
        }

        private static string Clean(string raw)
        {
            string line = (raw ?? string.Empty).Trim();
            return line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal)
                ? string.Empty
                : line;
        }

        private static bool TryFlag(string value, out bool result)
        {
            result = false;
            if (!int.TryParse(value.Trim(), out int number) || number is < 0 or > 1) return false;
            result = number == 1;
            return true;
        }
    }
}
