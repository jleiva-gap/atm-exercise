export type TransactionType = "deposit" | "withdrawal" | "transfer";
export interface Account {
  id: number;
  name: string;
  currency: string;
  balanceMinor: number;
}
export interface TransactionPayload {
  type: TransactionType;
  sourceAccountId?: number;
  destinationAccountId?: number;
  amountMinor: number;
}
export interface Pending {
  key: string;
  payload: TransactionPayload;
}
export interface Receipt {
  id: string;
  type: TransactionType;
  sourceAccountId: number | null;
  destinationAccountId: number | null;
  amountMinor: number;
  currency: string;
  createdAtUtc: string;
  sourceBalanceAfterMinor: number | null;
  destinationBalanceAfterMinor: number | null;
}
export interface HistoryPage {
  items: Receipt[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
export interface TransactionForm {
  type: TransactionType;
  source: string;
  destination: string;
  amount: string;
}
export type FieldErrors = Partial<
  Record<"source" | "destination" | "amount", string>
>;
