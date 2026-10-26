import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import App from "./App";
import { pendingKey } from "./pending";
import type { Receipt, TransactionPayload } from "./types";

const receipt: Receipt = {
  id: "2475ad13-2fef-4efb-93d5-f2cfdc605ce6",
  type: "deposit",
  sourceAccountId: null,
  destinationAccountId: 1,
  amountMinor: 1250,
  currency: "USD",
  createdAtUtc: "2026-10-23T12:00:00Z",
  sourceBalanceAfterMinor: null,
  destinationBalanceAfterMinor: 11250,
};
let writes: { body: unknown; key: string; stored: string | null }[];
let execute: (payload: TransactionPayload) => Promise<Response>;
let accountsFail: boolean;
let historyPages: number[];
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status });
beforeEach(() => {
  sessionStorage.clear();
  writes = [];
  historyPages = [];
  accountsFail = false;
  execute = async (payload) =>
    json(
      {
        ...receipt,
        ...payload,
        sourceAccountId: payload.sourceAccountId ?? null,
        destinationAccountId: payload.destinationAccountId ?? null,
        sourceBalanceAfterMinor: payload.sourceAccountId
          ? 10000 - payload.amountMinor
          : null,
        destinationBalanceAfterMinor: payload.destinationAccountId
          ? (payload.destinationAccountId === 1 ? 10000 : 5000) +
          payload.amountMinor
          : null,
      },
      201,
    );
  vi.stubGlobal("fetch", async (path: string, init?: RequestInit) => {
    if (init?.method === "POST") {
      writes.push({
        body: JSON.parse(init.body as string),
        key: (init.headers as Record<string, string>)["Idempotency-Key"],
        stored: sessionStorage.getItem(pendingKey),
      });
      return execute(JSON.parse(init.body as string));
    }
    if (path === "/api/accounts")
      return accountsFail
        ? json({ detail: "Read temporarily failed." }, 500)
        : json([
          { id: 1, name: "Checking", currency: "USD", balanceMinor: 10000 },
          { id: 2, name: "Savings", currency: "USD", balanceMinor: 5000 },
        ]);
    if (path.startsWith("/api/accounts/")) {
      const page = Number(
        new URL(`http://test${path}`).searchParams.get("page"),
      );
      historyPages.push(page);
      return json({
        items: page === 1 ? [receipt] : [],
        page,
        pageSize: 10,
        hasMore: page === 1,
      });
    }
    return json(receipt);
  });
});
afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});
async function ready() {
  render(<App />);
  await screen.findByRole("button", { name: "Confirm deposit" });
  await waitFor(() =>
    expect(
      screen.getByRole("button", { name: "Confirm deposit" }),
    ).toBeEnabled(),
  );
}
async function deposit() {
  await userEvent.type(screen.getByLabelText("Amount (USD)"), "12.50");
  await userEvent.click(
    screen.getByRole("button", { name: "Confirm deposit" }),
  );
}

it("submits exact cents after storing recovery and shows a full receipt", async () => {
  await ready();
  await deposit();
  expect(
    await screen.findByRole("heading", { name: "Transaction confirmed" }),
  ).toBeInTheDocument();
  expect(writes[0].body).toEqual({
    type: "deposit",
    destinationAccountId: 1,
    amountMinor: 1250,
  });
  expect(JSON.parse(writes[0].stored!).key).toBe(writes[0].key);
  expect(sessionStorage.getItem(pendingKey)).toBeNull();
  expect(
    within(screen.getByLabelText("Transaction receipt")).getByText(receipt.id),
  ).toBeInTheDocument();
});
it("shows inline amount feedback and focuses it without sending", async () => {
  await ready();
  await userEvent.type(screen.getByLabelText("Amount (USD)"), "1.001");
  await userEvent.click(
    screen.getByRole("button", { name: "Confirm deposit" }),
  );
  expect(screen.getByLabelText("Amount (USD)")).toHaveAttribute(
    "aria-invalid",
    "true",
  );
  expect(screen.getByLabelText("Amount (USD)")).toHaveFocus();
  expect(writes).toHaveLength(0);
});
it("validates displayed funds and same-account transfers", async () => {
  await ready();
  await userEvent.click(screen.getByRole("tab", { name: "Withdraw" }));
  await userEvent.type(screen.getByLabelText("Amount (USD)"), "100.01");
  await userEvent.click(
    screen.getByRole("button", { name: "Confirm withdrawal" }),
  );
  expect(screen.getByText(/Only \$100.00 is available/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole("tab", { name: "Transfer" }));
  await userEvent.selectOptions(screen.getByLabelText("To account"), "1");
  await userEvent.click(
    screen.getByRole("button", { name: "Confirm transfer" }),
  );
  expect(
    screen.getByText("Choose a different destination account."),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
});
it.each(["withdrawal", "transfer"] as const)(
  "executes %s with the correct account fields",
  async (type) => {
    await ready();
    await userEvent.click(
      screen.getByRole("tab", {
        name: type === "withdrawal" ? "Withdraw" : "Transfer",
      }),
    );
    await userEvent.type(screen.getByLabelText("Amount (USD)"), "10.01");
    await userEvent.click(
      screen.getByRole("button", { name: `Confirm ${type}` }),
    );
    await screen.findByRole("heading", { name: "Transaction confirmed" });
    expect(writes[0].body).toEqual({
      type,
      sourceAccountId: 1,
      ...(type === "transfer" ? { destinationAccountId: 2 } : {}),
      amountMinor: 1001,
    });
  },
);
it("blocks double submits while the response is pending", async () => {
  let resolve!: (r: Response) => void;
  execute = () =>
    new Promise((r) => {
      resolve = r;
    });
  await ready();
  await userEvent.type(screen.getByLabelText("Amount (USD)"), "12.50");
  const button = screen.getByRole("button", { name: "Confirm deposit" });
  fireEvent.click(button);
  fireEvent.click(button);
  expect(writes).toHaveLength(1);
  expect(screen.getByLabelText("Amount (USD)")).toBeDisabled();
  await act(async () => resolve(json(receipt, 201)));
});
it.each([500, 503])(
  "keeps HTTP %s pending through remount and retries the original key/body",
  async (status) => {
    execute = async () =>
      json(
        {
          code: "database_busy",
          detail: "Database is busy.",
          traceId: "retry-trace",
        },
        status,
      );
    const view = render(<App />);
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Confirm deposit" }),
      ).toBeEnabled(),
    );
    await deposit();
    await screen.findByRole("button", { name: "Retry same request" });
    const first = writes[0];
    view.unmount();
    render(<App />);
    expect(
      screen.getByText(/Unresolved submission restored/),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Amount (USD)")).toBeDisabled();
    execute = async () => json(receipt, 201);
    await userEvent.click(
      screen.getByRole("button", { name: "Retry same request" }),
    );
    await screen.findByRole("heading", { name: "Transaction confirmed" });
    expect(writes[1].key).toBe(first.key);
    expect(writes[1].body).toEqual(first.body);
  },
);
it("retains a lost-response request for safe retry", async () => {
  execute = async () => {
    throw new TypeError("connection lost");
  };
  await ready();
  await deposit();
  expect(
    await screen.findByRole("button", { name: "Retry same request" }),
  ).toBeEnabled();
  expect(sessionStorage.getItem(pendingKey)).not.toBeNull();
});
it("shows definitive server failures, trace and field errors and unlocks the form", async () => {
  execute = async () =>
    json(
      {
        code: "validation_error",
        detail: "Amount rejected.",
        traceId: "trace-409",
        errors: { amountMinor: ["The amount changed."] },
      },
      400,
    );
  await ready();
  await deposit();
  expect(await screen.findByText("Amount rejected.")).toBeInTheDocument();
  expect(screen.getByText("The amount changed.")).toBeInTheDocument();
  expect(screen.getByText(/trace-409/)).toBeInTheDocument();
  expect(sessionStorage.getItem(pendingKey)).toBeNull();
  expect(screen.getByLabelText("Amount (USD)")).toBeEnabled();
});
it("keeps confirmed success when follow-up reads fail and refreshes without another write", async () => {
  execute = async () => {
    accountsFail = true;
    return json(receipt, 201);
  };
  await ready();
  await deposit();
  await screen.findByRole("heading", { name: "Transaction confirmed" });
  await screen.findByText(/Read temporarily failed/);
  accountsFail = false;
  await userEvent.click(
    screen.getByRole("button", { name: "Refresh balances and history" }),
  );
  await waitFor(() =>
    expect(
      screen.queryByText(/Read temporarily failed/),
    ).not.toBeInTheDocument(),
  );
  expect(writes).toHaveLength(1);
});
it("does not send if saving recovery fails", async () => {
  await ready();
  vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
    throw new Error("quota");
  });
  await deposit();
  expect(
    await screen.findByText(/Could not save submission recovery/),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
});
it("blocks new writes for corrupt pending data", async () => {
  sessionStorage.setItem(pendingKey, "{broken");
  render(<App />);
  expect(
    await screen.findByText(/Submission recovery data is unavailable/),
  ).toBeInTheDocument();
  expect(screen.getByLabelText("Amount (USD)")).toBeDisabled();
});
it("pages history and resets it when switching accounts", async () => {
  await ready();
  await userEvent.click(
    await screen.findByRole("button", { name: "Next page" }),
  );
  await screen.findByText("Page 2");
  await userEvent.click(screen.getByRole("button", { name: /Select Savings/ }));
  await screen.findByText("Page 1");
  expect(historyPages).toContain(2);
});
it("looks up a receipt by UUID and rejects invalid lookup input", async () => {
  await ready();
  await userEvent.click(screen.getByText("Find a receipt"));
  await userEvent.type(screen.getByLabelText("Transaction ID"), "bad");
  await userEvent.click(screen.getByRole("button", { name: "Find receipt" }));
  expect(
    screen.getByText("Enter a valid transaction UUID."),
  ).toBeInTheDocument();
  await userEvent.clear(screen.getByLabelText("Transaction ID"));
  await userEvent.type(screen.getByLabelText("Transaction ID"), receipt.id);
  await userEvent.click(screen.getByRole("button", { name: "Find receipt" }));
  expect(
    await screen.findByLabelText("Transaction receipt"),
  ).toBeInTheDocument();
});
it("replays a confirmed operation with its original key", async () => {
  await ready();
  await deposit();
  await screen.findByRole("heading", { name: "Transaction confirmed" });
  await userEvent.click(
    screen.getByRole("button", { name: "Verify safe replay" }),
  );
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(writes[1].key).toBe(writes[0].key);
  expect(writes[1].body).toEqual(writes[0].body);
});
it("blocks another transaction if clearing a confirmed pending record fails", async () => {
  const remove = vi
    .spyOn(Storage.prototype, "removeItem")
    .mockImplementation(() => {
      throw new Error("storage blocked");
    });
  await ready();
  await deposit();
  await screen.findByRole("heading", { name: "Transaction confirmed" });
  expect(screen.getByLabelText("Amount (USD)")).toBeDisabled();
  expect(sessionStorage.getItem(pendingKey)).not.toBeNull();
  remove.mockRestore();
  await userEvent.click(
    screen.getByRole("button", { name: "Retry same request" }),
  );
  await waitFor(() =>
    expect(screen.getByLabelText("Amount (USD)")).toBeEnabled(),
  );
  expect(writes[1].key).toBe(writes[0].key);
});
it("ignores a slow history response from the previously selected account", async () => {
  let resolveOld!: (response: Response) => void;
  const original = globalThis.fetch;
  vi.stubGlobal("fetch", async (path: string, init?: RequestInit) =>
    path.includes("/accounts/1/transactions")
      ? new Promise<Response>((resolve) => {
        resolveOld = resolve;
      })
      : original(path, init),
  );
  await ready();
  await userEvent.click(screen.getByRole("button", { name: /Select Savings/ }));
  await waitFor(() =>
    expect(
      screen.getByRole("heading", { name: "Savings history" }),
    ).toBeInTheDocument(),
  );
  await act(async () =>
    resolveOld(
      json({
        items: [{ ...receipt, id: "old-account-only" }],
        page: 1,
        pageSize: 10,
        hasMore: false,
      }),
    ),
  );
  expect(
    screen.queryByRole("button", { name: /old-account-only/ }),
  ).not.toBeInTheDocument();
});
it("announces account balances and transaction amounts to assistive technology", async () => {
  await ready();
  expect(
    screen.getByRole("button", { name: /Select Checking/ }),
  ).toHaveAccessibleDescription(/\$100.00/);
  const row = await screen.findByRole("button", {
    name: /View deposit receipt/,
  });
  expect(row).toHaveAccessibleDescription(/Credit \$12.50/);
});
it("supports keyboard navigation between operation tabs", async () => {
  await ready();
  const depositTab = screen.getByRole("tab", { name: "Deposit" });
  depositTab.focus();
  fireEvent.keyDown(depositTab, { key: "ArrowRight" });
  expect(screen.getByRole("tab", { name: "Withdraw" })).toHaveFocus();
  expect(screen.getByRole("tab", { name: "Withdraw" })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  fireEvent.keyDown(screen.getByRole("tab", { name: "Withdraw" }), {
    key: "End",
  });
  expect(screen.getByRole("tab", { name: "Transfer" })).toHaveFocus();
});
