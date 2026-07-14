using Cerbi.Governance;
using CerbiStream.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// REQUIRED: path to Cerbi governance profile (override with CERBI_GOVERNANCE_PATH when deploying)
var cerbiGovernancePath = Environment.GetEnvironmentVariable("CERBI_GOVERNANCE_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cerbi_governance.json");
var cerbiGovernanceProfileName = Environment.GetEnvironmentVariable("CERBI_GOVERNANCE_PROFILE") ?? "default";
var governanceProfile = GovernanceProfileLoader.LoadWrappedProfile(cerbiGovernancePath, cerbiGovernanceProfileName);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// REQUIRED: CerbiStream governed logging (enforces profile before logs hit sinks).
// Startup fails fast when the configured wrapped profile is missing or invalid so the demo never silently uses another profile.
builder.Logging.AddCerbiGovernanceRuntime(
    LoggerFactory.Create(b => b.AddConsole()),
    profileName: governanceProfile.Name,
    configPath: cerbiGovernancePath);

// REQUIRED: runtime validator to annotate/validate structured data using the Cerbi profile (hot-reloads on file changes).
// OPTIONAL best practice: input validation below (header/body/topic) for clean 400/403 responses; Cerbi runtime will still annotate without it.
var governanceSource = new FileGovernanceSource(cerbiGovernancePath, governanceProfile.Name);
var validator = new RuntimeGovernanceValidator(
    isEnabled: () => true,
    profileName: governanceProfile.Name,
    source: governanceSource,
    plugins: Array.Empty<IRuntimeGovernancePlugin>());

builder.Services.AddSingleton(validator);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseSwagger();
app.UseSwaggerUI();

// Health/readiness endpoint
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithTags("Health");

// Expose the active Cerbi governance profile (helps confirm which rules are loaded)
app.MapGet("/governance/profile", () =>
{
    return Results.Ok(governanceProfile.Document);
}).WithName("GetGovernanceProfile")
  .WithTags("Governance");

// Demo governance evaluation endpoint: flattens metadata, validates via Cerbi runtime, and returns allow/deny.
app.MapPost("/event", ([FromHeader(Name = "x-user-role")] string? userRole, [FromBody] EventRequest request, RuntimeGovernanceValidator validator, ILogger<Program> logger) =>
{
    // OPTIONAL best practice: app-level input validation for clearer client errors; Cerbi runtime would still annotate without it
    if (request is null)
    {
        return Results.BadRequest("Request body is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Topic))
    {
        return Results.BadRequest("Topic is required.");
    }

    request.Metadata ??= new Dictionary<string, object>();

    if (string.IsNullOrWhiteSpace(userRole))
    {
        return Results.BadRequest("Header 'x-user-role' is required.");
    }

    // Build a flat record for validation (metadata keys become top-level for governance checks)
    var record = new Dictionary<string, object>
    {
        ["topic"] = request.Topic,
        ["userRole"] = userRole
    };

    foreach (var kvp in request.Metadata)
    {
        record[kvp.Key] = kvp.Value;
    }

    // Keep original metadata for echoing/logging; validation already ran on flattened fields
    record["metadata"] = request.Metadata;

    validator.ValidateInPlace(record);

    var hasViolations = record.TryGetValue("GovernanceViolations", out var violationsObj)
                        && violationsObj is IEnumerable<object> enumerable
                        && enumerable.Cast<object>().Any();
    var relaxed = record.TryGetValue("GovernanceRelaxed", out var relaxedObj) && relaxedObj is bool b && b;

    logger.LogInformation("Governance evaluated {@Record}", record);

    if (hasViolations && !relaxed)
    {
        return Results.Json(new GovernanceDecisionResponse(request.Topic, false, "Governance violations present"), statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new GovernanceDecisionResponse(request.Topic, true, "Governed and accepted"));
})
.Produces<GovernanceDecisionResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces<GovernanceDecisionResponse>(StatusCodes.Status403Forbidden)
.WithName("EvaluateEvent")
.WithTags("Governance");

app.Run();


public sealed record LoadedGovernanceProfile(string Name, JsonElement Document);

public static class GovernanceProfileLoader
{
    public static LoadedGovernanceProfile LoadWrappedProfile(string path, string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new InvalidOperationException("CERBI_GOVERNANCE_PROFILE must name the wrapped governance profile to load.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Cerbi governance configuration was not found at '{path}'. Set CERBI_GOVERNANCE_PATH to a valid Runtime 2.x wrapper file.", path);
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (!root.TryGetProperty("LoggingProfiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Cerbi governance configuration '{path}' must use the Runtime 2.x wrapper format with a LoggingProfiles object.");
        }

        if (!profiles.TryGetProperty(profileName, out _))
        {
            var availableProfiles = string.Join(", ", profiles.EnumerateObject().Select(p => p.Name));
            throw new InvalidOperationException($"Cerbi governance profile '{profileName}' was not found in '{path}'. Available profiles: {availableProfiles}.");
        }

        return new LoadedGovernanceProfile(profileName, root.Clone());
    }
}

// Request/response DTOs for the demo endpoint
public class EventRequest
{
    public string Topic { get; set; } = string.Empty;

    public Dictionary<string, object>? Metadata { get; set; }
}

public record GovernanceDecisionResponse(string Topic, bool Allowed, string Reason);

public partial class Program { }
