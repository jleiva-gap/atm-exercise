import type { Pending } from "./types";
import { maximumMinor } from "./money";
export const pendingKey = "atm.pending.v1";
export const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function loadPending(): Pending | null {
  try {
    const stored = sessionStorage.getItem(pendingKey);
    if (stored === null) return null;
    const value = JSON.parse(stored);
    const p = value?.payload;
    const source = p?.sourceAccountId;
    const destination = p?.destinationAccountId;
    const account = (id: unknown) => id === 1 || id === 2;
    const validShape =
      p &&
      ((p.type === "deposit" && source === undefined && account(destination)) ||
        (p.type === "withdrawal" &&
          account(source) &&
          destination === undefined) ||
        (p.type === "transfer" &&
          account(source) &&
          account(destination) &&
          source !== destination));
    if (
      !uuidPattern.test(value?.key) ||
      !validShape ||
      !Number.isInteger(p.amountMinor) ||
      p.amountMinor < 1 ||
      p.amountMinor > maximumMinor
    )
      throw new Error();
    return value as Pending;
  } catch {
    throw new Error(
      "Submission recovery data is unavailable or unreadable. New transactions are blocked to avoid losing a possibly completed request. Restore access to this tab’s session storage and reload.",
    );
  }
}
export function savePending(pending: Pending): void {
  sessionStorage.setItem(pendingKey, JSON.stringify(pending));
}
export function clearPending(): void {
  sessionStorage.removeItem(pendingKey);
}
