using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
}

public class GovernanceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public GovernanceTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        // Verifies app is up and middleware pipeline is configured
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GovernanceProfile_ReturnsProfile()
    {
        // Confirms the Cerbi governance profile is served and parsed
        var response = await _client.GetAsync("/governance/profile");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var hasLoggingProfiles = document.EnumerateObject().Any(p => string.Equals(p.Name, "LoggingProfiles", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasLoggingProfiles);
    }

    [Fact]
    public async Task Event_Allows_When_No_Violations()
    {
        // Valid request with no disallowed fields should be allowed
        var response = await PostEvent(new EventRequest
        {
            Topic = "user-data",
            Metadata = new Dictionary<string, object>()
        }, userRole: "Compliance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = await response.Content.ReadFromJsonAsync<GovernanceDecisionResponse>();
        Assert.NotNull(decision);
        Assert.True(decision!.Allowed);
    }

    [Fact]
    public async Task Event_Forbidden_When_Governance_Violation()
    {
        // Disallowed field (password) should trigger governance violation and 403
        var response = await PostEvent(new EventRequest
        {
            Topic = "user-data",
            Metadata = new Dictionary<string, object>
            {
                ["password"] = "secret"
            }
        }, userRole: "Support");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var decision = await response.Content.ReadFromJsonAsync<GovernanceDecisionResponse>();
        Assert.NotNull(decision);
        Assert.False(decision!.Allowed);
    }

    [Fact]
    public async Task Event_BadRequest_When_Topic_Missing()
    {
        // Missing topic should short-circuit with 400
        var response = await PostEvent(new EventRequest { Topic = "" }, userRole: "Compliance");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostEvent(EventRequest request, string userRole)
    {
        using var message = JsonContent.Create(request);
        message.Headers.Add("x-user-role", userRole);
        return await _client.PostAsync("/event", message);
    }
}
