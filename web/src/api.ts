import type { Account, HistoryPage, Pending, Receipt } from "./types";
import { maximumMinor } from "./money";
import { uuidPattern } from "./pending";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    public detail: string,
    public traceId?: string,
    public errors?: Record<string, string[]>,
  ) {
    super(detail);
  }
  get uncertain(): boolean {
    return ![400, 404, 409, 415].includes(this.status);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  try {
    const response = await fetch(`/api${path}`, {
      ...init,
      signal: AbortSignal.timeout(15000),
      headers: { Accept: "application/json", ...init?.headers },
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new ApiError(
        response.status,
        typeof problem.code === "string" ? problem.code : "http_error",
        typeof problem.detail === "string"
          ? problem.detail
          : `The server returned HTTP ${response.status}. Please try again.`,
        problem.traceId,
        problem.errors,
      );
    }
    return (await response.json()) as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(
      0,
      "connection_error",
      "Could not reach the API or read its response. Check that the API is running and try again.",
    );
  }
}

function receiptFrom(value: unknown, pending?: Pending): Receipt {
  const r = value as Receipt | null;
  const balance = (n: unknown) =>
    typeof n === "number" && Number.isInteger(n) && n >= 0 && n <= maximumMinor;
  const account = (id: unknown) => id === 1 || id === 2;
  const source =
    r && account(r.sourceAccountId) && balance(r.sourceBalanceAfterMinor);
  const destination =
    r &&
    account(r.destinationAccountId) &&
    balance(r.destinationBalanceAfterMinor);
  const shape =
    r &&
    ((r.type === "deposit" &&
      r.sourceAccountId === null &&
      r.sourceBalanceAfterMinor === null &&
      destination) ||
      (r.type === "withdrawal" &&
        source &&
        r.destinationAccountId === null &&
        r.destinationBalanceAfterMinor === null) ||
      (r.type === "transfer" &&
        source &&
        destination &&
        r.sourceAccountId !== r.destinationAccountId));
  if (
    !r ||
    !shape ||
    !uuidPattern.test(r.id) ||
    r.currency !== "USD" ||
    typeof r.createdAtUtc !== "string" ||
    !Number.isFinite(Date.parse(r.createdAtUtc)) ||
    !Number.isInteger(r.amountMinor) ||
    r.amountMinor < 1 ||
    r.amountMinor > maximumMinor ||
    (pending &&
      (pending.payload.type !== r.type ||
        pending.payload.amountMinor !== r.amountMinor ||
        (pending.payload.sourceAccountId ?? null) !== r.sourceAccountId ||
        (pending.payload.destinationAccountId ?? null) !==
        r.destinationAccountId))
  ) {
    throw new ApiError(
      0,
      "invalid_response",
      "The API response could not be verified. Retry the same request to safely determine its outcome.",
    );
  }
  return r;
}

export const api = {
  accounts: (): Promise<Account[]> => request("/accounts"),
  history: (
    accountId: number,
    page: number,
    pageSize: number,
  ): Promise<HistoryPage> =>
    request(
      `/accounts/${accountId}/transactions?page=${page}&pageSize=${pageSize}`,
    ),
  receipt: async (id: string): Promise<Receipt> =>
    receiptFrom(await request(`/transactions/${encodeURIComponent(id)}`)),
  execute: async (pending: Pending): Promise<Receipt> =>
    receiptFrom(
      await request("/transactions", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": pending.key,
        },
        body: JSON.stringify(pending.payload),
      }),
      pending,
    ),
};
