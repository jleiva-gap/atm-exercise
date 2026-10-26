import type {
  Account,
  FieldErrors,
  TransactionForm,
  TransactionPayload,
} from "./types";
import { formatMoney, maximumMinor, parseAmount } from "./money";

export function validateTransaction(
  form: TransactionForm,
  accounts: Account[],
): { payload?: TransactionPayload; errors: FieldErrors } {
  const errors: FieldErrors = {};
  const source = accounts.find((a) => String(a.id) === form.source);
  const destination = accounts.find((a) => String(a.id) === form.destination);
  if (form.type !== "deposit" && !source)
    errors.source = "Choose an account to withdraw from.";
  if (form.type !== "withdrawal" && !destination)
    errors.destination = "Choose an account to deposit into.";
  if (
    form.type === "transfer" &&
    source &&
    destination &&
    source.id === destination.id
  )
    errors.destination = "Choose a different destination account.";
  const amountMinor = parseAmount(form.amount);
  if (amountMinor === null)
    errors.amount =
      "Enter $0.01 to $10,000,000.00 with up to two decimal places. Use a decimal point, without commas.";
  else if (
    form.type !== "deposit" &&
    source &&
    amountMinor > source.balanceMinor
  )
    errors.amount = `Only ${formatMoney(source.balanceMinor)} is available in ${source.name}.`;
  else if (
    form.type !== "withdrawal" &&
    destination &&
    amountMinor > maximumMinor - destination.balanceMinor
  )
    errors.amount = `${destination.name} would exceed the $10,000,000.00 balance limit.`;
  if (Object.keys(errors).length || amountMinor === null) return { errors };
  return {
    errors,
    payload: {
      type: form.type,
      amountMinor,
      ...(form.type !== "deposit" ? { sourceAccountId: source!.id } : {}),
      ...(form.type !== "withdrawal"
        ? { destinationAccountId: destination!.id }
        : {}),
    },
  };
}
