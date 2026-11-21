using System;
using System.Collections.Generic;
using System.Linq;

namespace knkwebapi_v2.Services.Shops
{
    /// <summary>Where a price came from.</summary>
    public enum ShopPriceSource
    {
        /// <summary>No price: a blueprint without a base price and no static price (not for sale, not bought).</summary>
        None,
        Static,
        Formula,
    }

    public readonly record struct ShopPrice(long? UnitPrice, ShopPriceSource Source);

    /// <summary>
    /// The interim pricing of shops DESIGN § 4, until the supply-and-demand model (#120):
    /// <code>
    /// sell price   = static catalogue price, when an admin set one
    ///              | round(base price × shop tax modifier × shop level modifier)
    /// base price   = midpoint of BasePriceMin and BasePriceMax (F1)
    /// unique items = base price × (1 + Σ enchantment level × percent per level / 100) (F3), then the same modifiers
    /// buy-back     = floor(sell price × BuyBackPercent / 100)                         (default 50%)
    /// </code>
    /// Coins are whole numbers: a formula price is rounded half away from zero and is at least 1 when the base price is
    /// above zero; a buy-back is rounded down (the town never pays more than the percentage).
    /// </summary>
    public static class ShopPricing
    {
        public static decimal BasePrice(decimal basePriceMin, decimal basePriceMax)
        {
            decimal lo = Math.Max(0m, Math.Min(basePriceMin, basePriceMax));
            decimal hi = Math.Max(0m, Math.Max(basePriceMin, basePriceMax));
            return (lo + hi) / 2m;
        }

        /// <summary>The enchantment factor of a unique item: 1 + Σ levels × percent / 100.</summary>
        public static decimal EnchantmentFactor(IEnumerable<int> enchantmentLevels, decimal percentPerLevel) =>
            1m + enchantmentLevels.Where(l => l > 0).Sum(l => (decimal)l) * Math.Max(0m, percentPerLevel) / 100m;

        public static ShopPrice SellPrice(long? staticPrice, decimal basePrice, decimal taxModifier, decimal levelMultiplier,
            decimal enchantmentFactor = 1m)
        {
            if (staticPrice is > 0) return new ShopPrice(staticPrice, ShopPriceSource.Static);
            decimal raw = basePrice * enchantmentFactor * taxModifier * levelMultiplier;
            if (basePrice <= 0m || raw <= 0m) return new ShopPrice(null, ShopPriceSource.None);
            long price = (long)Math.Max(1m, Math.Round(raw, MidpointRounding.AwayFromZero));
            return new ShopPrice(price, ShopPriceSource.Formula);
        }

        public static long BuyBack(long sellPrice, decimal buyBackPercent) =>
            (long)Math.Floor(sellPrice * Math.Clamp(buyBackPercent, 0m, 100m) / 100m);
    }
}
