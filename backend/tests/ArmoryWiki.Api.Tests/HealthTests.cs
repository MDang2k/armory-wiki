using System.Net;

namespace ArmoryWiki.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public sealed class HealthTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Healthz_answers_ok_when_the_database_is_reachable()
    {
        var response = await fixture.Client().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"ok\"", await response.Content.ReadAsStringAsync());
    }
}
