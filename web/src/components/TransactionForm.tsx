import { useRef, useState } from "react";
import type {
  Account,
  FieldErrors,
  TransactionForm as Form,
  TransactionPayload,
  TransactionType,
} from "../types";
import { amountInput, formatMoney, maximumMinor, parseAmount } from "../money";
import { validateTransaction } from "../validation";

export default function TransactionForm({
  accounts,
  disabled,
  busy,
  serverErrors,
  onSubmit,
}: {
  accounts: Account[];
  disabled: boolean;
  busy: boolean;
  serverErrors: FieldErrors;
  onSubmit: (payload: TransactionPayload) => void;
}) {
  const [form, setForm] = useState<Form>({
    type: "deposit",
    source: "1",
    destination: "1",
    amount: "",
  });
  const [localErrors, setErrors] = useState<FieldErrors>({});
  const [edited, setEdited] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);
  const errors = { ...(!edited ? serverErrors : {}), ...localErrors };
  const source = accounts.find((a) => String(a.id) === form.source);
  const destination = accounts.find((a) => String(a.id) === form.destination);
  const amount = parseAmount(form.amount);
  function update(field: keyof Form, value: string) {
    setForm((previous) => ({ ...previous, [field]: value }));
    setErrors({});
    setEdited(true);
  }
  function changeType(type: TransactionType) {
    setForm((previous) => ({
      ...previous,
      type,
      destination:
        type === "transfer" && previous.destination === previous.source
          ? previous.source === "1"
            ? "2"
            : "1"
          : previous.destination,
    }));
    setErrors({});
    setEdited(true);
  }
  function submit(event: React.SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (disabled) return;
    const result = validateTransaction(form, accounts);
    setErrors(result.errors);
    setEdited(false);
    if (!result.payload) {
      const first = Object.keys(result.errors)[0];
      formRef.current?.querySelector<HTMLElement>(`[name="${first}"]`)?.focus();
      return;
    }
    onSubmit(result.payload);
  }
  const accountSelect = (field: "source" | "destination", label: string) => (
    <div className="field">
      <label htmlFor={field}>{label}</label>
      <select
        id={field}
        name={field}
        value={form[field]}
        onChange={(e) => update(field, e.target.value)}
        aria-invalid={Boolean(errors[field])}
        aria-describedby={errors[field] ? `${field}-error` : undefined}
      >
        <option value="">Choose an account</option>
        {accounts.map((a) => (
          <option key={a.id} value={a.id}>
            {a.name} · {formatMoney(a.balanceMinor)}
          </option>
        ))}
      </select>
      {errors[field] && (
        <p className="field-error" id={`${field}-error`}>
          {errors[field]}
        </p>
      )}
    </div>
  );
  return (
    <section className="panel transaction-panel" aria-labelledby="move-money">
      <p className="eyebrow">MAKE A TRANSACTION</p>
      <h2 id="move-money">Move your money</h2>
      <p className="muted">A little everyday banking, made simple.</p>
      <form ref={formRef} onSubmit={submit} noValidate>
        <fieldset disabled={disabled}>
          <legend className="sr-only">Transaction details</legend>
          <div
            className="operation-tabs"
            role="tablist"
            aria-label="Transaction type"
          >
            {(["deposit", "withdrawal", "transfer"] as const).map(
              (type, index, types) => (
                <button
                  key={type}
                  type="button"
                  role="tab"
                  aria-selected={form.type === type}
                  tabIndex={form.type === type ? 0 : -1}
                  aria-controls="operation-fields"
                  id={`tab-${type}`}
                  onClick={() => changeType(type)}
                  onKeyDown={(event) => {
                    const target =
                      event.key === "ArrowRight"
                        ? types[(index + 1) % types.length]
                        : event.key === "ArrowLeft"
                          ? types[(index + types.length - 1) % types.length]
                          : event.key === "Home"
                            ? types[0]
                            : event.key === "End"
                              ? types[types.length - 1]
                              : null;
                    if (target) {
                      event.preventDefault();
                      changeType(target);
                      formRef.current
                        ?.querySelector<HTMLElement>(`#tab-${target}`)
                        ?.focus();
                    }
                  }}
                >
                  {type === "withdrawal"
                    ? "Withdraw"
                    : type === "deposit"
                      ? "Deposit"
                      : "Transfer"}
                </button>
              ),
            )}
          </div>
          <div
            id="operation-fields"
            role="tabpanel"
            aria-labelledby={`tab-${form.type}`}
          >
            {form.type !== "deposit" && accountSelect("source", "From account")}
            {form.type !== "withdrawal" &&
              accountSelect("destination", "To account")}
            <div className="field">
              <label htmlFor="amount">Amount (USD)</label>
              <div className={`amount-input ${errors.amount ? "invalid" : ""}`}>
                <span aria-hidden="true">$</span>
                <input
                  id="amount"
                  name="amount"
                  inputMode="decimal"
                  autoComplete="off"
                  placeholder="0.00"
                  maxLength={32}
                  value={form.amount}
                  onChange={(e) => update("amount", e.target.value)}
                  aria-invalid={Boolean(errors.amount)}
                  aria-describedby={`amount-help${errors.amount ? " amount-error" : ""}`}
                />
              </div>
              {errors.amount && (
                <p className="field-error" id="amount-error">
                  {errors.amount}
                </p>
              )}
              <p id="amount-help" className="small muted">
                $0.01–$10,000,000.00 · up to 2 decimal places
              </p>
            </div>
            <div className="quick-amounts" aria-label="Quick amounts">
              {[2000, 5000, 10000].map((minor) => (
                <button
                  type="button"
                  key={minor}
                  onClick={() => update("amount", amountInput(minor))}
                >
                  {formatMoney(minor)}
                </button>
              ))}
              {form.type !== "deposit" && source && (
                <button
                  type="button"
                  onClick={() =>
                    update(
                      "amount",
                      amountInput(
                        form.type === "transfer"
                          ? Math.min(
                              source.balanceMinor,
                              maximumMinor - (destination?.balanceMinor ?? 0),
                            )
                          : source.balanceMinor,
                      ),
                    )
                  }
                >
                  Max
                </button>
              )}
            </div>
            <div className="transaction-summary">
              <span>
                {form.type === "deposit"
                  ? "Deposit to"
                  : form.type === "withdrawal"
                    ? "Withdraw from"
                    : "Transfer"}
              </span>
              <strong>
                {form.type === "transfer"
                  ? `${source?.name ?? "—"} → ${destination?.name ?? "—"}`
                  : ((form.type === "deposit"
                      ? destination?.name
                      : source?.name) ?? "Choose account")}
              </strong>
              <span>Total amount</span>
              <strong>{amount === null ? "—" : formatMoney(amount)}</strong>
            </div>
            <button className="primary full" type="submit">
              {busy ? "Processing…" : `Confirm ${form.type}`}
              <span aria-hidden="true"> →</span>
            </button>
          </div>
        </fieldset>
        <p className="form-note small muted">
          One confirmation. One transaction. Safe retries if your connection
          drops.
        </p>
      </form>
    </section>
  );
}
