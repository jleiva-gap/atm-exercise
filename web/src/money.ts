export const maximumMinor = 1_000_000_000;
export function parseAmount(value: string): number | null {
  const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(value.trim());
  if (!match) return null;
  const minor =
    BigInt(match[1]) * 100n + BigInt((match[2] ?? "").padEnd(2, "0"));
  return minor >= 1n && minor <= BigInt(maximumMinor) ? Number(minor) : null;
}
export const formatMoney = (minor: number): string =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(
    minor / 100,
  );
export const amountInput = (minor: number): string =>
  `${Math.floor(minor / 100)}.${String(minor % 100).padStart(2, "0")}`;
