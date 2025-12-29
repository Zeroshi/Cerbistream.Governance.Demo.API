# CerbiStream Governance Demo (net8)

This demo shows how to wire CerbiStream governed logging together with the Cerbi runtime validator on .NET 8.

## What's included
- CerbiStream 1.1.84
- Cerbi.Governance.Runtime 1.1.10
- CerbiStream.GovernanceAnalyzer 1.5.49 (latest net8-compatible)
- Swagger UI enabled

## Config files
- `config/cerbi_governance.json`: Cerbi governance profile for logging/runtime. Override path with `CERBI_GOVERNANCE_PATH`.
- `config/governance-policy.json`: Minimal sample policy (kept for reference).

## How it starts (Program.cs)
- Logging: console + `AddCerbiGovernanceRuntime` (with fallback if the profile is missing).
- Runtime validation: `RuntimeGovernanceValidator` loads `cerbi_governance.json` and annotates/denies when violations exist.
- Endpoints:
  - `GET /healthz` – readiness.
  - `GET /governance/profile` – returns the loaded Cerbi governance profile.
  - `POST /event` – validates request metadata; 403 when violations exist, 200 otherwise. Requires `x-user-role` header.
- Swagger available at `/swagger`.

## What’s required vs. optional
- Required for Cerbi to function:
  - NuGet packages: `CerbiStream`, `Cerbi.Governance.Runtime` (matching API), and governance profile JSON (`cerbi_governance.json`).
  - Register `AddCerbiGovernanceRuntime` (for governed logging) and `RuntimeGovernanceValidator` (for request validation/annotation).
  - Ensure the profile file is present (or set `CERBI_GOVERNANCE_PATH`).
- Optional but recommended (app-level best practices):
  - Validate requests (body, topic, header) and return 400/403 instead of letting exceptions surface.
  - Guard profile loading/logging registration so missing/invalid profiles fall back to console logging instead of crashing.
  - Decide how to act on violations (e.g., 403) — the runtime annotates; your app chooses the response policy.

## Pilot vs. Full Demo

### Pilot (minimum to see CerbiStream work)
- Packages: `CerbiStream` 1.1.84, `Cerbi.Governance.Runtime` 1.1.10.
- Config: `config/cerbi_governance.json` (or set `CERBI_GOVERNANCE_PATH`).
- Code: keep `AddCerbiGovernanceRuntime` and `RuntimeGovernanceValidator`; keep `/event` endpoint returning 200 when no violations, 403 when violations.
- You can skip extra request guards; Cerbi will still annotate. Expect less friendly errors if inputs are bad.

### Full demo (best practices, already wired)
- Same packages/config/registrations as Pilot.
- Add request validation for clean 400/403 (topic/header/body checks).
- Guard profile loading so missing/invalid profiles fall back to console logging instead of crashing.
- Swagger + health + profile endpoints.
- Explicit allow/deny policy based on `GovernanceViolations`/`GovernanceRelaxed`.

Use the Full demo as default; use Pilot when you want the smallest surface to prove CerbiStream works.

## Run locally
1) Restore/build/tests: `dotnet test Cerbistream.Governance.Demo.Tests/Cerbistream.Governance.Demo.Tests.csproj` (tests cover health, profile, allow/deny, bad request).
2) Run the API: `dotnet run --project Cerbistream.Governance.Demo/Cerbistream.Governance.Demo.csproj`
3) Open Swagger: `https://localhost:5001/swagger`

## Example requests
- Allowed:
  - Header: `x-user-role: Compliance`
  - Body: `{ "topic": "user-data", "metadata": {} }`
  - Result: 200, `Allowed`.
- Denied (violation):
  - Header: `x-user-role: Support`
  - Body: `{ "topic": "user-data", "metadata": { "password": "secret" } }`
  - Result: 403, governance violations tagged (password redacted in logs).

## Notes for developers
- If you move configs, set `CERBI_GOVERNANCE_PATH` to the profile location.
- CerbiStream logging wrapper uses the updated runtime; if a future runtime/API mismatch appears, the extension will log a fallback message and continue with console logging.
- Analyzer runs at build/IDE time; runtime validation happens on `/event` requests.
