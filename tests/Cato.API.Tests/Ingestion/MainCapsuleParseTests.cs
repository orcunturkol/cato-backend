using Cato.Infrastructure.Steam;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="SteamApiService.ParseMainCapsuleUrls"/> on the shapes of
/// IStoreBrowseService/GetItems answers.
/// </summary>
public class MainCapsuleParseTests
{
    private const string Cdn = "https://shared.akamai.steamstatic.com/store_item_assets/";

    private static string Answer(string assets) =>
        """{"response":{"store_items":[{"appid":10,"assets":{""" + assets + "}}]}}";

    private static string? Parse(string json) => SteamApiService.ParseMainCapsuleUrls(json).GetValueOrDefault(10);

    [Fact]
    public void Each_app_in_a_batch_gets_its_own_capsule_and_apps_without_one_are_left_out()
    {
        var json = """
            {"response":{"store_items":[
              {"appid":10,"assets":{"asset_url_format":"steam/apps/10/${FILENAME}","main_capsule":"a/capsule_616x353.jpg"}},
              {"appid":11,"success":2},
              {"appid":12,"assets":{"asset_url_format":"steam/apps/12/${FILENAME}","main_capsule":"capsule_616x353.jpg"}}
            ]}}
            """;

        var urls = SteamApiService.ParseMainCapsuleUrls(json);

        Assert.Equal([10, 12], urls.Keys.Order());
        Assert.Equal(Cdn + "steam/apps/12/capsule_616x353.jpg", urls[12]);
    }

    [Fact]
    public void Hashed_main_capsule_uses_its_own_folder()
    {
        // Newer apps keep each asset in its own hash folder; the small capsule's folder differs.
        var json = Answer("""
            "asset_url_format":"steam/apps/10/${FILENAME}?t=123",
            "main_capsule":"aaa111/capsule_616x353.jpg",
            "small_capsule":"bbb222/capsule_231x87.jpg"
            """);

        Assert.Equal(Cdn + "steam/apps/10/aaa111/capsule_616x353.jpg?t=123",
            Parse(json));
    }

    [Fact]
    public void Plain_main_capsule_has_no_folder()
    {
        var json = Answer("""
            "asset_url_format":"steam/apps/10/${FILENAME}?t=5",
            "main_capsule":"capsule_616x353.jpg"
            """);

        Assert.Equal(Cdn + "steam/apps/10/capsule_616x353.jpg?t=5", Parse(json));
    }

    [Theory]
    [InlineData("""{"response":{}}""")]
    [InlineData("""{"response":{"store_items":[]}}""")]
    [InlineData("""{"response":{"store_items":[{"appid":10,"success":2}]}}""")]
    [InlineData("""{"response":{"store_items":[{"appid":10,"assets":{"asset_url_format":"steam/apps/10/${FILENAME}"}}]}}""")]
    public void Missing_main_capsule_gives_null(string json) =>
        Assert.Null(Parse(json));
}
