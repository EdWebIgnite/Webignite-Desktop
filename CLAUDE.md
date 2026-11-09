# Webignite Desktop

Native Windows app (C#, .NET 10, Avalonia 12, MVVM with CommunityToolkit.Mvvm). Chromium, Electron, Tauri and WebView2 are off the table: the owner wants no browser engine in the app.

- Build: `dotnet build Webignite.slnx`. Test: `dotnet test --solution Webignite.slnx` (Microsoft.Testing.Platform, xUnit v3).
- Screenshots of the UI: `dotnet run --project tools/Snapshot -- <folder>`. Use this to check UI changes; it runs on Linux.
- Package versions are central in `Directory.Packages.props`. Warnings are errors.
- Business rules belong in `Webignite.Core` with tests, never in view models.
- Documents use snake_case JSON (see `DocumentJson`), the same shape assistants send.
- Webignite is not VAT registered. Xero writes are DRAFT (adding a missing contact, or an item to Products and Services by name only, along the way), except approving an invoice and marking it or a quote sent, which happen only when the person clicks the email button on the quote or invoice (Approve and email, Email the invoice or Email the quote, or Email it on the Quotes and Invoices right-click menu), and marking a sent quote accepted or declined, which happens only when the person clicks Client accepted or Client declined. Claude and ChatGPT only ever draft: they never approve or send.
- UI copy: plain South African English, Rand shown as `R 1 234.56`, no em dashes.
- Don't pin a country or culture: dates on screen follow the PC's Windows region, as users may be outside South Africa later. Keys that are stored, and text sent to clients or Xero, use `CultureInfo.InvariantCulture`.
- Colours come only from `Styles/Palette.axaml`. Red and green mean problem and done, nothing else.
- Recipes for adding a page, setting, table, background check or assistant tool: `docs/adding-things.md`.
- Map of the projects, pages, stores and main flows: `docs/architecture.md`. Update it in the same PR when one of those changes.
- Light by design: pages are built when first opened and let go when unused (`PageHost`, `IHoldsWork`, `IDisposable`), and a page can be open more than once, so no page keeps state anywhere shared; background work runs in `BackgroundJobs` without needing a page; heavy libraries live in `Webignite.Tools` and are never touched at start-up. Closing something must free what it held (timers, pictures, watchers).
- Dropped tasks go through `Background.Forget("What it was doing")`, never `_ =` and never `async void`; add `tell: true` when the person started it, so a failure shows a note. Problems go to `AppLog`. An error nobody caught on the screen's thread is logged and shown by `Background.CatchUnhandled`, and the app carries on.
- Pages read and change quotes, invoices and clients through `services.Documents` and `services.Clients` (in `Webignite.Services`), never on the store directly. A change is applied to the record as stored now (`Documents.Change`, `Clients.Change`, `SaveDraft`, `SaveDetails`); never save an older copy back with `Save(old with { ... })`, which undoes whatever changed meanwhile, such as a payment. Mail the person sends is prepared through `services.Drafts`. Editors call `Keep()` before they are replaced, and anything open is saved in `MainViewModel.PrepareToCloseAsync` before the app closes.
