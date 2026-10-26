import { useState } from "react";
import { api, ApiError } from "../api";
import { uuidPattern } from "../pending";
import type { Receipt } from "../types";
export default function ReceiptLookup({
  onReceipt,
}: {
  onReceipt: (receipt: Receipt) => void;
}) {
  const [id, setId] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <details className="panel lookup">
      <summary>Find a receipt</summary>
      <form
        noValidate
        onSubmit={async (event) => {
          event.preventDefault();
          if (busy) return;
          setError("");
          if (!uuidPattern.test(id.trim())) {
            setError("Enter a valid transaction UUID.");
            return;
          }
          setBusy(true);
          try {
            onReceipt(await api.receipt(id.trim()));
          } catch (caught) {
            const p = caught as ApiError;
            setError(
              `${p.detail}${p.traceId ? ` · Reference: ${p.traceId}` : ""}`,
            );
          } finally {
            setBusy(false);
          }
        }}
      >
        <label htmlFor="receipt-id">Transaction ID</label>
        <div className="lookup-input">
          <input
            id="receipt-id"
            placeholder="Paste a transaction UUID"
            value={id}
            onChange={(e) => setId(e.target.value)}
            aria-invalid={Boolean(error)}
            aria-describedby={error ? "lookup-error" : undefined}
          />
          <button type="submit" disabled={busy}>
            {busy ? "Finding…" : "Find receipt"}
          </button>
        </div>
        {error && (
          <p id="lookup-error" className="field-error" role="alert">
            {error}
          </p>
        )}
      </form>
    </details>
  );
}
