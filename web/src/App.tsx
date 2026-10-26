import { useEffect, useRef, useState } from "react";
import { api, ApiError } from "./api";
import { formatMoney } from "./money";
import type { Account, HistoryPage, Receipt } from "./types";
import { useTransaction } from "./useTransaction";
import TransactionForm from "./components/TransactionForm";
import ReceiptPanel, { accountName } from "./components/ReceiptPanel";
import ReceiptLookup from "./components/ReceiptLookup";
import Activity from "./components/Activity";

export default function App() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [accountsLoading, setAccountsLoading] = useState(true);
  const [accountsError, setAccountsError] = useState("");
  const [selected, setSelected] = useState(1);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [history, setHistory] = useState<HistoryPage | null>(null);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [viewReceipt, setViewReceipt] = useState<Receipt | null>(null);
  const feedbackRef = useRef<HTMLDivElement>(null);
  const receiptRef = useRef<HTMLDivElement>(null);
  const transaction = useTransaction(() => {
    setPage(1);
    setRefresh((value) => value + 1);
  });
  function refreshReads() {
    setPage(1);
    setRefresh((value) => value + 1);
  }
  useEffect(() => {
    let active = true;
    setAccountsLoading(true);
    setAccountsError("");
    void api
      .accounts()
      .then((value) => {
        if (active) setAccounts(value);
      })
      .catch((error: ApiError) => {
        if (active)
          setAccountsError(
            `${error.detail}${error.traceId ? ` Reference: ${error.traceId}` : ""}`,
          );
      })
      .finally(() => {
        if (active) setAccountsLoading(false);
      });
    return () => {
      active = false;
    };
  }, [refresh]);
  useEffect(() => {
    let active = true;
    setHistoryLoading(true);
    setHistoryError("");
    setHistory(null);
    void api
      .history(selected, page, pageSize)
      .then((value) => {
        if (active) setHistory(value);
      })
      .catch((error: ApiError) => {
        if (active) setHistoryError(error.detail);
      })
      .finally(() => {
        if (active) setHistoryLoading(false);
      });
    return () => {
      active = false;
    };
  }, [selected, page, pageSize, refresh]);
  useEffect(() => {
    if (transaction.notice || transaction.storageError)
      feedbackRef.current?.focus();
  }, [transaction.notice, transaction.storageError, transaction.error]);
  function showReceipt(receipt: Receipt) {
    setViewReceipt(receipt);
  }
  useEffect(() => {
    if (viewReceipt) receiptRef.current?.focus();
  }, [viewReceipt]);
  const readBusy = accountsLoading || historyLoading;
  return (
    <>
      <header className="site-header">
        <div className="header-inner">
          <a className="brand" href="#main" aria-label="ATM home">
            <span className="brand-symbol" aria-hidden="true">
              a.
            </span>
            <span>
              ATM<span className="brand-divider">/</span>
              <span className="brand-caption">everyday banking</span>
            </span>
          </a>
          <span className="demo-label">
            <span />
            Single-user demo
          </span>
        </div>
      </header>
      <main id="main" className="dashboard">
        <div className="intro">
          <div>
            <p className="eyebrow">YOUR MONEY, AT A GLANCE</p>
            <h1>
              A little clarity.
              <br />
              <span>A lot of possibility.</span>
            </h1>
            <p className="muted">Two accounts. Every move in one place.</p>
          </div>
          <button
            className="refresh-button"
            aria-label="Refresh balances and history"
            onClick={refreshReads}
            disabled={readBusy}
          >
            <span aria-hidden="true">↻</span>{" "}
            {readBusy ? "Refreshing…" : "Refresh"}
          </button>
        </div>
        {accountsError && (
          <div className="feedback error" role="alert">
            <strong>Balances could not be refreshed.</strong>
            <p>{accountsError}</p>
            <p>
              Your confirmed receipt stays valid. Refresh to get current
              balances; no transaction will be resubmitted.
            </p>
            <button onClick={refreshReads}>Retry balance refresh</button>
          </div>
        )}
        <section
          className="accounts-section"
          aria-label="Your accounts"
          aria-busy={accountsLoading}
        >
          <div className="section-heading account-heading">
            <h2>Your accounts</h2>
            <span className="muted small">
              USD · Combined balance{" "}
              {accounts.length
                ? formatMoney(
                    accounts.reduce((total, a) => total + a.balanceMinor, 0),
                  )
                : "—"}
              {accountsError ? " · Last loaded values" : ""}
            </span>
          </div>
          <div className="account-cards">
            {[1, 2].map((id) => {
              const account = accounts.find((a) => a.id === id);
              return (
                <button
                  key={id}
                  className={`account-card ${selected === id ? "selected" : ""}`}
                  aria-label={`Select ${accountName(id)} account`}
                  aria-describedby={`account-balance-${id}`}
                  aria-pressed={selected === id}
                  onClick={() => {
                    setSelected(id);
                    setPage(1);
                  }}
                >
                  <span className="card-top">
                    <span>{accountName(id)}</span>
                    <span className="account-mark" aria-hidden="true">
                      {id === 1 ? "◈" : "◎"}
                    </span>
                  </span>
                  <span className="balance" id={`account-balance-${id}`}>
                    {account
                      ? formatMoney(account.balanceMinor)
                      : accountsLoading
                        ? "Loading…"
                        : "Unavailable"}
                  </span>
                  <span className="card-bottom">
                    <span>
                      Available balance <span className="currency">USD</span>
                    </span>
                    <span>
                      {selected === id ? "Viewing activity" : "View activity"}{" "}
                      <span aria-hidden="true">↗</span>
                    </span>
                  </span>
                </button>
              );
            })}
          </div>
        </section>
        {(transaction.notice ||
          transaction.storageError ||
          transaction.error) && (
          <div
            ref={feedbackRef}
            tabIndex={-1}
            className={`feedback ${transaction.pending ? "warning" : transaction.error || transaction.storageError ? "error" : "success"}`}
            role={
              transaction.error || transaction.storageError ? "alert" : "status"
            }
          >
            {transaction.notice && <p>{transaction.notice}</p>}
            {transaction.error && (
              <>
                <strong>{transaction.error.detail}</strong>
                <p className="small">
                  {transaction.error.code}
                  {transaction.error.status > 0 &&
                    ` · HTTP ${transaction.error.status}`}
                  {transaction.error.traceId &&
                    ` · Reference: ${transaction.error.traceId}`}
                </p>
              </>
            )}
            {transaction.storageError && <p>{transaction.storageError}</p>}
            {transaction.pending && (
              <>
                <p className="small">
                  Saved request: {transaction.pending.payload.type} ·{" "}
                  {formatMoney(transaction.pending.payload.amountMinor)}
                  {transaction.pending.payload.sourceAccountId &&
                    ` · from ${accountName(transaction.pending.payload.sourceAccountId)}`}
                  {transaction.pending.payload.destinationAccountId &&
                    ` · to ${accountName(transaction.pending.payload.destinationAccountId)}`}
                </p>
                <details>
                  <summary>Recovery reference</summary>
                  <code className="mono">{transaction.pending.key}</code>
                </details>
                <button onClick={transaction.retry} disabled={transaction.busy}>
                  {transaction.busy ? "Processing…" : "Retry same request"}
                </button>
              </>
            )}
          </div>
        )}
        <div className="workspace">
          <div className="form-column">
            <TransactionForm
              accounts={accounts}
              disabled={
                transaction.blocked ||
                accountsLoading ||
                Boolean(accountsError) ||
                accounts.length !== 2
              }
              busy={transaction.busy}
              serverErrors={transaction.fieldErrors}
              onSubmit={transaction.submit}
            />
            <div className="trust-note">
              <span aria-hidden="true">◇</span>
              <p>
                <strong>Every cent accounted for.</strong>
                <br />
                Your balances and history are saved together.
              </p>
            </div>
          </div>
          <div className="activity-column">
            <Activity
              accountId={selected}
              data={history}
              loading={historyLoading}
              error={historyError}
              page={page}
              pageSize={pageSize}
              onPage={setPage}
              onSize={(size) => {
                setPageSize(size);
                setPage(1);
              }}
              onReceipt={showReceipt}
              onRetry={() => setRefresh((value) => value + 1)}
            />
            <ReceiptLookup onReceipt={showReceipt} />
          </div>
        </div>
        {transaction.receipt && (
          <div className="confirmed-receipt">
            <ReceiptPanel receipt={transaction.receipt} />
            <div className="receipt-actions">
              <p className="small muted">
                Check duplicate protection by requesting this same transaction
                again.
              </p>
              <button
                onClick={transaction.replay}
                disabled={transaction.blocked}
              >
                Verify safe replay
              </button>
            </div>
          </div>
        )}
        {viewReceipt && (
          <div ref={receiptRef} tabIndex={-1} className="viewed-receipt">
            <div className="section-heading">
              <h2>Selected receipt</h2>
              <button onClick={() => setViewReceipt(null)}>
                Close receipt
              </button>
            </div>
            <ReceiptPanel receipt={viewReceipt} title="Transaction details" />
          </div>
        )}
        <footer>
          <span>ATM exercise · Built for the everyday.</span>
          <span>USD only · No fees · Persistent local data</span>
        </footer>
      </main>
    </>
  );
}
