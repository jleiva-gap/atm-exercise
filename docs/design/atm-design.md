# ATM application design

Implementation blueprint based on [the exercise](../../.ref/exercise.md) and [the architecture notes](../../.ref/initial-design.md). Stack choices come from those notes; product defaults below are proposed design decisions where the exercise is silent.

## 1. Scope and decisions

Build a single-user web application to deposit, withdraw, transfer between two accounts, and view balances and transaction history.

| Decision | Reason |
|---|---|
| .NET 10 Minimal API, EF Core 10, SQLite | Required stack; persistent storage with minimal setup. |
| React + TypeScript + Vite | Small frontend with explicit API types and a simple development server. |
| Four backend projects following Clean Architecture | Keep HTTP, business rules, and persistence separate without a framework of abstractions. |
| Two fixed accounts: Checking (`1`) and Savings (`2`), initially zero | Makes initialization predictable; deposits provide starting funds. |
| USD only; integer cents throughout | Exact arithmetic and an unambiguous API. Currency is a proposed default, not an exercise requirement. |
| One database transaction per successful operation | Balances, history, and duplicate protection succeed or fail together. |
| Persist only successful operations | History represents actual money movement; failed attempts are logged, not bank transactions. |

**Approaches considered:** A single API project is smaller but provides weaker architectural boundaries than requested. The selected small layered application preserves those boundaries. CQRS frameworks, generic repositories, event sourcing, and distributed services add little value for two accounts and are excluded.

Also out of scope: authentication, account management, external banking, cash hardware, fees, interest, currency conversion, and deployment infrastructure. This is a local exercise application, not a production banking platform.

## 2. Structure and responsibilities

```text
src/
  Atm.Domain/          Account, transaction types, money rules
  Atm.Application/     Execute transaction and read use cases; persistence ports
  Atm.Infrastructure/  EF mappings, migrations, SQLite transaction implementation
  Atm.Api/             Minimal endpoints, DTO validation, Problem Details, DI
web/                   React application
tests/                 Domain, API/SQLite integration, frontend tests
scripts/start.ps1      PowerShell 7 launcher
requests/atm.http      REST Client examples
```

Dependencies: Application → Domain; Infrastructure → Application/Domain; API → Application, with Infrastructure referenced only for composition. Domain and Application have no HTTP or EF dependencies.

Endpoints bind and validate input, call a use case, and map its result to HTTP. The application coordinates business rules through a narrow persistence/session interface. Infrastructure opens the database transaction, loads accounts, saves changes, and commits or rolls back. Use an explicit transaction boundary that wraps the **entire** use case, including its reads. Do not hide the boundary inside `SaveChanges`.

Use constructor injection and small feature-focused services. No mediator, generic repository, separate unit-of-work framework, or global frontend state library is needed.

## 3. Money and persistence

Amounts and balances use C# `long`, SQLite `INTEGER`, and JSON integer `amountMinor` / `balanceMinor`. `1250` means USD 12.50. Each amount must be between `1` and `1,000,000,000` cents; balances must remain between `0` and `1,000,000,000` cents. This proposed limit keeps every value safely representable in JavaScript. Use checked arithmetic; never use floating-point arithmetic for money. Integer storage also avoids SQLite provider limitations around decimal queries. [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)

| Table | Essential fields and constraints |
|---|---|
| `Accounts` | `Id` (1 or 2, PK), `Name`, `BalanceMinor` (range check). Seed both rows once in a migration. |
| `Transactions` | `Id` (generated UUID, PK), `IdempotencyKey` (unique), `Type`, nullable `SourceAccountId` / `DestinationAccountId` (FKs), `AmountMinor`, `CreatedAtUtc`, nullable `SourceBalanceAfterMinor` / `DestinationBalanceAfterMinor`. |

Transactions are immutable. Persist timestamps as UTC `DateTime`; return ISO 8601 with `Z`. Enforce positive bounded amounts, balance ranges, foreign keys, and valid account combinations in the database as well as application validation. Enable foreign-key enforcement on every connection. Add history indexes on `(SourceAccountId, CreatedAtUtc, Id)` and `(DestinationAccountId, CreatedAtUtc, Id)`.

| Type | Required accounts | Effect |
|---|---|---|
| `deposit` | Destination only | Increase destination balance. |
| `withdrawal` | Source only | Decrease source balance if sufficient. |
| `transfer` | Source and different destination | Decrease source and increase destination atomically. |

One transfer is one transaction row, visible in both accounts' histories. History derives debit/credit direction and the relevant balance-after from that row. Current balances are stored on accounts; transfers preserve their combined balance. There is no second ledger to synchronize.

Store the file at a configured absolute path, defaulting to `data/atm.db` under the repository. Use EF migrations, not `EnsureCreated`; initialization never resets existing balances. Ignore the database and SQLite sidecar files in source control.

## 4. Transaction safety and idempotency

Every `POST /api/transactions` requires one UUID `Idempotency-Key` header. Normalize its representation before storing it. The key identifies one user submission across retries, browser reloads, and server restarts; it is not an account or transaction ID.

Execution order:

1. Validate JSON, header, type, integer amount/range, and required account-field combinations.
2. Open a short, non-deferred SQLite write transaction **before reading balances or checking the key**. Use `SqliteConnection.BeginTransaction(deferred: false)` and enlist the same EF context/connection with `UseTransaction`.
3. Look up the key. If present, compare the normalized tuple `(type, sourceAccountId, destinationAccountId, amountMinor)`. Same tuple: return the original receipt without mutation. Different tuple: return `409 idempotency_conflict`.
4. Load the referenced accounts inside the transaction. Check existence, sufficient funds, and resulting balance limits; apply the domain operation.
5. Save changed balances and one transaction row, including balance-after snapshots. Commit before returning success. Any failure rolls everything back.

SQLite allows one writer at a time. Acquiring the write transaction before reads prevents two withdrawals from approving against the same old balance, including across API processes using the same file. An in-process lock is insufficient. Keep transactions free of network calls. [SQLite isolation](https://sqlite.org/isolation.html), [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)

Configure a five-second database lock timeout. Lock exhaustion becomes `503 database_busy` with `Retry-After: 1`; no additional application retry loop. The client retries with the same key. Do not introduce optimistic version columns alongside this serialization strategy.

The unique key is a final database safeguard. A key collision must roll back before reading the existing receipt through a fresh context and applying the same tuple comparison; unrelated database constraints must not be misreported as duplicate requests.

Keep successful keys for as long as history is retained; no expiry or cleanup in this app. Failed requests do not reserve keys. Replays return `201`, the same `Location`, and the original receipt, including original balance snapshots and timestamp—not today's balances. This also resolves a commit whose HTTP response was lost. Read current accounts separately after success.

## 5. HTTP contract and errors

All paths are under `/api`; JSON property names are camelCase.

| Endpoint | Success response |
|---|---|
| `GET /accounts` | `200` array of `{ id, name, currency, balanceMinor }`, sorted by ID. Read both balances in one query. |
| `GET /accounts/{id}/transactions?page=1&pageSize=20` | `200 { items, page, pageSize, hasMore }`; newest first by `(createdAtUtc DESC, id DESC)`. Page ≥ 1; size 1–100. Empty history returns an empty array. |
| `GET /transactions/{id}` | `200` transaction receipt; `404` if absent. |
| `POST /transactions` | `201` receipt and `Location: /api/transactions/{id}`; requires `Idempotency-Key`. |

Example transfer request:

```http
POST /api/transactions
Content-Type: application/json
Idempotency-Key: 1e7d055f-c35b-4e2f-8b41-e70ab35525dc

{"type":"transfer","sourceAccountId":1,"destinationAccountId":2,"amountMinor":1250}
```

For deposits omit source; for withdrawals omit destination. Omitted and explicit null accounts normalize identically. Reject unknown properties and unsupported type values. Receipts contain `{ id, type, sourceAccountId, destinationAccountId, amountMinor, currency, createdAtUtc, sourceBalanceAfterMinor, destinationBalanceAfterMinor }`; irrelevant fields are null. Do not expose the idempotency key. History items use this same receipt shape. Separate requests/pages can observe newer commits; refreshing returns to page 1.

Return `application/problem+json` for errors, with `type`, `title`, `status`, clear `detail`, stable `code`, and `traceId`; validation errors also include field-keyed `errors`.

| Status | Use |
|---|---|
| `400 Bad Request` | Malformed JSON/IDs/pagination; missing or invalid key; invalid type, amount, or account-field combination; same-account transfer. |
| `404 Not Found` | A structurally valid referenced account or transaction does not exist. |
| `409 Conflict` | `insufficient_funds`, `balance_limit_exceeded`, or `idempotency_conflict`. |
| `415 Unsupported Media Type` | POST body is not JSON. |
| `503 Service Unavailable` | Database lock timeout; message: “The database is busy. Retry this request with the same Idempotency-Key.” |
| `500 Internal Server Error` | Unexpected persistence or application failure; safe message and trace ID. |

Use one exception/Problem Details handler and consistent binding/validation responses. Log underlying SQLite error codes and exceptions server-side; never return SQL, file paths, or stack traces. A database failure must never return success. Expected business failures use explicit results rather than exceptions.

## 6. Frontend behavior

One responsive page contains two balance cards, a Deposit / Withdraw / Transfer form, and history for the selected account. Show account names, USD amounts, direction, timestamps in local time, and success receipts. Provide loading, empty, error, and retry states; accessible labels, inline errors, and keyboard focus on feedback.

Validate required accounts, different transfer accounts, positive amounts, at most two decimal places, and displayed available funds/limits. Parse the amount string into integer cents without multiplying a floating-point number. Server validation remains authoritative because balances can change in another tab.

Generate one UUID when a valid submission starts. Save the key and normalized payload in `sessionStorage` before sending; disable repeated submission while pending. A timeout, network error, `500`, or `503` leaves the outcome uncertain: preserve the payload/key, offer Retry, and block a new submission until resolved. On reload, restore this pending state. Never silently generate a fresh key for an uncertain request. A successful response or definitive `400`/`404`/`409`/`415` ends that submission; a subsequent submission gets a new key.

After success, refresh balances and selected history from the API. If refreshing fails, retain the success receipt and offer a read refresh; do not resubmit the transaction. Keep form/state local and use a small typed API client with `fetch`; styling can use plain CSS.

## 7. Implementation sequence and verification

1. Create the four backend projects; implement domain rules and migrations with fixed accounts.
2. Implement the atomic application flow, SQLite boundary, and stored idempotency receipts.
3. Add endpoints, validation, error mapping, and integration tests against a temporary **file-backed SQLite database**.
4. Build the React page, API client, and pending-submission recovery.
5. Deliver the launcher, REST Client file, and README with prerequisites, run/test commands, assumptions, and tradeoffs.

Tests must demonstrate:

- Deposit/withdraw/transfer success, exact cents, balance limits, invalid inputs, missing accounts, and insufficient funds.
- Transfer conserves total funds; forced failure between writes leaves both balances and history unchanged.
- Concurrent withdrawals with distinct keys cannot overdraw; concurrent identical keys create exactly one transaction; changed payload with the same key returns `409`.
- Replaying after another operation, a simulated lost response, and an API restart returns the original receipt without another balance change.
- Lock timeout returns `503`; database exceptions are sanitized; persistence survives restart.
- UI validation, disabled duplicate submission, same-key retry/reload, and refresh failure after confirmed success.

`requests/atm.http` includes every endpoint, the three operations, exact replay, changed-key-payload conflict, and representative `400`/`404`/`409`/`415` cases, with expected statuses and configurable base URL/keys. Cover forced `500`/`503` and concurrency in integration tests; do not add failure-injection endpoints.

`scripts/start.ps1` targets PowerShell 7: verify .NET 10/Node prerequisites, restore dependencies, apply migrations once, and start the API on `127.0.0.1:5000` and Vite on `127.0.0.1:5173`. Proxy frontend `/api` calls to the backend. Fail clearly on occupied ports or startup errors, wait for readiness, print URLs, and stop only processes it launched on exit/Ctrl+C. Use hidden child processes on Windows and preserve the database on every run. Pin SDK/dependency versions during implementation and commit lockfiles.

**Ready-to-build boundary:** the schema, operation semantics, API, retry behavior, and consistency strategy above are the blueprint. Exact class names, CSS details, and test-library choices remain implementation decisions.
