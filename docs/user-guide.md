# ATM user guide

## Open the app

From the project folder, run `pwsh -File ./scripts/start.ps1`, then open **http://127.0.0.1:5173**. Keep the terminal running; press **Ctrl+C** there to stop. See [setup requirements](../README.md#run) if needed.

The dashboard shows **Checking**, **Savings**, and their combined USD balance. Balances and transactions are saved between restarts.

## Make a transaction

1. In **Move your money**, choose an operation:
   - **Deposit**: choose **To account**.
   - **Withdraw**: choose **From account**.
   - **Transfer**: choose different **From account** and **To account** values.
2. Enter **Amount (USD)**, such as `25.50`. Use up to two decimal places, without a dollar sign or commas. Quick amount buttons fill the amount; **Max** fills the available withdrawal or transfer amount.
3. Check the account names and total in the summary, then click **Confirm deposit**, **Confirm withdrawal**, or **Confirm transfer** once.
4. Wait for **Transaction confirmed**. The receipt appears below and balances and history refresh automatically.

Amounts must be $0.01–$10,000,000.00. Withdrawals and transfers need sufficient funds; a deposit or incoming transfer cannot take an account above $10,000,000.00. Correct any field message before confirming again.

Selecting an account card changes the **history** being viewed. Always choose the transaction accounts in the form separately.

## View history and receipts

- Click an account card to view its history, newest first. Use **Show**, **Previous**, and **Next** to browse.
- Click a history entry to open its receipt, or expand **Find a receipt**, paste the transaction ID (UUID), and click **Find receipt**.
- Receipts show the original balances immediately after that transaction; account cards show current balances. Times use your browser's local timezone.
- Click **Refresh** for current balances and the first history page. A transfer appears in both accounts with the same transaction ID.

## If something goes wrong

- **Rejected request:** read the message, correct the details, and submit again.
- **Outcome not confirmed / connection error:** keep this tab open and use **Retry same request** when the service is available. Reloading the same tab preserves recovery; closing it or clearing browser storage loses the saved request. New transactions stay blocked until recovery finishes.
- **Balance or history refresh failed after confirmation:** use **Refresh** or **Retry history**. Your confirmed transaction remains recorded.
- **Storage error:** follow the displayed message and enable browser session storage so the app can save recovery details before sending.

For a quick example, deposit **$100** into Checking, withdraw **$20**, then transfer **$30** to Savings. Starting from zero, Checking ends at **$50**, Savings at **$30**. **Verify safe replay** returns the last confirmed receipt without moving money again.
