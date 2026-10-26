import { afterEach, expect, it, vi } from "vitest";
import { api, ApiError } from "./api";
import { loadPending, savePending, clearPending, pendingKey } from "./pending";
const pending = {
  key: "de17944c-67bd-4c35-ae45-175c2bc0da5b",
  payload: {
    type: "deposit" as const,
    destinationAccountId: 1,
    amountMinor: 1250,
  },
};
const receipt = {
  id: "2475ad13-2fef-4efb-93d5-f2cfdc605ce6",
  type: "deposit",
  sourceAccountId: null,
  destinationAccountId: 1,
  amountMinor: 1250,
  currency: "USD",
  createdAtUtc: "2026-10-23T12:00:00Z",
  sourceBalanceAfterMinor: null,
  destinationBalanceAfterMinor: 1250,
};
afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});
it("sends the persisted key and exact integer payload", async () => {
  let sent: RequestInit | undefined;
  vi.stubGlobal("fetch", async (_url: string, init: RequestInit) => {
    sent = init;
    return new Response(JSON.stringify(receipt), { status: 201 });
  });
  await api.execute(pending);
  expect(sent?.headers).toMatchObject({
    "Idempotency-Key": pending.key,
    "Content-Type": "application/json",
  });
  expect(JSON.parse(sent?.body as string)).toEqual(pending.payload);
});
it.each([400, 404, 409, 415])(
  "treats HTTP %s as definitive and exposes field/trace details",
  async (status) => {
    vi.stubGlobal(
      "fetch",
      async () =>
        new Response(
          JSON.stringify({
            status,
            code: "validation_error",
            detail: "Invalid amount.",
            errors: { amountMinor: ["Too small."] },
            traceId: "trace-1",
          }),
          { status },
        ),
    );
    try {
      await api.execute(pending);
      expect.fail("expected error");
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect(error).toMatchObject({
        uncertain: false,
        detail: "Invalid amount.",
        traceId: "trace-1",
        errors: { amountMinor: ["Too small."] },
      });
    }
  },
);
it.each([500, 503, 502, 408])(
  "preserves uncertainty for HTTP %s",
  async (status) => {
    vi.stubGlobal("fetch", async () => new Response("unavailable", { status }));
    await expect(api.execute(pending)).rejects.toMatchObject({
      uncertain: true,
    });
  },
);
it("preserves uncertainty for network errors", async () => {
  vi.stubGlobal("fetch", async () => {
    throw new TypeError("offline");
  });
  await expect(api.execute(pending)).rejects.toMatchObject({ uncertain: true });
});
it("persists and restores the original submission on reload", () => {
  savePending(pending);
  expect(loadPending()).toEqual(pending);
  clearPending();
  expect(loadPending()).toBeNull();
});
it("blocks malformed recovery records instead of silently forgetting them", () => {
  sessionStorage.setItem(pendingKey, "{bad");
  expect(() => loadPending()).toThrow(/recovery/i);
});
it("rejects structurally invalid stored requests", () => {
  sessionStorage.setItem(
    pendingKey,
    JSON.stringify({ key: "bad", payload: pending.payload }),
  );
  expect(() => loadPending()).toThrow(/recovery/i);
});
it.each([
  {},
  { ...receipt, amountMinor: 2000 },
  { ...receipt, createdAtUtc: "bad" },
  { ...receipt, destinationBalanceAfterMinor: null },
])(
  "does not confirm a malformed or mismatched success response",
  async (body) => {
    vi.stubGlobal(
      "fetch",
      async () => new Response(JSON.stringify(body), { status: 201 }),
    );
    await expect(api.execute(pending)).rejects.toMatchObject({
      uncertain: true,
    });
  },
);
