# How Webignite is put together

A map of the app in four levels, using the C4 model: what Webignite talks to, the programs it is made of, the parts inside each, and how the main jobs flow through them. The diagrams are Mermaid, so GitHub draws them. Keep this file up to date in the same PR when a project, a page, a store or an outside service is added or removed.

Recipes for adding things are in [adding-things.md](adding-things.md). The house rules are in [CLAUDE.md](../CLAUDE.md).

## 1. What Webignite talks to

One person (Eddie) uses one Windows PC. Everything is kept on that PC; outside services are only reached for the job they do.

```mermaid
flowchart LR
    eddie(["Eddie"])
    app["Webignite<br/>desktop app"]
    ai["Claude Desktop<br/>or ChatGPT"]

    eddie -->|uses| app
    eddie -->|asks| ai
    ai -->|"prepares drafts and to-dos<br/>(never approves, sends or deletes)"| app

    app -->|"DRAFT quotes and invoices,<br/>reads payments, PDFs"| xero[("Xero")]
    app -->|"IMAP read, SMTP send"| mail[("Mail server<br/>(xneelo)")]
    app -->|"uptime, speed,<br/>domain lookups"| web[("Client websites,<br/>PageSpeed, RDAP, DNS")]
    app -->|"encrypted backups"| onedrive[("OneDrive folder")]
    app -->|"checks for updates"| github[("GitHub releases")]
    app -->|"secrets and the<br/>database key"| windows[("Windows Credential<br/>Manager and DPAPI")]
```

| Outside service | What for | Where in the code |
| --- | --- | --- |
| Xero | Quotes and invoices go in as DRAFT; payments and statuses are read; PDFs fetched for emailing. Sign-in is OAuth with PKCE in the browser. | `Webignite.Xero` |
| Mail server | Mail is read over IMAP into a copy on this PC and sent over SMTP. | `Webignite.Mail` (MailKit) |
| Claude Desktop, ChatGPT | They start `Webignite.Connector`, which offers tools over MCP. | `Webignite.Connector` |
| Websites, Google PageSpeed, rdap.org, dns.google, Cloudflare DNS | Site check, uptime watch, domain and hosting reports. | `Webignite.Services`, `Core/Web` |
| OneDrive folder | Backups land in a synced folder, encrypted with a password. | `Webignite.Data/Backups.cs` |
| GitHub releases | Velopack checks for and downloads updates. | `Desktop/ViewModels/UpdateViewModel.cs` |
| Windows | Credential Manager holds mail passwords and the Xero sign-in; DPAPI protects the database key. | `Webignite.Data/Secrets.cs`, `VaultKey.cs`, `Webignite.Xero/WindowsCredentialStore.cs` |

## 2. The programs and projects

Two programs run on the PC, the app and the connector, and they share one encrypted database file.

```mermaid
flowchart TB
    subgraph pc["Eddie's PC"]
        desktop["Webignite.exe<br/>Avalonia app"]
        connector["Webignite.Connector.exe<br/>MCP over stdin and stdout"]
        db[("webignite.db<br/>SQLite, every record<br/>encrypted with the Vault")]
        logs[("logs folder<br/>AppLog")]
    end
    ai["Claude Desktop / ChatGPT"] -->|starts and talks to| connector
    desktop --> db
    connector --> db
    desktop --> logs
```

### Projects and what may use what

Arrows point at what a project uses. Core uses nothing, so every rule in it can be tested on its own.

```mermaid
flowchart BT
    core["Webignite.Core<br/>rules, records, no packages"]
    data["Webignite.Data<br/>SQLite store, Vault, backups, secrets, log"]
    xero["Webignite.Xero<br/>Xero API, sign-in, drafts, sending"]
    mail["Webignite.Mail<br/>IMAP and SMTP, composing, assistant mail"]
    services["Webignite.Services<br/>background checks"]
    connector["Webignite.Connector<br/>MCP tools"]
    desktop["Webignite.Desktop<br/>windows, pages, view models"]
    tests["tests/Webignite.Tests"]
    snapshot["tools/Snapshot<br/>screenshots, headless"]

    data --> core
    xero --> core
    mail --> core
    mail --> data
    services --> core
    services --> data
    services --> xero
    connector --> core
    connector --> data
    connector --> mail
    desktop --> core
    desktop --> data
    desktop --> xero
    desktop --> mail
    desktop --> services
    tests --> core
    tests --> data
    tests --> xero
    tests --> mail
    tests --> services
    snapshot --> desktop
```

| Project | Holds | Outside packages |
| --- | --- | --- |
| `Webignite.Core` | Every business rule and record: documents, money, clients, mail rules, time, layout. Pure functions with dates passed in. | none |
| `Webignite.Data` | `LocalStore` (SQLite, one table per kind of record, payload encrypted by `Vault`), `VaultKey`, backups, secrets, `AppLog`, the assistant rules for documents and to-dos. | Microsoft.Data.Sqlite, ProtectedData |
| `Webignite.Xero` | `XeroSession` (sign-in, tokens), `XeroApi`, `XeroDrafts` (create and check drafts), `XeroSending` (PDFs, mark sent), `XeroStandings` (payments). | none |
| `Webignite.Mail` | `MailService` (sync, read, mark, move, send), `MailComposer`, `MailReader`, `AssistantTools` (what an assistant may do with mail). | MailKit |
| `Webignite.Services` | `PaymentWatch`, `UptimeWatch`, `SiteChecker`: each has `Due(now)` and `RunAsync`. | none |
| `Webignite.Connector` | The MCP server and its tools: documents, to-dos, mail. Logs go to stderr only. | ModelContextProtocol, Hosting |
| `Webignite.Desktop` | The app: `AppServices`, `AppSettings`, `Navigator`, pages (view models and views), controls, styles. | Avalonia, CommunityToolkit.Mvvm, HtmlRenderer, QRCoder, Docnet.Core, Velopack |
| `tests/Webignite.Tests` | xUnit v3 tests, one file per area. | xunit.v3 |
| `tools/Snapshot` | Builds sample data in a throwaway database and saves a screenshot of each screen. | Avalonia.Headless |

## 3. Inside the projects

### Core: rules by area

```mermaid
flowchart LR
    subgraph core["Webignite.Core"]
        documents["Documents<br/>Document, Line, Money, Receivables,<br/>Chasers, DocumentHeadline, Validator, Json"]
        clients["Clients<br/>Client, ClientSummary, Onboarding,<br/>ClientRecords, Monogram"]
        mailc["Mail<br/>MailThreads, MailRules, Snooze, SendLater,<br/>SavedReplies, ClientMail, NewMail, Outgoing"]
        finance["Finance<br/>Ledger, Projection, Trends,<br/>RecurringExpenses"]
        time["Time<br/>TimeRules, TimeRates, TimeInvoice"]
        tasks["Tasks<br/>TaskGroups, AutomaticTasks"]
        overview["Overview<br/>MorningSummary, OverviewCards"]
        layout["Layout<br/>Sidebar, Panels, ListHints"]
        other["Calendar, Renewals, Retainers,<br/>Search, Tools, Web, Backups, Xero"]
    end
    overview --> documents
    overview --> finance
    overview --> mailc
    tasks --> documents
    clients --> documents
    time --> clients
    mailc --> clients
```

### Desktop: how a page is built

Every page is a `Module` in the sidebar: a view model (`ViewModels/ThingViewModel.cs`) shown by its view (`Views/ThingView.axaml`). View models call Core for decisions and `AppServices` for everything else.

```mermaid
flowchart LR
    window["MainWindow<br/>sidebar, page host, search, notes"]
    main["MainViewModel<br/>modules, timers, shortcuts"]
    page["Page view model<br/>e.g. DocumentsViewModel"]
    view["Page view<br/>e.g. DocumentsView.axaml"]
    services["AppServices<br/>Store, Settings, Mail, Xero,<br/>watches, Go, ShowNote, Work"]
    core["Core rules"]

    window --> main
    main --> page
    view -. binds to .-> page
    page --> core
    page --> services
    services --> store[("LocalStore")]
    services --> xeroS["Xero"]
    services --> mailS["MailService"]
```

| Piece | Job |
| --- | --- |
| `AppServices` | One of each long-lived thing: the store, settings, mail, Xero session, watches, `Navigator`, notes, background work. |
| `AppSettings` | Typed settings saved as JSON under a key in the `settings` table, e.g. `mail-rules`, `mail-snooze`, `income-plan`. |
| `Navigator` (`services.Go`) | Moving between pages and opening a client, a document or a mail, without pages knowing about each other. |
| `Background.Forget` | Runs a task nobody waits for and writes any failure to `AppLog`. |
| `Feedback` | Timings for small movements (fades, the to-do tick), following the Motion setting. |
| Controls | `PanelGrid` (movable panes), `Sticky` (pinned table headings), `ListKeys` (arrow keys on lists), `EmptyState`, `ListHint`, `SavedNote`, `Monogram`, `Sparkline`, `ShareBar`, `IncomeChart`, `MarkupCanvas`. |
| Styles | `Palette.axaml` and three dark palettes (the only place colours live), `Controls.axaml`, `Icons.axaml` (Lucide), `Motion.axaml`. |

### Pages

| Group | Pages |
| --- | --- |
| (top) | Overview, To approve, Mail |
| Client work | Clients, Retainers, Domains and hosting |
| Billing | Quotes, Invoices, Money, Reports |
| Planning | To-do, Calendar, Planner, Time |
| Web tools | Site check, Mark-up, Tools |
| (bottom) | Settings |

### What is stored where

| Table | Holds |
| --- | --- |
| `documents` | Quotes and invoices (`StoredDocument`), with their Xero state. |
| `clients` | Clients with notes, files, projects, onboarding, hourly rates. |
| `tasks`, `events` | To-dos and calendar events. |
| `entries`, `recurring` | Money in and out, and monthly costs. |
| `retainers`, `drafts_made` | Retainers and which monthly drafts were already made. |
| `renewals` | Domains and hosting renewals. |
| `time_entries` | Logged time. |
| `mail_accounts`, `mail_folders`, `mail_messages`, `mail_bodies` | The copy of the mail on this PC. |
| `settings` | Every `AppSettings` record, by key. |

Every row's payload is JSON encrypted by the `Vault` with a key tied to the row, so a row can't be copied into another. Only what the database needs to find rows (ids, kinds, dates) is in the clear.

## 4. How the main jobs flow

### A quote or invoice goes to Xero

```mermaid
sequenceDiagram
    actor Eddie
    participant Editor as DocumentEditorViewModel
    participant Core as Core (Validator, Defaults)
    participant Drafts as XeroDrafts
    participant Xero
    participant Store as LocalStore

    Eddie->>Editor: Create draft in Xero
    Editor->>Core: check the document
    Core-->>Editor: problems or "ready"
    Editor->>Drafts: CreateAsync
    Drafts->>Xero: POST as DRAFT (never approved here)
    Xero-->>Drafts: number and id
    Drafts->>Store: save with XeroNumber, InXero
    Editor-->>Eddie: "Draft in Xero" status
```

### An invoice is emailed

```mermaid
sequenceDiagram
    actor Eddie
    participant Editor as DocumentEditorViewModel
    participant Sending as XeroSending
    participant Xero
    participant Mail as Mail page (ComposeViewModel)
    participant Service as MailService

    Eddie->>Editor: Email it
    Editor->>Sending: PrepareAsync
    Sending->>Xero: PDF and contact email
    Editor->>Mail: open the filled-in mail with the PDF
    Eddie->>Mail: Send
    Mail->>Mail: wait 10 seconds (Undo)
    Mail->>Service: SendAsync (SMTP, copy to Sent)
    Mail->>Sending: MarkSentAsync(who it went to)
    Sending->>Xero: invoice marked sent, quote marked SENT
```

### Mail arrives

```mermaid
sequenceDiagram
    participant Timer as MainViewModel timer
    participant Mail as MailViewModel
    participant Service as MailService
    participant Server as Mail server
    participant Core as Core (SendLater, Snooze, MailRules, NewMail)

    Timer->>Mail: SyncAll (every few minutes)
    Mail->>Service: SyncAccountAsync per mailbox
    Service->>Server: IMAP: new mail, flags
    Service-->>Mail: copy on this PC updated
    Mail->>Core: what is due to send later
    Mail->>Core: snoozed mail whose time has come
    Mail->>Core: rules for mail that just arrived
    Mail->>Service: send, move back, mark, move (as decided)
    Timer->>Core: NewMail.Find
    Timer-->>Timer: note and taskbar flash for new mail
```

### An assistant prepares something

```mermaid
sequenceDiagram
    actor Eddie
    participant AI as Claude Desktop
    participant Conn as Webignite.Connector
    participant Rules as AssistantDocuments / AssistantTasks / AssistantTools
    participant Store as LocalStore
    participant App as Webignite (To approve)

    Eddie->>AI: "Draft an invoice for Karoo"
    AI->>Conn: MCP tool call
    Conn->>Rules: check what is allowed
    Rules->>Store: save as a draft on this PC
    App->>Store: next refresh
    App-->>Eddie: waits under To approve
    Eddie->>App: checks it and approves it
```

### Payments and sites are checked in the background

`MainViewModel` ticks every minute and asks each watch whether it is `Due(now)`. `PaymentWatch` reads open invoices from Xero and `Receivables.Apply` decides what changed; `UptimeWatch` visits watched sites and `Uptime` decides what to say. Findings come up as notes in the corner and lines on Overview.

## Where new code goes

| Adding | Put it in |
| --- | --- |
| A rule, calculation or decision | `Webignite.Core/<Area>`, with a test in `tests/Webignite.Tests` |
| A new kind of record that is saved | Core record + a table and `LocalStore.Thing.cs` in `Webignite.Data` |
| A setting | A record in Core, a property on `AppSettings` |
| A page | View model, view, icon, `Page` value, `Module` in `MainViewModel` |
| A background check | A class in `Webignite.Services` with `Due` and `RunAsync` |
| Something an assistant can do | A tool in `Webignite.Connector`, its rule in `Assistant*` |
| A colour | All four palette files |
