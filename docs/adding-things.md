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
| picking files, a folder or where to save | `Picker.Files`, `Picker.Folder`, `Picker.Save` (the app's own explorer, never Windows' boxes); a folder to show is `services.Go.Folder` or `Go.ShowFile`, never Explorer |
| reading an amount someone typed ("1,250", "R 1 250.50") | `Money.Parse` |
| formatted writing (a mail, a note): lists, checklists, tables, pictures | `RichDocument`, changed only through `RichEditing`, stored as HTML with `RichHtml` |
| how a quote or invoice was settled, Xero's statuses | `Settlement`, `Settlements`, `XeroStatuses` |
| which mailbox sends invoices or everyday mail | `MailAccounts.ForInvoices` and `ForEveryday` |

Text that is stored, or sent to a client or Xero, is formatted with `CultureInfo.InvariantCulture`. Dates shown on screen in the Desktop project follow the Windows region; don't pin a culture.

## A page in the sidebar

1. View model: `src/Webignite.Desktop/ViewModels/ThingViewModel.cs`, `partial class ThingViewModel : ViewModelBase`, taking `AppServices` in its constructor. Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm. If it shows data other pages change, implement `IRefreshable`: its `Refresh()` runs whenever the page is opened and when F5 is pressed.
2. View: `src/Webignite.Desktop/Views/ThingView.axaml` with `x:DataType="vm:ThingViewModel"`. Bindings are compiled, so a typo fails the build. Use the existing classes (`h1`, `h2`, `card`, `muted`, `quiet`, `link`) and `EmptyState` for the empty case. Never a literal colour: everything comes from `Styles/Palette.axaml` through `DynamicResource`.
3. Icon: a Lucide path in `Styles/Icons.axaml`, keyed `Icon.Thing`.
4. Name: add `Thing` to the `Page` enum in `Navigator.cs`. Pages are known by this key, never by their title, so the title can change without breaking the jumps to it.
5. Sidebar: one `new Module(Page.Thing, "Thing", () => new ThingViewModel(services), "Thing")` in the `nav` array in `MainViewModel`, under the right heading. The page is built the first time it is opened and let go again after a while unused, so: if it can have something open or typed in it, implement `IHoldsWork` so it is kept while it does; if it starts a timer or subscribes to anything on `AppServices`, implement `IDisposable` and undo that there; and never reach for a page from start-up or from a background job (`Built<T>` in `MainViewModel` finds only the copies that are built). A page can be open more than once, so it keeps its state in itself, never in anything static or on `AppServices`. If what is open in it says more than the page's name (a client, an invoice), implement `ITabTitle` so its tab says so. A page can also be shown in a popped-out window, so its view finds its window with `TopLevel.GetTopLevel(this)`, never by assuming `MainWindow`. `OnSelectedChanged` refreshes any page that is `IRefreshable`.
8. Heavy libraries (PDF, imaging, recording) go in `src/Webignite.Tools`, called from code that only runs when the tool is used, so they are not read from disk at start-up. The screenshot tool fails if they are.
6. Jumping there from another page: `services.Go.To(Page.Thing)`. If other pages need to open a particular thing, add a method to `Navigator` (as `Go.Client(client)` and `Go.Document(stored)` do) rather than a callback on `AppServices`.
7. Screenshot: a `Save("thing")` block in `tools/Snapshot/Program.cs`, then `dotnet run --project tools/Snapshot -- shots` and look at the PNG. For work that finishes off the screen's thread use `Wait(task, "What it was doing")`, never `await` (the headless dispatcher isn't pumped) and never `_ =`. Anything worth checking goes through `Check("What should be true", condition)`: a failed check fails the run, here and in CI, which draws every screen on each pull request and keeps the pictures with the run.

A page that belongs with projects and planning is marked `InPlanner = true`: it then shows in the Planner sidebar instead of the main one. Pages use the whole width of the window, however big the screen; only forms meant for reading and filling in pass `readingWidth: true` (Settings) to keep a comfortable width. Ordinary pages sit in a scroller held by `FitWidth`, so they are measured at the window's width and only scroll sideways below their least width. Pages that fill the window with their own panes (Mail, Calendar) pass `fullBleed: true`. Such a page sits in a sideways scroller that offers it endless width, so star columns, trimmed text and wrapping lists won't fit the window by themselves; so derive the view from `FullBleedView` (as `FilesView` and `PdfView` do) to keep to the width on screen. The sidebar's order, folded groups, hidden state and what has been taken off the menus are the person's (`Core.Layout.Sidebar`, saved under "sidebar"); a page must still open when it is off the menus, as search and links reach it; a new page slots in after the one it follows in the `nav` array, whatever order the person has.

## A small tool in its own window

Small tools (the calculator, QR codes) aren't pages. Add a value to `ToolKind` (`ViewModels/ToolLink.cs`), its state and commands to a `ToolsViewModel` partial, a view of its own (`ThingView.axaml`, `x:DataType="vm:ToolsViewModel"`), a case in `ToolWindow` for its view, width and title, and a `new ToolLink(ToolKind.Thing, "Thing", "Icon")` in the `nav` array under Computer or Web tools. The window is built when clicked and let go when closed; Measure checks that it is.

A tool that needs a web page drawn (and only such a tool) asks `services.NewBrowser()` for an `IWebEngine` once its view is in its window, shows the engine's `View`, and disposes it when the view leaves the window, as `PageCaptureView` and `ToolsViewModel.Capture.cs` do. Never keep one in a field on `AppServices` or start one before the window is open. `NewBrowser()` gives null where there is no WebView2 to use, and the tool then says so. What it asks of the browser's developer tools goes in `Core/Web` as plain JSON with tests (`PageCapture`), so it can be tried without a browser; add the tool to `ToolWindow.Fills` so its window is big and stands on its own, and give the screenshot tool's `StandInBrowser` the answers it needs.

## Panes the person can drag, move and hide

With `Panels` bound, each divider has a small minimise handle for the pane to its left, a minimised pane leaves a slim strip with its name where it was (a click brings it back), and the page's minimised panes show as chips under its name in the sidebar (`MainViewModel.HiddenPanes`) to bring them back too. The divider's right-click menu hides either pane or puts them back. A pane's own minimise button binds to `Panels[key].MinimiseCommand`; a menu tick binds `IsShown` with `Mode=TwoWay` (a menu item's tick is one-way otherwise, so nothing would happen). Make the page's view model `IHasPanes` so Ctrl+Shift+1 to 4 show and hide its panes. Mail, Clients, Notes and Files are built this way.

Mail is the example. The rules (show, hide but never the last one, move, resize, match a saved layout to the panes the page has now) are in `Core.Layout.Panels`, with tests in `PanelsTests.cs`.

1. In the view model: `Panels = new PanelsViewModel(services.Settings, "thing", [("list", "List"), ("detail", "Detail")])`. The first string is the name the layout is saved under. Any pane can be hidden as long as one stays; a hidden pane leaves a strip with its name to bring it back.
2. In the view: a `<c:PanelGrid Layout="{Binding Panels.Layout}" Panels="{Binding Panels}" Resized="PaneResized">` with each pane as a child carrying `c:PanelGrid.Pane="list"` and `c:PanelGrid.DefaultWidth="380"`. The last visible pane takes what is left. The code-behind passes the drag on: `void PaneResized(string pane, double width) => (DataContext as ThingViewModel)?.Panels.Resized(pane, width);`.
3. A panes button with a `MenuFlyout`: one `MenuItem` per pane (`DataContext="{Binding Panels[list]}"`) holding a "Show" item with `ToggleType="CheckBox"` bound to `IsShown`, and "Move left" and "Move right" bound to `MoveLeftCommand` and `MoveRightCommand`; then "Put the panes back" bound to `Panels.ResetCommand`. Copy the one in `ClientsView.axaml`.

## A drop-down menu made as it opens

Use `c:FreshMenu` with `Filling="…"`, not a `MenuFlyout` filled in `Opening`: a MenuFlyout filled while opening opens as a thin empty line on Windows. A FreshMenu with nothing in it doesn't open at all.

## Rows the person can drag into order

Attach `DragReorder` to the items control with `DragRules`: what can be picked up, where it can land (above or below another item, or into one with `IsInto`), what dropping does (a view model method that changes the record as stored now), and the words on the card. Put the ordering rule in Core with tests, as `Sidebar.MoveTo` and `ProjectLooks.PlaceAt` are. For a list box, set `Click` so a press only picks the row when it wasn't a drag. To take things dragged from another list, add a `DropZone`.

## Folding rows on a page

To keep a page calm, put its extra blocks in one card as folding rows rather than a stack of cards (the client page is the example). In the view: `<c:FoldList Sections="{Binding Sections}">` with a `<c:FoldSection Key="mail" Title="Mail" Icon="{StaticResource Icon.Mail}" Summary="{Binding MailSummary}">` per block. In the view model: `Sections = new SectionsViewModel(services.Settings, "page-name", keys, openAtFirst)` and a short summary string for each row, raised when what it sums up changes. Order and what's open are saved per page.

## A widget for My pages

1. Add the kind to `WidgetKind` in `Core/Layout/MyPages.cs`, with its `Label`, `StartSize` and, if it needs more room to be useful, `LeastSize`. If it shows one project or client, add it to `NeedsTarget`; the Add widget menu then asks which. Test the sizes in `MyPagesTests`.
2. Write its body in `ViewModels/Widgets.cs`: a `WidgetBody` that reads what it needs when built and in `Refresh`, keeps anything typed in `Keep`, and lets go of what it follows in `Dispose`. Map the kind to it in `WidgetBody.For`.
3. Give the body a `DataTemplate` in `MyPagesView.axaml`. Keep it to what fits a small card; the full page stays a click away.
4. Add it to the My pages part of the screenshot tool.

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
- Quotes, invoices and clients are read and changed by pages through `services.Documents` and `services.Clients` (`DocumentService`, `ClientService` in `Webignite.Services`, with tests in `ServiceLayerTests.cs`), never on the store directly. A change is applied to the record as stored now: `Documents.Change(id, d => d with { ... })`, `Documents.SaveDraft`, `Clients.Change(id, c => ...)`, `Clients.SaveDetails`. Never `Save(old with { ... })`: it undoes whatever changed meanwhile, such as a payment. Other kinds of record are still read and saved on the store; give them a service the same way if two places start changing them.
- Changing a table that already has data (a new indexed column, a split, fields moved inside a record): bump `LocalStore.SchemaVersion` and add the step to `Upgrade()`, with the step itself in `LocalStore.Upgrades.cs`. It runs once, inside a transaction, on first open, and again on a restored older backup. A database from a newer version stops with `NewerDataException` instead of being misread. Test it the way `DocumentUpgradeTests` does: write the old shape, open, read it back.

Backups copy the whole database, so a new table is in the next backup without any change to `Backups`.

## A check that runs in the background

The pattern is `PaymentWatch` and `UptimeWatch` in `src/Webignite.Services`: a class that takes what it needs in its constructor (the store, a way to Xero, a settings getter, the network call itself) so a test can hand it fakes, a `Due(now)` that says whether it is time, and a `RunAsync` that returns what it found. `AppServices` owns one of each. `BackgroundJobs` ticks once a minute, asks `Due`, and runs the job `MainViewModel` hands it; the job tells only pages that are built (`IfBuilt`), and announcing goes through `services.ShowNote`. Tests go in `ServicesTests.cs` against a fake `HttpMessageHandler` and a fixed clock. Start it with `.Forget("Checking things")`, never `_ =`, so a failure lands in the log. Show it in the sidebar with `services.Work.Run("Checking things", ...)` so the spinner appears.

## An assistant tool

Tools live in `src/Webignite.Connector` as `[McpServerTool]` methods on a `[McpServerToolType]` class, calling into `AssistantDocuments`, `AssistantTasks` (Data), `AssistantTools` (Mail), or `AssistantPlanner` and `AssistantBusiness` (Services). The rule for what an assistant may do is in those classes, with tests (`AssistantMailTests`, `AssistantTasksTests`, `AssistantPlannerTests`, `AssistantBusinessTests`), not in the tool method. Assistants read, create and change as Eddie would, and tick things off; they never approve, send or delete. Things are found by id or name (`AssistantLookup` in Core: an exact name, or a part that only one matches) and problems go back as an `ArgumentException` in plain words, which the tool turns into an `McpException`. A write tool goes through the tool class's `Write(server, what, ...)`, which acts as the assistant asking and adds `what` to `AssistantLog`; a rule that replaces writing keeps the old value with a `Field` that `AssistantPutBack` knows, checks sizes with `AssistantLimits` and refuses to empty writing. A rule that changes something calls `AssistantSignal.Touch(store, "area")` (projects, todos, clients, contacts, notes, calendar, time, renewals) so the open app refreshes those pages; `MainViewModel.ShowAssistantChanges` says which pages each area means. Register a new tool class with `.WithTools<ThingTools>()` in `Program.cs`. Nothing may be written to stdout; it is the transport.

Tool parameters are typed and named in snake_case, as the assistant sees them. What goes back to it is written with `DocumentJson.Options`, the same snake_case shape.

## A colour, a style, an animation

Colours: `Styles/Palette.axaml` for light, the three `Palette.Dark.*.axaml` files for dark, the same keys in all four (shadows and the mark-up pens included). Styles: `Styles/Controls.axaml` for anything used on more than one page; a table heading is a `Border Classes="stickyhead"` with only its own Padding, as the class pins it and gives it the heading colour and height; styles only one page uses can stay in that view. Avalonia gives a later style the say over an earlier one, so a style that must win (a heading that never lights up) goes after the general one. Motion: `Styles/Motion.axaml`, switched by the `motion-full` and `motion-reduced` classes on the window, and `FadeRise` in `Motion.cs` for page transitions. Red means a problem, green means done, nothing else uses them. A style (Settings, Style) changes shape, never colour: its files are in `Styles/Looks/{style}/`, each a list of setters laid over the app's own (`Controls.axaml`) or over one page's (`Mail.axaml`, after the page calls `Looks.Attach(this, "Mail")` in its constructor). Every selector in them starts with `Window.look-airy ` (the style's name), which is what switches the rule off again when another style is picked; a rule without it would stay on in Classic. Small text is sized by name, never by number: `FontSize="{DynamicResource Text12}"` (the names are in `Styles/Sizes.axaml`, for 10 to 13 and the halves between), so a style can make it bigger; body text takes the window's size and headings their class. Don't put a named size on an element whose class also sets one (`label`): the class wins, so give it a class of its own instead, as `label caps` is. A style can only change what a style sets: a size or margin typed on the element itself always wins, so to let styles change it, move it into a class in the page's own styles with the same value (as `Border.folderpane` and `StackPanel.listhead` in `MailView.axaml`), which leaves Classic as it was. Classic must not change: compare the screenshots before and after. The screenshot tool's `SaveLooks("name")` draws a screen in every style for comparing. To go through a whole style, `WEBIGNITE_SNAPSHOT_STYLE=Airy WEBIGNITE_SNAPSHOT_SIDEBAR=Light dotnet run --project tools/Snapshot -- shots-airy` draws every screen in it. The colours offered for text and highlights in a mail are part of what is sent, so they are in `Core/Writing/MailColours`, not the palette; the editor's own paper colours (`PaperText`, `PaperLine` and so on) are in the palette and stay the same in every look.

## A mail the person sends

Anything that prepares a mail for the person to read and send (an invoice, a reminder, a statement, a mark-up) goes through `services.Drafts`: pick `InvoiceMailbox` or `EverydayMailbox`, save attachments with `AttachAsync`, and `Open(new MailDraft(...))` with what should happen after it is sent. Nothing is ever sent from code; the person presses Send. Give it plain text; the mail editor adds the mailbox's signature and the person can format it before sending. Formatting that should arrive on its own goes in as the light marks (**bold**, "- " lists) that `RichText.FromText` reads.

## Before the app closes

`MainViewModel.PrepareToCloseAsync` saves whatever is open (quotes and invoices, the open client, the mail being written, typed amounts) and sends a mail still in its undo wait. Something new that holds unsaved work gets a line there.

## A package

Versions are central in `Directory.Packages.props`; a project's `.csproj` names the package with no version. Dependabot opens a PR when a newer version is out. No browser engine (Chromium, Electron, Tauri) may be added. The one exception is WebView2, the copy Windows already has, and only inside the web tools on the terms in [roadmap.md](roadmap.md): started when such a tool opens, shut down when it closes, never at start-up.

## Before a pull request

```
dotnet build Webignite.slnx
dotnet test --solution Webignite.slnx
dotnet run --project tools/Snapshot -- shots
dotnet run --project tools/Measure -c Release
```

Measure fails when start-up time or memory passes `tools/Measure/limits.json`, or when a page or its screen is still in memory after it is let go. A screen that puts a handler on the window (or anything else that outlives it) keeps the window holding the screen; remember what it was put on and take it off when the screen is taken off screen, as `MarkupView` does. A new tool that opens and closes (a viewer, the file explorer) gets the same open-and-let-go check there. Raise a limit only on purpose, in the PR that needs it, saying why.

Warnings are errors and the .NET code analysers run at "recommended", so the build is the lint. A rule that doesn't fit is switched off in `.editorconfig` with the reason, or for one line with a `#pragma` and a comment saying why. Look at the screenshots for any screen you touched. One PR per feature, branched from `main`.
