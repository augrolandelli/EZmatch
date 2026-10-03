using System.Net;
using System.Net.Http.Json;

namespace EZmatchApi.Tests;

[Collection(ApiCollection.Name)]
public class HealthApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task Health_ReportsApiAndDatabaseUp()
    {
        var response = await fixture.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.Equal("healthy", body!.Status);
        Assert.Equal("up", body.Database);
    }

    private sealed record HealthBody(string Status, string Database);
}
