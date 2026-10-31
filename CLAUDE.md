# Webignite Desktop

Native Windows app (C#, .NET 10, Avalonia 12, MVVM with CommunityToolkit.Mvvm). Chromium, Electron, Tauri and WebView2 are off the table: the owner wants no browser engine in the app.

- Build: `dotnet build Webignite.slnx`. Test: `dotnet test --solution Webignite.slnx` (Microsoft.Testing.Platform, xUnit v3).
- Screenshots of the UI: `dotnet run --project tools/Snapshot -- <folder>`. Use this to check UI changes; it runs on Linux.
- Package versions are central in `Directory.Packages.props`. Warnings are errors.
- Business rules belong in `Webignite.Core` with tests, never in view models.
- Documents use snake_case JSON (see `DocumentJson`), the same shape assistants send.
- Webignite is not VAT registered. Xero writes are DRAFT only. Assistants never approve or send.
- UI copy: plain South African English, Rand shown as `R 1 234.56`, no em dashes.
- Colours come only from `Styles/Palette.axaml`. Red and green mean problem and done, nothing else.
