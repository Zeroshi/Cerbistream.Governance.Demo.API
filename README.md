# CerbiStream Governance Demo

This demo wires CerbiStream governed logging to the Cerbi Governance Runtime on .NET 8 and .NET 9. It is intentionally local-file based: application logs are governed in-process before they leave the application, with no control-plane or network dependency in the runtime hot path.

## What's included

- `CerbiStream` 2.0.6
- `Cerbi.Governance.Runtime` 2.0.43
- Swagger UI enabled for local exploration
- Pull-request CI for restore, Release builds, tests, config-copy validation, and a credential-free startup smoke check

## Config files

- `config/cerbi_governance.json`: active Runtime 2.x wrapped governance profile. Override the file path with `CERBI_GOVERNANCE_PATH`.
- `config/governance-policy.json`: older sample topic policy kept for reference only; it is not the active runtime logging profile.

The active wrapped config uses `LoggingProfiles` only and does not mix canonical root profile markers:

```json
{
  "EnforcementMode": "Strict",
  "LoggingProfiles": {
    "default": {
      "name": "default",
      "version": "2026.07",
      "disallowedFields": ["password"],
      "fieldSeverities": {}
    }
  }
}
```

The selected profile defaults to `default`. Set `CERBI_GOVERNANCE_PROFILE` to choose a different wrapped profile. Startup fails with a clear error when the configured file is missing, does not use the Runtime 2.x wrapper format, or does not contain the selected profile.

## How it starts

- Logging: console + `AddCerbiGovernanceRuntime`, using the configured wrapped profile name.
- Runtime validation: `RuntimeGovernanceValidator` uses `FileGovernanceSource` with the same configured profile name, so the API does not silently drift to another profile.
- Endpoints:
  - `GET /healthz` — readiness.
  - `GET /governance/profile` — returns the loaded wrapped governance config.
  - `POST /event` — validates request metadata; returns 403 when governed violations are present, 200 otherwise. Requires `x-user-role` header.
- Swagger available at `/swagger`.

## Run locally

1. Restore dependencies: `dotnet restore Cerbistream.Governance.Demo.API.sln`
2. Build all target frameworks: `dotnet build Cerbistream.Governance.Demo.API.sln --configuration Release --no-restore`
3. Run tests: `dotnet test Cerbistream.Governance.Demo.API.sln --configuration Release --no-build`
4. Run the API: `dotnet run --project Cerbistream.Governance.Demo/Cerbistream.Governance.Demo.csproj --framework net8.0`
5. Open Swagger: `https://localhost:5001/swagger` or the URL printed by `dotnet run`.

## Example requests

Allowed structured event:

- Header: `x-user-role: Compliance`
- Body: `{ "topic": "user-data", "metadata": { "operation": "created" } }`
- Result: 200, `Allowed`.

Governed violation path:

- Header: `x-user-role: Support`
- Body: `{ "topic": "user-data", "metadata": { "password": "secret" } }`
- Result: 403, because `password` is disallowed by the selected governance profile and the governed record is logged through CerbiStream.

## Notes for developers

- Keep governance runtime configuration local and deterministic. Do not route application logs through a public control-plane service.
- If you move configs, set `CERBI_GOVERNANCE_PATH` to the wrapped profile file.
- If you add profiles, set `CERBI_GOVERNANCE_PROFILE` explicitly in launch or deployment instructions.
- Do not add direct `Cerbi.Governance.Core` or `CerbiShield.Contracts` references unless code imports those packages or NuGet restore requires a direct aligned dependency.
