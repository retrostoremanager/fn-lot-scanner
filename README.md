# fn-lot-scanner

Azure Function (.NET 10, isolated worker) backend for AI game lot scanning. Implements
every endpoint in [retrostoremanager/lot-scanner](https://github.com/retrostoremanager/lot-scanner)'s
`specs/001-lot-scan-review/contracts/lot-scan-api.md`.

## What it does

1. `POST /lot-scans` accepts a lot photo, stores it in Blob Storage, creates a
   `processing` session, and enqueues identification work (`lot-scan-identify` queue) —
   it does not block the HTTP response on the AI call.
2. A queue-triggered function downloads the photo, sends it to Claude's vision
   capability to detect candidate items, resolves each candidate against
   `api-gamedb`'s real `pricing/bulk-lookup` endpoint in one batched call, and persists
   the results (`in_review`) or marks the session `failed` if the AI call errors out.
3. The remaining endpoints (`GET /lot-scans/{id}`, item `PATCH`, bulk `PATCH`, confirm,
   discard, catalog-search) implement the review/correction/confirm flow per the
   contract.

## Conventions this follows

- `.NET 10` isolated worker, matching `api-gamedb`'s/`fn-mystore`'s Azure Functions
  pattern but on a newer runtime (deliberate choice, see `research.md` #2 in the specs
  repo).
- EF Core + Npgsql + `EFCore.NamingConventions` (snake_case) against the `lotscanner`
  database on `db-gamedb`'s existing Postgres Flexible Server.
- JWT auth middleware ported from `fn-mystore`'s `JwtAuthenticationMiddleware` (same
  dual custom-JWT/Entra-External-ID validation), so this app accepts the exact same
  employee tokens mystore issues rather than inventing a second auth scheme.
- `ApiGamedbClient` calls api-gamedb's **real** `pricing/lookup` and
  `pricing/bulk-lookup` endpoints (confirmed by reading `api-gamedb`'s
  `PricingLookupFunctions.cs` directly) — it does not call a generic/imagined search
  endpoint.
- Response envelope (`ResponseHelper`, camelCase JSON) mirrors `api-gamedb`'s.

## Local development

Required `local.settings.json` keys (see `README`'s `.gitignore`'d template — ask
whoever set up the Azure resources for real values):

| Key | Purpose |
|---|---|
| `ConnectionStrings__lotscanner` | Postgres connection string |
| `ApiGamedb__BaseUrl` | Base URL of a running `api-gamedb` instance |
| `Anthropic__ApiKey` | Claude API key |
| `JwtAuthentication__SecretKey` | Shared secret matching `fn-mystore`'s custom JWT (local/MVP auth path) |
| `EntraExternalId__Authority` / `EntraExternalId__ClientId` | Alternative to the above for production auth |
| `Blob__ConnectionString` / `Blob__ContainerName` | Photo storage |

```bash
dotnet build
dotnet test tests/fn_lot_scanner.Tests/fn_lot_scanner.Tests.csproj
func start   # requires Azurite running for local blob/queue storage
```

## Known gaps (tracked, not silently skipped)

- No Durable Functions / retry policy on the identification queue trigger yet beyond
  the Functions runtime's default queue retry — fine for v1 volume, revisit if failed
  identifications need a dead-letter story.
- `pricing/bulk-lookup`'s per-item "best match" has no numeric fuzzy-match score; the
  confidence heuristic (`IdentificationService.ToScannedItem`) approximates it with an
  exact-title-match check. See `research.md` #3/#6 in the specs repo.
