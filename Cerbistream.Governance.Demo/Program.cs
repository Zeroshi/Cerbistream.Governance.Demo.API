using Cerbi.Governance;
using CerbiStream.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// REQUIRED: path to Cerbi governance profile (override with CERBI_GOVERNANCE_PATH when deploying)
var cerbiGovernancePath = Environment.GetEnvironmentVariable("CERBI_GOVERNANCE_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "cerbi_governance.json");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// REQUIRED: CerbiStream governed logging (enforces profile before logs hit sinks).
// OPTIONAL best practice: guard missing/invalid profile so startup doesn’t crash; Cerbi will still run if you remove the guard, but failure won’t be graceful.
if (File.Exists(cerbiGovernancePath))
{
    try
    {
        builder.Logging.AddCerbiGovernanceRuntime(
            LoggerFactory.Create(b => b.AddConsole()),
            profileName: "default",
            configPath: cerbiGovernancePath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CerbiStream] Failed to enable governed logging: {ex.Message}. Falling back to console-only logging.");
    }
}
else
{
    Console.WriteLine($"[CerbiStream] Governance profile not found at '{cerbiGovernancePath}'. Running without governed logging.");
}

// REQUIRED: runtime validator to annotate/validate structured data using the Cerbi profile (hot-reloads on file changes).
// OPTIONAL best practice: input validation below (header/body/topic) for clean 400/403 responses; Cerbi runtime will still annotate without it.
var governanceSource = new FileGovernanceSource(cerbiGovernancePath);
var validator = new RuntimeGovernanceValidator(
    isEnabled: () => true,
    profileName: "default",
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
    var profile = governanceSource.Load();
    return profile is null
        ? Results.NotFound("Governance profile not found")
        : Results.Ok(profile);
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

// Request/response DTOs for the demo endpoint
public class EventRequest
{
    public string Topic { get; set; } = string.Empty;

    public Dictionary<string, object>? Metadata { get; set; }
}

public record GovernanceDecisionResponse(string Topic, bool Allowed, string Reason);

public partial class Program { }
