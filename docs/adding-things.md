# Adding things to Webignite

The recipes for the changes that come up most. Each one names the files to touch and the one that must not be skipped.

## A rule or a calculation

Put it in `src/Webignite.Core`, in the folder for its area (`Documents`, `Finance`, `Clients`, `Mail`, `Web`, `Tools`), as a static method on a small static class or a method on the record it belongs to. Write the test first in `tests/Webignite.Tests`, one file per area (`FinanceTests.cs`, `TimeTests.cs`). Core has no packages and no network, so a rule is a pure function of its inputs and a `DateOnly` or `DateTimeOffset` passed in, never `DateTime.Now`.

View models call the rule and show the result. If a view model starts to contain an `if` about money, dates or statuses, that `if` belongs in Core.

## A page in the sidebar

1. View model: `src/Webignite.Desktop/ViewModels/ThingViewModel.cs`, `partial class ThingViewModel : ViewModelBase`, taking `AppServices` in its constructor. Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm. Give it a `Refresh()` if it shows data other pages change.
2. View: `src/Webignite.Desktop/Views/ThingView.axaml` with `x:DataType="vm:ThingViewModel"`. Bindings are compiled, so a typo fails the build. Use the existing classes (`h1`, `h2`, `card`, `muted`, `quiet`, `link`) and `EmptyState` for the empty case. Never a literal colour: everything comes from `Styles/Palette.axaml` through `DynamicResource`.
3. Icon: a Lucide path in `Styles/Icons.axaml`, keyed `Icon.Thing`.
4. Sidebar: one `new Module("Thing", Thing, "Thing")` in the `nav` array in `MainViewModel`, under the right heading. Add a `case ThingViewModel thing: thing.Refresh(); break;` in `OnSelectedChanged` if it has a `Refresh()`.
5. Screenshot: a `Save("thing")` block in `tools/Snapshot/Program.cs`, then `dotnet run --project tools/Snapshot -- shots` and look at the PNG. Never await an async command inside the snapshot tool; the headless dispatcher isn't pumped.

Pages that fill the window with their own panes (Mail) pass `fullBleed: true`.

## A setting

Settings are records saved as JSON under a name. Add a property to an existing settings record in Core (`DocumentSettings`, `TaskSettings`, `BackupSettings`, `AppearanceSettings`) or make a new record, then expose it on `AppServices`:

```csharp
public ThingSettings ThingSettings
{
    get => Store.GetSetting<ThingSettings>("thing") ?? new ThingSettings();
    set => Store.SetSetting("thing", value);
}
```

Records are `sealed record` types with `init` properties and sensible defaults, so an older database without the new field reads as the default. Removing or renaming a property is safe too: `StoredJson` skips what it doesn't know. The Settings page (`SettingsViewModel` and `SettingsView.axaml`) is one card per area; add a card or a row to the right one.

## A table in the database

Records are JSON blobs encrypted by the `Vault`; only what the database needs to index is in the clear (ids, kinds, statuses, dates). So:

- A new field on an existing record: add the property to the record. Nothing else.
- A new kind of record: add a `CREATE TABLE IF NOT EXISTS` to the block at the top of `LocalStore`, a `LocalStore.Thing.cs` partial file with `Save`, `Get`, `List` and `Delete` using `Seal("thing:" + id, value)` and `Open<Thing>("thing:" + id, payload)`, and tests in `StoreTests.cs` or a new file. The context string ties the ciphertext to its row, so use the same prefix everywhere for that table.
- Changing a table that already has data (a new indexed column, a split): bump `LocalStore.SchemaVersion` and add the step to `Upgrade()`. It runs once, inside a transaction, on first open.

Backups copy the whole database, so a new table is in the next backup without any change to `Backups`.

## A check that runs in the background

The pattern is in `PaymentWatch`, `UptimeWatch` and `CheckMailAsync`: a `DispatcherTimer` in `MainViewModel` that ticks every minute and asks a `Due(settings, lastRun, now)` rule in Core whether it is time. The work itself is an `async Task` that returns what it found; announcing goes through `services.ShowNote`. Start it with `.Forget("Checking things")`, never `_ =`, so a failure lands in the log. Show it in the sidebar with `services.Work.Run("Checking things", ...)` so the spinner appears.

## An assistant tool

Tools live in `src/Webignite.Connector` as `[McpServerTool]` methods on a `[McpServerToolType]` class, calling into `AssistantDocuments`, `AssistantTasks` (Data) or `AssistantTools` (Mail). The rule for what an assistant may do is in those three classes, with tests (`AssistantMailTests`, `AssistantTasksTests`), not in the tool method. Assistants prepare and read; they never approve, send or delete. Register a new tool class with `.WithTools<ThingTools>()` in `Program.cs`. Nothing may be written to stdout; it is the transport.

Input from an assistant is parsed with `DocumentJson` (strict, unknown fields refused). Output back to it is written with the same options.

## A colour, a style, an animation

Colours: `Styles/Palette.axaml` for light, the three `Palette.Dark.*.axaml` files for dark, the same keys in all four. Styles: `Styles/Controls.axaml`. Motion: `Styles/Motion.axaml`, switched by the `motion-full` and `motion-reduced` classes on the window, and `FadeRise` in `Motion.cs` for page transitions. Red means a problem, green means done, nothing else uses them.

## A package

Versions are central in `Directory.Packages.props`; a project's `.csproj` names the package with no version. Dependabot opens a PR when a newer version is out. No browser engine of any kind (Chromium, WebView2, Electron, Tauri) may be added.

## Before a pull request

```
dotnet build Webignite.slnx
dotnet test --solution Webignite.slnx
dotnet run --project tools/Snapshot -- shots
```

Warnings are errors, so the build is the lint. Look at the screenshots for any screen you touched. One PR per feature, branched from `main`.
