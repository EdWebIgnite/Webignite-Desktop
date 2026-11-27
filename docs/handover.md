# Webignite Desktop: where we are (handover, 27 Nov 2026, evening)

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
3. Done: the client page as the hub.
4. Done: expenses with receipts, supplier bills kept here. Still to come: bills and money entries going between the two PCs.
5. Done (first pass): FNB statement import, matching and learning. Next: try it on a real FNB download, then reports.
6. Reports for us (profit by month and tax year, cash, debtors and creditors, provisional tax helper), and an accountant's corner for the sister: trial balance, journals, closing a period, Excel exports, and her own login with an accountant role.
7. Bring in the Xero and QuickBooks history, remove duplicates (both were run at the same time), and check the totals.
8. Switch Xero off.

Answers Eddie gave:
- Fields on quotes and invoices: Project for now, more later.
- His sister will probably work on her own PC, with an accountant role.
- The business is a (Pty) Ltd. The books use share capital, director's loan account and dividends.

## Merged in the session before (PRs #335 to #358)

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

## Done this session (PRs #359 to #366)

- #359: the client page as the hub: money strip (owes, overdue, paid and invoiced this tax year, usually pays in), quick actions together.
- #360: expenses keep their receipts (`ReceiptFiles`, `Receipts`).
- #361, #362: mail run-through: assistant drafts replace their earlier copy, a mail that comes back unsent never replaces one being written, From changes, stale drafts, the signature logo on assistant drafts, mail pushed during a check, search off the screen thread, waiting for a send before closing, linked pictures in forwards.
- #363: Cc, Bcc and Reply-To as buttons; Avalonia's own warnings and errors go to the log (`AvaloniaLog`).
- #364: one list of projects per client: their Planner projects, with For (an agency's end client) and their own rate; the old list on the client moved across at start-up.
- #365: file mail under a client's project (IMAP keyword, Projects list in Mail's folder list).
- #366: supplier bills kept in Webignite when the invoicing switch is on; Paid adds the expense.
- #367: bank statement import (OFX or CSV, FNB's layout included), matched to invoices, bills and what's already typed in, learning the kind of expense from corrections. Not yet tried with a real FNB file: ask Eddie for one (amounts and names can be blanked) and check the reader.

## The draft window on Windows

A mail in its own window (`ComposeWindow`) didn't lay itself out again when it grew, maximised or dragged. Logs showed no layout or drawing errors; the one difference from a popped-out page (which works) was that the draft window was owned by the main window (`Show(this)`). #369 shows it as a window of its own (`Show()`, own taskbar button, placed near the main window). Eddie: still there on 0.2.334, only with Edit draft, and X never closes it (the Keep this as a draft? bar never shows, so the window stops redrawing altogether). The Chat window is fine. A draft differs from a new mail by its earlier message, which the HTML viewer (`HtmlPanel`) was handed as soon as the draft opened, though folded away; #370 hands it over only on Show the earlier message (`ShownQuoteHtml`). If it still happens, ask Eddie to edit a draft with no earlier message: if that one's fine, the viewer is confirmed; if not, look at what `RichHtml.Read` makes of the draft's HTML.

## Still open

- Instant push from the sync server: parked by Eddie.
- Update the sync server for the recent changes: Eddie wants it, not yet. Check what changed since the server last moved, then give exact steps for the NAS.
- Money entries, receipts and supplier bills stay on the PC they were made on; they don't sync yet.
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
3. When CI is green: squash-merge with `expectedHeadSha`, unsubscribe, delete the check-in, and reset the session branch to `origin/main`.
4. Commits end with the Co-Authored-By and Claude-Session lines. PR bodies end with the Claude Code line and the session link. No model names anywhere else.
