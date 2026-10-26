export default function DemoGuide() {
  return (
    <details className="panel demo-guide">
      <summary>
        <span>
          <span className="eyebrow">TAKE A LOOK AROUND</span>
          <strong>Demonstrate the app</strong>
        </span>
        <span aria-hidden="true">＋</span>
      </summary>
      <div className="guide-body">
        <p className="muted">
          Single user, two USD accounts, persistent data. These steps use your
          current demo balances.
        </p>
        <ol>
          <li>
            <strong>Deposit $100 into Checking.</strong> Confirm it, check the
            updated balance, then open the full receipt.
          </li>
          <li>
            <strong>Withdraw $20 from Checking.</strong> The balance drops and a
            debit appears in its history.
          </li>
          <li>
            <strong>Transfer $30 to Savings.</strong> Checking decreases,
            Savings increases, and the same receipt appears in both histories.
            Your combined balance stays the same.
          </li>
          <li>
            <strong>Try invalid inputs.</strong> Submit an empty amount, 0, −1,
            1.001, more than the available funds, or a transfer to the same
            account. Inline messages explain how to fix each one.
          </li>
          <li>
            <strong>Verify safe replay.</strong> After a success, select “Verify
            safe replay.” You receive the original receipt without moving money
            again. Make another operation to see that receipt snapshots remain
            unchanged.
          </li>
          <li>
            <strong>Explore history and receipts.</strong> Switch account cards,
            change page size, page through activity, or look up a transaction
            UUID. An unknown UUID returns a clear server message.
          </li>
          <li>
            <strong>Demonstrate recovery.</strong> Stop the API, submit a valid
            operation, reload this same tab, restart the API, then select “Retry
            same request.” Keep this tab open: its saved request resolves
            safely. Confirmed writes survive an API restart.
          </li>
        </ol>
        <p className="small muted">
          The API validates current balances again, including changes from
          another tab. Failed attempts do not enter transaction history.
          Automated backend tests cover concurrency, rollback and database lock
          errors; no failure-injection endpoints are exposed.
        </p>
      </div>
    </details>
  );
}
