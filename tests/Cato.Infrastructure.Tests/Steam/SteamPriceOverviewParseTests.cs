using System.Text.Json;
using Cato.Infrastructure.Steam;

namespace Cato.Infrastructure.Tests.Steam;

/// <summary>
/// Covers <see cref="SteamApiService.ParsePriceOverviews"/> on the shapes Steam's app details
/// answer has with <c>filters=price_overview</c> for many apps (seen 2026-10-27).
/// </summary>
public class SteamPriceOverviewParseTests
{
    [Fact]
    public void Only_apps_with_a_price_overview_get_a_price()
    {
        const string json = """
            {
              "10": {"success": true, "data": {"price_overview": {"currency": "USD", "initial": 999, "final": 499, "discount_percent": 50, "initial_formatted": "$9.99", "final_formatted": "$4.99"}}},
              "20": {"success": true, "data": []},
              "30": {"success": false}
            }
            """;

        var prices = SteamApiService.ParsePriceOverviews(json);

        var price = Assert.Single(prices);
        Assert.Equal(10, price.Key);
        Assert.Equal("USD", price.Value.Currency);
        Assert.Equal(9.99m, price.Value.InitialUsd);
        Assert.Equal(4.99m, price.Value.FinalUsd);
        Assert.Equal(50, price.Value.DiscountPercent);
    }

    [Fact]
    public void An_answer_that_is_not_an_object_is_an_error()
    {
        Assert.ThrowsAny<JsonException>(() => SteamApiService.ParsePriceOverviews("null"));
    }
}
