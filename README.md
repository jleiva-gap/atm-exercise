# ATM application

A single-user ATM application for [the exercise](.ref/exercise.md), implemented from [the design](docs/design/atm-design.md). A React + TypeScript dashboard and .NET API provide two persistent USD accounts, deposits, withdrawals, atomic transfers, balances, history and original transaction receipts.

## Run

Prerequisites: .NET SDK **10.0.400** (or a later patch in that feature band), Node.js **22.12+** with npm, and PowerShell **7+** for the launcher. No database server is required. Frontend dependencies are pinned in `web/package-lock.json`.

```powershell
pwsh -File ./scripts/start.ps1
```

Open **http://127.0.0.1:5173**. The launcher restores locked dependencies, builds both applications, starts the API at `http://127.0.0.1:5000` and Vite at `http://127.0.0.1:5173`, checks both services and the `/api` proxy, and prints log paths. It rejects occupied ports and stops only the processes it launched on Ctrl+C. Migrations preserve existing balances and receipts.

Optional arguments: `-Port 5050`, `-WebPort 5174`, `-DatabasePath C:\data\atm.db`, `-NoRestore` (already restored), `-NoBuild` (already built Release and installed frontend dependencies). `-ApiOnly` retains the API-only workflow without requiring Node. A custom API port is passed to the frontend proxy automatically.

Or run directly:

```powershell
dotnet restore Atm.slnx --locked-mode
dotnet run --project src/Atm.Api --no-restore -- --urls http://127.0.0.1:5000
```

In a second terminal:

```powershell
cd web
npm ci
npm run dev
```

For a different API port, set `$env:ATM_API_TARGET = 'http://127.0.0.1:5050'` before starting Vite. The UI always uses relative `/api` paths; the local proxy avoids CORS configuration. This is a local exercise app; the Vite development server is not a production deployment.

## Demonstrate the UI

For everyday use, see the concise [UI user guide](docs/user-guide.md).

The dashboard includes two selectable account cards, a combined balance, a Deposit / Withdraw / Transfer form, exact USD entry, quick amounts and Max, a review summary, paginated account activity, full receipts, UUID receipt lookup, refresh and an expandable demo guide.

1. Deposit **$100** into Checking, withdraw **$20**, then transfer **$30** from Checking to Savings. Starting from zero, the balances become **$50 / $30**, combined **$80**. A transfer appears in both histories with the same transaction ID.
2. Submit an empty amount, zero, negative value, three decimals, excess funds, or a same-account transfer. Field messages explain the issue and focus the first invalid field. Deposits and transfers also check the destination's $10,000,000 balance limit. Server rules remain authoritative when another tab changes a balance.
3. Open an activity row, change page size, switch accounts, or paste a receipt UUID into **Find a receipt**. Times use the browser's local timezone; each receipt keeps its original balance snapshots. Refresh returns to page one.
4. After a success, select **Verify safe replay**. The original key and payload return the same receipt without changing balances again. Each new form submission receives a new UUID.
5. To demonstrate connection recovery, run API and UI in separate terminals, stop only the API, and submit a valid operation. The UI retains the pending payload/key and blocks new operations. Reload the **same tab**, restart the API, and select **Retry same request**. An already committed request replays safely. With the combined launcher, stopping the API intentionally stops both services, so use the separate-terminal workflow for this demonstration.
6. Stop and restart the API to verify persisted balances/history. A fresh demo can use a new absolute `-DatabasePath`; startup never resets an existing database.

Loading, empty history, read errors, field errors, business rejections, HTTP errors and uncertain submission states have distinct feedback. Server error codes and trace references appear when supplied. A confirmed transaction stays confirmed if a follow-up read fails: use **Refresh** to read again, without resubmitting it.

Pending recovery is stored in this tab's `sessionStorage` **before** sending. Network failures, timeouts, 500 and 503 preserve it; definitive 400/404/409/415 clear it. Closing the tab or clearing browser storage loses recovery information. If storage cannot be read, new writes are blocked; if saving fails, nothing is sent. Successful replay controls are available during the current page session; durable receipts remain searchable from history after a reload. The UI uses local component state and a small typed fetch client, without a global state or form framework.

The database defaults to `<repository>/data/atm.db` during source runs. Published applications default to `data/atm.db` below their content root. Override with an **absolute** file path using `Atm__DatabasePath` or `--Atm:DatabasePath C:\data\atm.db`. Startup fails clearly if the path cannot be used; it never silently switches to a temporary database. SQLite and its sidecar files are ignored by Git.

## API

| Method | Path | Success |
|---|---|---|
| GET | `/api/accounts` | 200; Checking (`1`), Savings (`2`), sorted by ID |
| GET | `/api/accounts/{id}/transactions?page=1&pageSize=20` | 200; `{ items, page, pageSize, hasMore }` |
| GET | `/api/transactions/{id}` | 200; original receipt |
| POST | `/api/transactions` | 201; receipt and `Location` |

Use the new [REST Client scenarios](requests/atm-scenarios.http) with [run instructions](requests/README.md) for the happy path, validation and business errors, HTTP errors, and optional 500/503 exception recovery checks. The original [requests/atm.http](requests/atm.http) remains available as a shorter sample. All money values are integer cents: `1250` means USD 12.50. Amounts are 1–1,000,000,000 cents; balances are 0–1,000,000,000 cents.

```http
POST http://127.0.0.1:5000/api/transactions
Content-Type: application/json
Idempotency-Key: 1e7d055f-c35b-4e2f-8b41-e70ab35525dc

{"type":"deposit","destinationAccountId":1,"amountMinor":1250}
```

- `deposit`: destination only.
- `withdrawal`: source only.
- `transfer`: source and a different destination.
- Omitted and explicit null account fields are equivalent. Unsupported properties, duplicate JSON properties, numeric strings, fractional cents, invalid types and account combinations are rejected.
- Page starts at 1; page size is 1–100. Ordering is `(createdAtUtc DESC, id DESC)`. Very large valid pages return an empty page. Separate pages can observe intervening commits; refresh from page 1.

Receipts contain `id`, `type`, `sourceAccountId`, `destinationAccountId`, `amountMinor`, `currency`, `createdAtUtc`, `sourceBalanceAfterMinor`, and `destinationBalanceAfterMinor`. Irrelevant fields are null; timestamps are ISO 8601 UTC with `Z`. Keys are never exposed. One transfer is one receipt appearing in both account histories.

Errors use `application/problem+json`, with `type`, `title`, `status`, `detail`, `code`, and `traceId`. Validation adds field-keyed `errors` (unreadable JSON uses `body`).

| Status | Meaning / stable codes |
|---|---|
| 400 | `validation_error`: JSON, header, account combination, amount, ID, pagination |
| 404 | `account_not_found`, `transaction_not_found` |
| 409 | `insufficient_funds`, `balance_limit_exceeded`, `idempotency_conflict` |
| 415 | `unsupported_media_type`: non-JSON body or unsupported charset |
| 503 | `database_busy`, `Retry-After: 1` |
| 500 | `internal_error`; safe message, exception logged server-side |

A submission needs one UUID `Idempotency-Key`. Reuse **the same key and payload** after a timeout, lost response, 500 or 503. The UUID is normalized, so equivalent representations replay the same receipt. A replay returns 201 and the original `Location`, timestamp and balance snapshots, even after later transactions or a server restart. A changed payload with a successful key returns 409. Failed operations do not reserve keys. Read `/api/accounts` for current balances after success.

## Structure and consistency

- `Atm.Domain`: checked money rules and account/transaction types; no framework dependencies.
- `Atm.Application`: transaction use case, validation, receipts and narrow persistence ports; depends only on Domain.
- `Atm.Infrastructure`: EF Core 10.0.13, SQLite, schema migration, and persistence sessions.
- `Atm.Api`: Minimal API routes, request validation, Problem Details, and composition.
- `web/src`: typed API and recovery client, exact amount validation, transaction state hook, and focused React components with responsive plain CSS. Runtime fonts fall back to locally installed sans-serif fonts; no remote assets are required.

Every write opens a **non-deferred SQLite transaction before any reads**, including duplicate-key checks. The application operates through that session; balances and the successful receipt commit together. Disposal rolls back unsuccessful work. SQLite's writer lock protects independent API instances using the same local database file. There is no in-process semaphore or optimistic version field. A five-second provider lock timeout becomes 503; there is no application retry loop. Provider I/O is synchronous internally, so this intentionally simple approach targets a local exercise rather than high throughput.

Database checks enforce amount/balance ranges, integer storage, account combinations and receipt snapshot shape. Every connection enables foreign keys. A unique index protects idempotency keys. Only that specific unique violation is treated as a duplicate; recovery disposes the failed transaction before reading through a fresh context. Other database failures remain 500. Transactions have no update/delete API; keys are retained with successful history.

`Migrations/InitialModel.cs` is the frozen version-one schema used by the hand-authored initial migration and model snapshot. Do not edit it for future schema changes: change the runtime model and add a new migration/snapshot instead. This avoids requiring EF tooling just to run the exercise. Startup uses `MigrateAsync`, never `EnsureCreated`.

## Verify

```powershell
dotnet restore Atm.slnx --locked-mode
dotnet build Atm.slnx --configuration Release --no-restore
dotnet test Atm.slnx --configuration Release --no-build --no-restore
cd web
npm ci
npm test
npm run build
```

The 86 tests use isolated temporary **file-backed SQLite** databases with the actual migrations. A separate Kestrel-process regression test verifies real HTTP error handling and console logging without Windows Event Log permissions. Coverage includes exact money arithmetic, limits, validation, business failures, persisted replay after restart, lost responses, independent-host concurrency, rollback after database writes, five-second lock timeout, sanitized unrelated constraint failures, schema checks and stable history ordering. Tests delete only their own temporary files.

Frontend tests exercise the real React components and client with an HTTP boundary substitute: exact cents, client validation, all operations, field/focus feedback, duplicate-click protection, persistence before sending, reload with the same key/body, network/500/503 uncertainty, storage failures, server errors, confirmed-success/read-failure separation, pagination, receipt lookup and safe replay. Backend integration tests provide the real SQLite/concurrency/fault coverage; there are no UI failure-injection endpoints.

