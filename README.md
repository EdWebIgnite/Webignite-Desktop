# Webignite Desktop

Native Windows software for Webignite's quotes and invoices, built to grow into email and more. It runs on your PC and keeps your data there. No browser engine, no Electron, no server.

![New invoice](docs/screenshot-invoice.png)

## What it does now

- **New quote** and **New invoice** screens. Type a customer and lines and the totals, date and due date fill in as you go. Amounts like `3,5` or `R650` are read correctly.
- Every document goes through the same checks that will apply to documents Claude and ChatGPT prepare: totals must add up to the cent, no VAT (Webignite is not VAT registered), and dates and lines must make sense.
- The Xero draft format and the read-back check are ready, but nothing is connected to Xero yet.

## Coming next

1. Connect Xero (desktop sign-in, drafts only) and save documents locally in an encrypted database.
2. The **To approve** list, plus the connector for Claude Desktop and ChatGPT.
3. Installer with automatic updates and encrypted backups.
4. Mail, with a native HTML viewer, plus the assistant tools to read and draft replies.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet run --project src/Webignite.Desktop        # open the app
dotnet test --solution Webignite.slnx             # run the tests
dotnet run --project tools/Snapshot -- shots       # save screenshots of each screen
```

## Layout

| Project | What it holds |
|---|---|
| `src/Webignite.Core` | Documents, money rules, defaults and checks. No UI, no network. |
| `src/Webignite.Xero` | The Xero draft format and the check that Xero saved what was sent. |
| `src/Webignite.Desktop` | The Avalonia app. Each sidebar entry is a module. |
| `tests/Webignite.Tests` | Tests for Core and Xero. |
| `tools/Snapshot` | Renders screens to PNG without opening a window. |

Colours live in `src/Webignite.Desktop/Styles/Palette.axaml`. Change that one file to try a different palette.

## Ground rules

- Drafts only. The app never sends, approves, emails or records payments in Xero without a person clicking.
- Assistants can prepare documents and replies, but only a person can approve them.
- Money is always `decimal`, rounded half away from zero to the cent.
- Never retry a write to Xero when the outcome is unclear. Check what Xero has first.
