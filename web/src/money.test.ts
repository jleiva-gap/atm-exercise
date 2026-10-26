import { describe, expect, it } from "vitest";
import { parseAmount, formatMoney } from "./money";
import { validateTransaction } from "./validation";

const accounts = [
  { id: 1, name: "Checking", currency: "USD", balanceMinor: 10000 },
  { id: 2, name: "Savings", currency: "USD", balanceMinor: 999999950 },
];
describe("exact dollars to cents", () => {
  it.each([
    ["0.01", 1],
    ["12.50", 1250],
    [" 0002.1 ", 210],
    ["10000000.00", 1000000000],
  ])("parses %s exactly", (input, expected) =>
    expect(parseAmount(input)).toBe(expected),
  );
  it.each([
    "",
    "0",
    "-1",
    "+1",
    ".5",
    "1.",
    "1.001",
    "1e2",
    "1,000",
    "NaN",
    "Infinity",
    "10000000.01",
    "999999999999999999999",
  ])("rejects %s", (input) => expect(parseAmount(input)).toBeNull());
  it("formats cents as USD", () => expect(formatMoney(1250)).toBe("$12.50"));
});
describe("transaction form validation", () => {
  it("normalizes deposit without a source", () =>
    expect(
      validateTransaction(
        { type: "deposit", source: "", destination: "1", amount: "12.50" },
        accounts,
      ),
    ).toEqual({
      payload: { type: "deposit", destinationAccountId: 1, amountMinor: 1250 },
      errors: {},
    }));
  it("requires an account", () =>
    expect(
      validateTransaction(
        { type: "withdrawal", source: "", destination: "", amount: "1" },
        accounts,
      ).errors.source,
    ).toBeTruthy());
  it("rejects same account transfer", () =>
    expect(
      validateTransaction(
        { type: "transfer", source: "1", destination: "1", amount: "1" },
        accounts,
      ).errors.destination,
    ).toMatch(/different/i));
  it("rejects unavailable funds", () =>
    expect(
      validateTransaction(
        { type: "withdrawal", source: "1", destination: "", amount: "100.01" },
        accounts,
      ).errors.amount,
    ).toMatch(/available/i));
  it("rejects destination overflow", () =>
    expect(
      validateTransaction(
        { type: "deposit", source: "", destination: "2", amount: "0.51" },
        accounts,
      ).errors.amount,
    ).toMatch(/limit/i));
  it("accepts a transfer of exact available cents", () =>
    expect(
      validateTransaction(
        { type: "transfer", source: "1", destination: "2", amount: "0.50" },
        accounts,
      ).payload,
    ).toEqual({
      type: "transfer",
      sourceAccountId: 1,
      destinationAccountId: 2,
      amountMinor: 50,
    }));
});
