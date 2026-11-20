using System.Text.Json.Nodes;

namespace ArmoryWiki.Api.Tests;

/// <summary>The OpenAPI document is the contract the frontend generates its types from (PLAN.md Phase 5).</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class OpenApiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Document_describes_every_read_endpoint_with_its_error_responses_and_enum_values()
    {
        var doc = await fixture.GetJsonAsync<JsonObject>("/openapi/v1.json");
        var paths = doc["paths"]!.AsObject();

        Assert.Equal(16, paths.Count);
        Assert.NotNull(paths["/api/firearms/{slug}"]!["get"]!["responses"]!["404"]);
        Assert.NotNull(paths["/api/firearms/{slug}"]!["get"]!["responses"]!["301"]);
        Assert.NotNull(paths["/api/firearms"]!["get"]!["responses"]!["400"]);
        Assert.Equal(
            ["SemiAutomatic", "Burst", "Automatic"],
            doc["components"]!["schemas"]!["FiringModes"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
    }
}
