# Webignite Desktop: where we are (handover, 27 Nov 2026)

For the next working session. Read this with `CLAUDE.md` (the house rules), `docs/architecture.md` (the map of the app, kept up to date in every PR) and `docs/adding-things.md` (recipes).

## Who and how we work

- Eddie owns Webignite (Pty) Ltd, a South African web studio. His partner Jaco also uses the app. Eddie's sister is an accountant and does the books.
- Standing rules from Eddie:
  - One PR per step. Merge it as soon as CI is green, then start the next item without asking.
  - Send before and after screenshots of UI changes.
  - Plain South African English, short and to the point, no em dashes, no AI-sounding writing.
  - Ask before answering when something is unclear. When guiding a setup, give exact steps.
- Security, not negotiable:
  - Invoice and quote mail goes only from the app's own mailbox, never through Xero's email.
  - No bank details or other secrets in the repo (it is public).
  - Claude and ChatGPT only ever draft or suggest. They never approve, send, delete, or confirm client changes.
- Webignite is not VAT registered. SA tax year: 1 March to end of February.

## Commands

```bash
export PATH=$HOME/.dotnet:$PATH
dotnet build Webignite.slnx
dotnet test --solution Webignite.slnx --no-build
timeout 1500 dotnet run --project tools/Snapshot --no-build -- /tmp/claude-0/snapN > /tmp/claude-0/snapN.log 2>&1
dotnet run --project tools/Measure --no-build
```

- Snapshot: grep the log for `^FAIL`, `check(s) failed` and `Unhandled`.
- Measure limits are in `tools/Measure/limits.json`. CI fails on them, so run it before pushing anything that touches styles or animations. A style that matches every control once leaked memory (see #353).
- Don't rebuild while a snapshot run is going: it uses the built files.

## The big goal: move off Xero and QuickBooks

Eddie wants the app to be the business's own accounting, simple for him and complete enough for his sister. Xero keeps running alongside until he decides to switch. The guiding rule: do a thing once, in one place, and everything else follows.

The plan:

1. Done: invoicing in Webignite (steps 1 and 2 below).
2. Done: project name and your own fields on quotes and invoices.
3. **In progress:** the client page as the hub.
4. Next: expenses and supplier bills, with receipts.
5. Next: FNB statement import (CSV/OFX), auto-matching to invoices and expenses, learning from corrections.
6. Reports for us (profit by month and tax year, cash, debtors and creditors, provisional tax helper), and an accountant's corner for the sister: trial balance, journals, closing a period, Excel exports, and her own login with an accountant role.
7. Bring in the Xero and QuickBooks history, remove duplicates (both were run at the same time), and check the totals.
8. Switch Xero off.

Answers Eddie gave:
- Fields on quotes and invoices: Project for now, more later.
- His sister will probably work on her own PC, with an accountant role.
- The business is a (Pty) Ltd. The books use share capital, director's loan account and dividends.

## Merged in this session (PRs #335 to #355)

- **Layout and memory:**
  - #335: five areas as tabs across the top (Mail, Business, Planner, Computer, Tools), a page of tiles for each area, and the sidebar as a pinned shortlist.
  - #336: long lists show the first 12 rows (start-up memory went from 41 MB to 18 MB).
  - #338: the Menu button version of the menu is gone.
  - #339 and #347: Clients and billing are one entry, with tabs for Clients, Quotes, Invoices, Money and Recurring.
- **Sync and chat:**
  - #340: chat listens from the moment the app opens.
  - #341 and #342: the app says when sync has stopped and why.
  - #343: the licence check is off for now.
  - #344: NAS guide (reserve the NAS's address).
- **Assistants:**
  - #345: assistants can change Xero drafts; the change goes to Xero only when Eddie updates it.
  - #348: Claude and ChatGPT only suggest client changes. Old and new show side by side on the client's page, with Accept or No.
- **Books:**
  - #346: double-entry books in Core, with a chart of accounts for a (Pty) Ltd, trial balance, profit and loss, balance sheet and tax years.
- **Clients:**
  - #347: a client's page leads with a read-only summary and an Edit button.
- **Quotes and invoices:**
  - #349: Project field (offers the client's projects) and your own fields in Settings. Printed fields go into Xero's reference.
- **Billing polish:**
  - #350, the editor: customer from your clients, date boxes, saves by itself, tidies typed amounts, no VAT row.
  - #351, the lists: three figures you can click (owed, overdue, paid this month).
  - #352, Money: everyday parts first, plain labels.
- **Movement:**
  - #353: sliding tab highlight, panels ease in, folding rows animate, tiles lift. Follows the Motion setting and Windows' animation setting.
- **Invoicing without Xero,** behind Settings > Quotes and invoices > "Make quotes and invoices in Webignite instead of Xero" (off):
  - #354: issue with numbering that carries on from current numbers, email with our own PDF, lock once issued, record payments.
  - #355: void, credit notes (CN-0001 on), quote accepted or declined, payments shown in Money, books worked out from the invoices (`Books/Postings.cs`).

## In progress: the client page as the hub

On branch `claude/client-hub` (not yet a PR):

- **Done:** `Core/Clients/ClientMoney.cs`, with tests. It gives what the client owes and how much is overdue, what they paid and were invoiced this tax year, and how many days they usually take to pay (`ClientMoney.PaysIn`).
- **To do:**
  1. A money strip at the top of the client page, replacing the one-line "Owed" text: owes and overdue, paid this tax year, invoiced this tax year, "Usually pays in 14 days".
  2. Quick actions together: New quote, New invoice, Draft statement, Write to them.
  3. Their Planner projects in the Projects row (projects with that `ClientId`), each opening with `services.Go.Project(id)`.
  4. Snapshot checks and screenshots, `docs/architecture.md`, PR.

## Still open

- Instant push from the sync server: parked by Eddie.
- Task #90, licences and subscriptions: on hold (only Eddie and Jaco use the app).
- Task #102, the NAS sync server guide: mostly done. The NAS is at 192.168.50.42, reserved in the ASUS router, with port 443 forwarded to it.
- Asked but never answered: merge the two top bars into one row when the dock is in use.
- For Eddie to do:
  - Update the app on both PCs.
  - Refresh the ChatGPT connector after updating (ChatGPT Settings > Apps and Connectors > Webignite > Refresh, then start a new chat).
  - Turn on the invoicing switch when he is ready to stop making new invoices in Xero.

## PR routine

1. Branch from `origin/main`, build, test, snapshot (and measure when styles or animations change).
2. Push, open the PR, subscribe to its activity, and set a 50-minute safety check-in.
3. When CI is green: squash-merge with `expectedHeadSha`, unsubscribe, delete the check-in, and reset `claude/lucid-feynman-ku7i1w` to `origin/main`.
4. Commits end with the Co-Authored-By and Claude-Session lines. PR bodies end with the Claude Code line and the session link. No model names anywhere else.
