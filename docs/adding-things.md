# Adding things to Webignite

The recipes for the changes that come up most. Each one names the files to touch and the one that must not be skipped. For how the pieces fit together, see [architecture.md](architecture.md).

## A rule or a calculation

Put it in `src/Webignite.Core`, in the folder for its area (`Documents`, `Finance`, `Clients`, `Mail`, `Web`, `Tools`), as a static method on a small static class or a method on the record it belongs to. Write the test first in `tests/Webignite.Tests`, one file per area (`FinanceTests.cs`, `TimeTests.cs`). Core has no packages and no network, so a rule is a pure function of its inputs and a `DateOnly` or `DateTimeOffset` passed in, never `DateTime.Now`.

View models call the rule and show the result. If a view model starts to contain an `if` about money, dates or statuses, that `if` belongs in Core.

Before writing a new helper, use the one that is there. Each of these is the only copy, and a second one would drift:

| For | Use |
|---|---|
| "1 day", "yesterday", "5 days ago", "Friday", "the 22nd" | `Wording` |
| week start, month start, the tax year | `Dates` |
| something every month (a day in the month, months due, pausing) | `MonthlySchedule` |
| comparing or finding a client by name | `ClientDirectory.Key` and `Find` |
| whose email address this is, a client's first address | `ClientMail.For`, `IsOwn` and `First` |
| a website or address turned into its domain | `Web.Domains.Of` |
| reading an amount someone typed ("1,250", "R 1 250.50") | `Money.Parse` |
| how a quote or invoice was settled, Xero's statuses | `Settlement`, `Settlements`, `XeroStatuses` |
| which mailbox sends invoices or everyday mail | `MailAccounts.ForInvoices` and `ForEveryday` |

Text that is stored, or sent to a client or Xero, is formatted with `CultureInfo.InvariantCulture`. Dates shown on screen in the Desktop project follow the Windows region; don't pin a culture.

## A page in the sidebar

1. View model: `src/Webignite.Desktop/ViewModels/ThingViewModel.cs`, `partial class ThingViewModel : ViewModelBase`, taking `AppServices` in its constructor. Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm. If it shows data other pages change, implement `IRefreshable`: its `Refresh()` runs whenever the page is opened and when F5 is pressed.
2. View: `src/Webignite.Desktop/Views/ThingView.axaml` with `x:DataType="vm:ThingViewModel"`. Bindings are compiled, so a typo fails the build. Use the existing classes (`h1`, `h2`, `card`, `muted`, `quiet`, `link`) and `EmptyState` for the empty case. Never a literal colour: everything comes from `Styles/Palette.axaml` through `DynamicResource`.
3. Icon: a Lucide path in `Styles/Icons.axaml`, keyed `Icon.Thing`.
4. Name: add `Thing` to the `Page` enum in `Navigator.cs`. Pages are known by this key, never by their title, so the title can change without breaking the jumps to it.
5. Sidebar: one `new Module(Page.Thing, "Thing", Thing, "Thing")` in the `nav` array in `MainViewModel`, under the right heading. Nothing else: `OnSelectedChanged` refreshes any page that is `IRefreshable`.
6. Jumping there from another page: `services.Go.To(Page.Thing)`. If other pages need to open a particular thing, add a method to `Navigator` (as `Go.Client(client)` and `Go.Document(stored)` do) rather than a callback on `AppServices`.
7. Screenshot: a `Save("thing")` block in `tools/Snapshot/Program.cs`, then `dotnet run --project tools/Snapshot -- shots` and look at the PNG. For work that finishes off the screen's thread use `Wait(task, "What it was doing")`, never `await` (the headless dispatcher isn't pumped) and never `_ =`. Anything worth checking goes through `Check("What should be true", condition)`: a failed check fails the run, here and in CI, which draws every screen on each pull request and keeps the pictures with the run.

Pages that fill the window with their own panes (Mail) pass `fullBleed: true`. The sidebar's order, folded groups and hidden state are the person's (`Core.Layout.Sidebar`, saved under "sidebar"); a new page slots in after the one it follows in the `nav` array, whatever order the person has.

## Panes the person can drag, move and hide

Mail is the example. The rules (show, hide but never the last one, move, resize, match a saved layout to the panes the page has now) are in `Core.Layout.Panels`, with tests in `PanelsTests.cs`.

1. In the view model: `Panels = new PanelsViewModel(services.Settings, "thing", [("list", "List"), ("detail", "Detail")], spine: "list")`. The first string is the name the layout is saved under; `spine` is a pane that can never be hidden.
2. In the view: a `<c:PanelGrid Layout="{Binding Panels.Layout}" Resized="PaneResized">` with each pane as a child carrying `c:PanelGrid.Pane="list"` and `c:PanelGrid.DefaultWidth="380"`. The last visible pane takes what is left. The code-behind passes the drag on: `void PaneResized(string pane, double width) => (DataContext as ThingViewModel)?.Panels.Resized(pane, width);`.
3. A panes button with a `MenuFlyout`: one `MenuItem` per pane (`DataContext="{Binding Panels[list]}"`) holding a "Show" item with `ToggleType="CheckBox"` bound to `IsShown`, and "Move left" and "Move right" bound to `MoveLeftCommand` and `MoveRightCommand`; then "Put the panes back" bound to `Panels.ResetCommand`. The spine pane has no "Show", as it can't be hidden. Copy the one in `ClientsView.axaml`.

## A setting

Settings are records saved as JSON under a name. Add a property to an existing settings record (`DocumentSettings`, `TaskSettings` and `BackupSettings` are in Core; `AppearanceSettings`, which is only about the app, is in `Desktop/Motion.cs`) or make a new record, then expose it on `AppSettings` (reached as `services.Settings`):

```csharp
public ThingSettings Thing
{
    get => store.GetSetting<ThingSettings>("thing") ?? new ThingSettings();
    set => store.SetSetting("thing", value);
}
```

Records are `sealed record` types with `init` properties and sensible defaults, so an older database without the new field reads as the default. Removing or renaming a property is safe too: `StoredJson` skips what it doesn't know. The Settings page (`SettingsViewModel` and `SettingsView.axaml`) has a side menu of sections (`SettingsViewModel.Sections`) and one or more cards per section, each card with `IsVisible="{Binding ShowThing}"`. Add a row to the right card, or a new section: an entry in `Sections`, a `ShowThing` flag in `SettingsViewModel.Sections.cs`, and a card bound to it.

## A table in the database

Records are JSON blobs encrypted by the `Vault`; only what the database needs to index is in the clear (ids, kinds, statuses, dates). So:

- A new field on an existing record: add the property to the record. Nothing else.
- A new kind of record: add a `CREATE TABLE IF NOT EXISTS` to the block at the top of `LocalStore`, and a `LocalStore.Thing.cs` partial file built on the helpers in `LocalStore.Rows.cs`: `Put("things", "thing:", id, value)`, `Find<Thing>`, `Remove`, `ReadAll<Thing>` for a list, `ReadKeys`, `Day(date)` for a readable date column, and `InOneGo(() => ...)` when several writes must land together. Tests go in `StoreTests.cs` or a new file. The context string (`"thing:"`) ties the ciphertext to its row, so use the same prefix everywhere for that table.
- Changing a saved quote or invoice: `Store.Update(id, current => current with { ... })`, which applies the change to the document as it is stored now. Never `Save(old with { ... })`: it undoes whatever changed meanwhile, such as a payment.
- Changing a table that already has data (a new indexed column, a split): bump `LocalStore.SchemaVersion` and add the step to `Upgrade()`. It runs once, inside a transaction, on first open.

Backups copy the whole database, so a new table is in the next backup without any change to `Backups`.

## A check that runs in the background

The pattern is `PaymentWatch` and `UptimeWatch` in `src/Webignite.Services`: a class that takes what it needs in its constructor (the store, a way to Xero, a settings getter, the network call itself) so a test can hand it fakes, a `Due(now)` that says whether it is time, and a `RunAsync` that returns what it found. `AppServices` owns one of each. A `DispatcherTimer` in `MainViewModel` ticks every minute, asks `Due`, and runs it; announcing goes through `services.ShowNote`. Tests go in `ServicesTests.cs` against a fake `HttpMessageHandler` and a fixed clock. Start it with `.Forget("Checking things")`, never `_ =`, so a failure lands in the log. Show it in the sidebar with `services.Work.Run("Checking things", ...)` so the spinner appears.

## An assistant tool

Tools live in `src/Webignite.Connector` as `[McpServerTool]` methods on a `[McpServerToolType]` class, calling into `AssistantDocuments`, `AssistantTasks` (Data) or `AssistantTools` (Mail). The rule for what an assistant may do is in those three classes, with tests (`AssistantMailTests`, `AssistantTasksTests`), not in the tool method. Assistants prepare and read; they never approve, send or delete. Register a new tool class with `.WithTools<ThingTools>()` in `Program.cs`. Nothing may be written to stdout; it is the transport.

Tool parameters are typed and named in snake_case, as the assistant sees them. What goes back to it is written with `DocumentJson.Options`, the same snake_case shape.

## A colour, a style, an animation

Colours: `Styles/Palette.axaml` for light, the three `Palette.Dark.*.axaml` files for dark, the same keys in all four (shadows and the mark-up pens included). Styles: `Styles/Controls.axaml` for anything used on more than one page; styles only one page uses can stay in that view. Avalonia gives a later style the say over an earlier one, so a style that must win (a heading that never lights up) goes after the general one. Motion: `Styles/Motion.axaml`, switched by the `motion-full` and `motion-reduced` classes on the window, and `FadeRise` in `Motion.cs` for page transitions. Red means a problem, green means done, nothing else uses them.

## A mail the person sends

Anything that prepares a mail for the person to read and send (an invoice, a reminder, a statement, a mark-up) goes through `services.Drafts`: pick `InvoiceMailbox` or `EverydayMailbox`, save attachments with `AttachAsync`, and `Open(new MailDraft(...))` with what should happen after it is sent. Nothing is ever sent from code; the person presses Send.

## Before the app closes

`MainViewModel.PrepareToCloseAsync` saves whatever is open (quotes and invoices, the open client, the mail being written, typed amounts) and sends a mail still in its undo wait. Something new that holds unsaved work gets a line there.

## A package

Versions are central in `Directory.Packages.props`; a project's `.csproj` names the package with no version. Dependabot opens a PR when a newer version is out. No browser engine of any kind (Chromium, WebView2, Electron, Tauri) may be added.

## Before a pull request

```
dotnet build Webignite.slnx
dotnet test --solution Webignite.slnx
dotnet run --project tools/Snapshot -- shots
```

Warnings are errors and the .NET code analysers run at "recommended", so the build is the lint. A rule that doesn't fit is switched off in `.editorconfig` with the reason, or for one line with a `#pragma` and a comment saying why. Look at the screenshots for any screen you touched. One PR per feature, branched from `main`.
