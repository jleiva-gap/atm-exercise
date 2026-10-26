import type { Receipt } from "../types";
import { formatMoney } from "../money";
export const accountName = (id: number | null | undefined) =>
  id === 1 ? "Checking" : id === 2 ? "Savings" : "—";
export const dateTime = (value: string) =>
  new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium",
  }).format(new Date(value));
export const typeName = {
  deposit: "Deposit",
  withdrawal: "Withdrawal",
  transfer: "Transfer",
};
export default function ReceiptPanel({
  receipt,
  title = "Transaction confirmed",
}: {
  receipt: Receipt;
  title?: string;
}) {
  return (
    <section className="receipt panel" aria-label="Transaction receipt">
      <div className="section-heading">
        <div>
          <p className="eyebrow">YOUR RECEIPT</p>
          <h2>{title}</h2>
        </div>
        <span className="pill positive">Recorded</span>
      </div>
      <div className="receipt-amount">
        {formatMoney(receipt.amountMinor)} <span>USD</span>
      </div>
      <dl className="receipt-grid">
        <div>
          <dt>Operation</dt>
          <dd>{typeName[receipt.type]}</dd>
        </div>
        <div>
          <dt>Recorded at · local time</dt>
          <dd>
            <time dateTime={receipt.createdAtUtc} title={receipt.createdAtUtc}>
              {dateTime(receipt.createdAtUtc)}
            </time>
          </dd>
        </div>
        {receipt.sourceAccountId !== null && (
          <div>
            <dt>From</dt>
            <dd>{accountName(receipt.sourceAccountId)}</dd>
          </div>
        )}
        {receipt.destinationAccountId !== null && (
          <div>
            <dt>To</dt>
            <dd>{accountName(receipt.destinationAccountId)}</dd>
          </div>
        )}
        {receipt.sourceBalanceAfterMinor !== null && (
          <div>
            <dt>{accountName(receipt.sourceAccountId)} balance after</dt>
            <dd>{formatMoney(receipt.sourceBalanceAfterMinor)}</dd>
          </div>
        )}
        {receipt.destinationBalanceAfterMinor !== null && (
          <div>
            <dt>{accountName(receipt.destinationAccountId)} balance after</dt>
            <dd>{formatMoney(receipt.destinationBalanceAfterMinor)}</dd>
          </div>
        )}
        <div className="full-width">
          <dt>Transaction ID</dt>
          <dd className="mono">{receipt.id}</dd>
        </div>
      </dl>
      <p className="muted small">
        These are original balance snapshots. Current balances are shown in your
        account cards.
      </p>
    </section>
  );
}
