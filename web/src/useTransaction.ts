import { useRef, useState } from "react";
import { api, ApiError } from "./api";
import { clearPending, loadPending, savePending } from "./pending";
import type {
  FieldErrors,
  Pending,
  Receipt,
  TransactionPayload,
} from "./types";

function recover(): { pending: Pending | null; error: string } {
  try {
    return { pending: loadPending(), error: "" };
  } catch (error) {
    return { pending: null, error: (error as Error).message };
  }
}
export function useTransaction(onSuccess: () => void) {
  const [initial] = useState(recover);
  const [pending, setPending] = useState(initial.pending);
  const [busy, setBusy] = useState(false);
  const inFlight = useRef(false);
  const [notice, setNotice] = useState(
    initial.pending
      ? "Unresolved submission restored. Retry the original request to safely determine its outcome."
      : "",
  );
  const [error, setError] = useState<ApiError | null>(null);
  const [storageError, setStorageError] = useState(initial.error);
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [last, setLast] = useState<Pending | null>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});

  async function send(submission: Pending) {
    if (inFlight.current || initial.error) return;
    inFlight.current = true;
    setError(null);
    setFieldErrors({});
    setStorageError("");
    try {
      // Never send until the original payload and UUID are recoverable after a reload.
      savePending(submission);
    } catch {
      setStorageError(
        "Could not save submission recovery. Nothing was sent. Enable session storage and try again.",
      );
      inFlight.current = false;
      return;
    }
    setPending(submission);
    setBusy(true);
    setNotice("Sending your request. Please keep this tab open.");
    try {
      const confirmed = await api.execute(submission);
      setReceipt(confirmed);
      setLast(submission);
      setNotice(
        "Transaction confirmed. The receipt below records the balances at the time of this transaction.",
      );
      try {
        clearPending();
        setPending(null);
      } catch {
        setStorageError(
          "Transaction confirmed, but recovery storage could not be cleared. Retry the same request to safely finish recovery before starting another.",
        );
      }
      onSuccess();
    } catch (caught) {
      const problem = caught as ApiError;
      setError(problem);
      if (problem.uncertain)
        setNotice(
          "The outcome is not confirmed. Your original request is saved in this tab. Retry it safely; a completed transaction will not run twice.",
        );
      else {
        setNotice(
          "The request was rejected. Review the message and try a new submission.",
        );
        const mapped: FieldErrors = {};
        for (const [field, messages] of Object.entries(problem.errors ?? {})) {
          const key = (
            {
              amountMinor: "amount",
              sourceAccountId: "source",
              destinationAccountId: "destination",
            } as const
          )[field as "amountMinor"];
          if (key) mapped[key] = messages.join(" ");
        }
        setFieldErrors(mapped);
        try {
          clearPending();
          setPending(null);
        } catch {
          setStorageError(
            "Could not clear recovery storage. Retry the original request to resolve recovery before starting another.",
          );
        }
      }
    } finally {
      setBusy(false);
      inFlight.current = false;
    }
  }
  return {
    pending,
    busy,
    notice,
    error,
    storageError,
    receipt,
    fieldErrors,
    last,
    blocked: Boolean(pending || busy || initial.error),
    submit: (payload: TransactionPayload) => {
      if (!pending && !inFlight.current && !initial.error)
        void send({ key: crypto.randomUUID(), payload });
    },
    retry: () => {
      if (pending) void send(pending);
    },
    replay: () => {
      if (last && !pending) void send(last);
    },
  };
}
