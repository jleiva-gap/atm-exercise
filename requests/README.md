# REST Client scenarios

Open [atm-scenarios.http](atm-scenarios.http) in VS Code with the **REST Client** extension. Click **Send Request** above each request and compare the response with its expected status, code, and comments. These are manual checks; the file does not run assertions automatically.

## Start a clean scenario run

Run from the repository root, using an unused database filename:

```powershell
pwsh -File ./scripts/start.ps1 -ApiOnly -Port 5055 -DatabasePath "$PWD/data/rest-scenarios-01.db"
```

The file defaults to `http://127.0.0.1:5055`. Change `@baseUrl` if needed. Run **01–44 in order**, once against a fresh database, without other writers. Both accounts must start at zero; expected ending balances are **5000 / 3000 cents** ($50 / $30). Do not point the optional fault checks at your regular UI database.

Successful requests use fixed UUID keys so retries are safe. To start over, stop this launcher and choose another unused database filename. Reusing the same database replays successful keys and retains history. `{{$guid}}` generates a new key for each ordinary error request.

All amounts are integer cents. Every expected error should have `Content-Type: application/problem+json`, the stated `status` and `code`, and nonempty `type`, `title`, `detail`, and `traceId`. A 400 also includes field-keyed `errors`. Rejected writes must leave balances and history unchanged; requests 43–44 check this.

## Optional exception checks: F1–F9

These require the **SQLite CLI** (`sqlite3`) or an equivalent SQLite editor, against the same disposable database used above. Keep the API running. Faults are created in the database; there are no special API endpoints.

### 500: failed receipt insert and rollback

After request 44, open a second terminal from the repository root:

```powershell
sqlite3 "$PWD/data/rest-scenarios-01.db"
```

At the SQLite prompt, run:

```sql
CREATE TRIGGER RestClientFailReceipt BEFORE INSERT ON Transactions
BEGIN SELECT RAISE(ABORT, 'rest-client simulated persistence failure'); END;
```

1. Send **F1**: expect 500 `internal_error`. The response must not expose the trigger message, SQL, database path, or stack trace; server logs contain the exception.
2. Send **F2**: balances remain 5000 / 3000. Check histories with requests 08–10: still three Checking entries and one Savings entry, proving rollback.
3. Remove the fault at the SQLite prompt, even if a check fails:

```sql
DROP TRIGGER IF EXISTS RestClientFailReceipt;
```

4. Send **F3**, then **F4**: both return 201 with the same receipt and `Location`. The $1 deposit happens once; Checking is now 5100 cents.

### 503: database writer lock and recovery

In the same SQLite session, run:

```sql
BEGIN IMMEDIATE;
```

1. Leave that session open and send **F5**. After about five seconds, expect 503 `database_busy` and `Retry-After: 1`. Keep the REST Client timeout above five seconds.
2. Release the lock at the SQLite prompt, even if a check fails:

```sql
ROLLBACK;
```

3. Send **F6**: balances remain 5100 / 3000. Checking history still has four entries; Savings has one.
4. Send **F7**, then **F8**: both return 201 with the same receipt and `Location`; the $1 deposit happens once.
5. Send **F9**: balances are **5200 / 3000**, with five Checking history entries and one Savings entry. Enter `.quit` at the SQLite prompt.

Retry recovery requests with their original key and body. A connection failure or timeout has no HTTP status and may leave the outcome uncertain; restore connectivity and resend the exact request. To check persisted replay, restart the API with the same database and resend request 04: its original receipt and snapshots should return without another deposit.

The existing backend integration suite also checks exceptions, rollback, retries, restart, and concurrent writes: `dotnet test Atm.slnx --configuration Release`.
