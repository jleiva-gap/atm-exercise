import type { HistoryPage, Receipt } from "../types";
import { formatMoney } from "../money";
import { accountName, dateTime, typeName } from "./ReceiptPanel";

export default function Activity({
  accountId,
  data,
  loading,
  error,
  page,
  pageSize,
  onPage,
  onSize,
  onReceipt,
  onRetry,
}: {
  accountId: number;
  data: HistoryPage | null;
  loading: boolean;
  error: string;
  page: number;
  pageSize: number;
  onPage: (page: number) => void;
  onSize: (size: number) => void;
  onReceipt: (receipt: Receipt) => void;
  onRetry: () => void;
}) {
  return (
    <section
      className="panel activity"
      aria-labelledby="activity-heading"
      aria-busy={loading}
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">ACCOUNT ACTIVITY</p>
          <h2 id="activity-heading">{accountName(accountId)} history</h2>
        </div>
        <label className="page-size">
          Show
          <select
            aria-label="Transactions per page"
            value={pageSize}
            onChange={(e) => onSize(Number(e.target.value))}
          >
            {[5, 10, 20, 50, 100].map((n) => (
              <option key={n}>{n}</option>
            ))}
          </select>
        </label>
      </div>
      {loading ? (
        <div className="empty-state" role="status">
          Loading transactions…
        </div>
      ) : error ? (
        <div className="feedback error" role="alert">
          <p>{error}</p>
          <button onClick={onRetry}>Retry history</button>
        </div>
      ) : !data?.items.length ? (
        <div className="empty-state">
          <span className="empty-icon" aria-hidden="true">
            ↔
          </span>
          <h3>
            {page === 1 ? "A fresh start" : "No transactions on this page"}
          </h3>
          <p>
            {page === 1
              ? "Make your first deposit to see your money in motion."
              : "Go to the previous page or refresh to see the latest activity."}
          </p>
        </div>
      ) : (
        <ul className="activity-list">
          {data.items.map((item) => {
            const incoming = item.destinationAccountId === accountId;
            const balance = incoming
              ? item.destinationBalanceAfterMinor
              : item.sourceBalanceAfterMinor;
            return (
              <li key={item.id}>
                <button
                  className="activity-row"
                  aria-label={`View ${typeName[item.type].toLowerCase()} receipt ${item.id}`}
                  aria-describedby={`activity-${item.id}`}
                  onClick={() => onReceipt(item)}
                >
                  <span className="sr-only" id={`activity-${item.id}`}>
                    {incoming ? "Credit" : "Debit"}{" "}
                    {formatMoney(item.amountMinor)}
                    {item.type === "transfer"
                      ? ` ${incoming ? "from" : "to"} ${accountName(incoming ? item.sourceAccountId : item.destinationAccountId)}`
                      : ""}
                    . Balance after {formatMoney(balance ?? 0)}.{" "}
                    {dateTime(item.createdAtUtc)}.
                  </span>
                  <span
                    className={`direction ${incoming ? "incoming" : ""}`}
                    aria-hidden="true"
                  >
                    {item.type === "transfer" ? "↔" : incoming ? "↓" : "↑"}
                  </span>
                  <span className="activity-description">
                    <strong>
                      {typeName[item.type]}
                      {item.type === "transfer"
                        ? ` ${incoming ? "from" : "to"} ${accountName(incoming ? item.sourceAccountId : item.destinationAccountId)}`
                        : ""}
                    </strong>
                    <time dateTime={item.createdAtUtc}>
                      {dateTime(item.createdAtUtc)}
                    </time>
                  </span>
                  <span className="activity-values">
                    <strong className={incoming ? "credit" : ""}>
                      {incoming ? "+" : "−"}
                      {formatMoney(item.amountMinor)}
                    </strong>
                    <span>Balance {formatMoney(balance ?? 0)}</span>
                  </span>
                  <span className="chevron" aria-hidden="true">
                    ›
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
      <div className="pagination">
        <button
          aria-label="Previous page"
          disabled={loading || page <= 1}
          onClick={() => onPage(page - 1)}
        >
          ← Previous
        </button>
        <span>Page {page}</span>
        <button
          aria-label="Next page"
          disabled={loading || !data?.hasMore}
          onClick={() => onPage(page + 1)}
        >
          Next →
        </button>
      </div>
      <p className="small muted">
        Newest first · times shown in your local timezone. Select a transaction
        for its original receipt.
      </p>
    </section>
  );
}
