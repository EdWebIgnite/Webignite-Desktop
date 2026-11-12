# What may come later

Tools and ideas that were agreed as worth doing. The first list is built; what is still to come is under Looks and Later. When something is started, take it off this list and add it to [architecture.md](architecture.md) in the same PR.

## The browser rule

The app has no browser engine of its own, to stay small and light on memory. WebView2 is allowed for the web tools below that need a page drawn as a browser draws it, on these terms:

- It uses the WebView2 that Windows already has and keeps up to date. None is shipped with the app. A PC without it is told so, with where to get it, and the rest of the app carries on.
- It starts when one of those tools' windows opens and is shut down when the window closes. The Site crawler's is the one without a window: it starts when a JavaScript crawl starts and is shut down when that crawl ends. While open it costs roughly 200 to 400 MB in its own `msedgewebview2.exe` processes; closed, nothing.
- Nothing at start-up touches it. `WebView2Tests` checks on Windows that no WebView2 process is left after it is closed (`tools/Measure` runs on Linux, where there is no WebView2).
- Mail keeps its own light renderer. Nothing outside the web tools uses WebView2.
- The Browser and phone preview tool is one of the web tools: a window of its own under Web tools, on the same terms. It is for looking at and testing sites, not a replacement for the PC's browser, so links elsewhere in the app still open in that.
- WebView2 is Windows only, so `tools/Snapshot` (which runs on Linux) needs a stand-in, as `IVideoTools` is for video.

How it is hosted (built in step 4): Microsoft's own small wrapper (`Microsoft.Web.WebView2`, only its `Core` library and loader, under 1 MB) drawing in a plain pane of the tool's window. A tool asks `services.NewBrowser()` for an `IWebEngine` and disposes it when its window closes; see Page to PDF or picture in [architecture.md](architecture.md). Avalonia's own `Avalonia.Controls.WebView` was looked at and left: it is free now, but the tools need the browser's developer tools (whole-page pictures, pretending to be a phone), which Microsoft's wrapper gives directly.

## Agreed, and built

Everything on the agreed list is built; the order and where each one lives is below. What was learnt along the way is in [architecture.md](architecture.md).

## Order to build them in

Web first, as that is most of the work; branding last. Each step is its own PR.

1. ~~Small web tools~~ Built: Web helpers, under Web tools.
2. ~~DNS and mail record tool~~ Built: DNS records, under Web tools.
3. ~~Site crawler, reading HTML only~~ Built: Site crawler, under Web tools.
4. ~~WebView2 groundwork, with Save a web page as a PDF and Website screenshots~~ Built: Page to PDF or picture, under Web tools.
5. ~~Responsive preview and Phone simulator~~ Built: Show as, in Page to PDF or picture. The device sizes were written from memory, not looked up, so correct any that are off in `Devices.All`.
6. ~~Site crawler for sites that need JavaScript~~ Built: a tick on the Site crawler page.
7. ~~Screen ruler~~ Built: Screen ruler, under Web tools, and Ctrl+Alt+M from any program.
8. ~~Text snippets from any program~~ Built: Text snippets, under Computer, and Ctrl+Alt+T from any program. A snippet is picked from a small window (by name or code), not by typing its code straight into the other program: that would need a hook on every key typed on the PC, which was left out on purpose.
9. ~~Copy text from a picture~~ Built: Text from the screen, under Computer, and Ctrl+Alt+X from any program.
10. ~~Compare, then Duplicate file finder~~ Built: Compare and Duplicate files, under Computer and on Files' right-click.
11. ~~Palette builder~~ Built: the Palette and Contrast parts of the Colours tool.
12. ~~Favicon and app icon maker, then Logo export pack~~ Built: Logo pack and favicons, under Web tools.
13. ~~Social image sizer~~ Built: Social images, under Web tools.
14. ~~Brand sheet maker~~ Built: the Brand sheet card in Logos and brand sheet, using the palette from Colours.

## Looks

The app is getting more than one style to try side by side, so we can see what works. Colours and style are separate choices (Settings, Style), and Classic stays as it is.

- **Airy** covers the whole app: Classic's soft grey page (a white page was tried and was too bright), flat cards, soft boxes, light borderless toolbars, taller sidebar rows, and its own touches on Mail, writing a mail, the Projects list and board, and Chat. Every screen has been drawn in it and looked over. The client page's folding rows, Files' side list and rows, and Settings' side menu have had their own pass too. What's left is whatever shows up in daily use.
- **Light sidebar** works with every style and palette.
- **The menu is a choice too**: a dock along the bottom, which a new install starts with, the sidebar, or a Menu button that opens every page over the one on screen. All three stay.
- Still to build: **Compact** (dense, small corners, thin lines) and **Soft** (rounder, pill buttons, tinted panels).
- Later, if styles aren't enough: a density choice, and bigger layout changes such as a top bar.
- Small text sizes now have names every page uses, and Airy makes each a size bigger. Body text and headings are unchanged.

## Later

### WordPress connector

One list of every client site that runs WordPress, showing the version of core, each theme and each plugin, and what has an update waiting. Updates are applied from the list, one site or several at once, only on the person's own click.

- It talks to WordPress's own REST API (`/wp-json/wp/v2/plugins`, `/themes`) signed in with an application password, which WordPress makes per user under Users, Profile. No plugin of ours has to be installed on the site.
- The application password is a site login, so it belongs in the `site_logins` table through `services.Clients` and never travels to assistants, the connector or Xero.
- Rules (what counts as out of date, what may be updated together) go in `Webignite.Core/Web` with tests; the calls go in `Webignite.Services`.
- Premium plugins that update through their own licence servers (Elementor Pro and the like) may not show an update through the API; the list should say so instead of showing them as up to date.
- Worth adding at the same time: licence renewals for premium plugins and who pays for each.
