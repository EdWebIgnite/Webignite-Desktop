# What may come later

Tools and ideas that were agreed as worth doing. Nothing here is built. When one is started, take it off this list and add it to [architecture.md](architecture.md) in the same PR.

## The browser rule

The app has no browser engine of its own, to stay small and light on memory. WebView2 is allowed for the web tools below that need a page drawn as a browser draws it, on these terms:

- It uses the WebView2 that Windows already has and keeps up to date. None is shipped with the app. A PC without it is told so, with where to get it, and the rest of the app carries on.
- It starts when one of those tools' windows opens and is shut down when the window closes. While open it costs roughly 200 to 400 MB in its own `msedgewebview2.exe` processes; closed, nothing.
- Nothing at start-up touches it. `WebView2Tests` checks on Windows that no WebView2 process is left after it is closed (`tools/Measure` runs on Linux, where there is no WebView2).
- Mail keeps its own light renderer. Nothing outside the web tools uses WebView2.
- WebView2 is Windows only, so `tools/Snapshot` (which runs on Linux) needs a stand-in, as `IVideoTools` is for video.

How it is hosted (built in step 4): Microsoft's own small wrapper (`Microsoft.Web.WebView2`, only its `Core` library and loader, under 1 MB) drawing in a plain pane of the tool's window. A tool asks `services.NewBrowser()` for an `IWebEngine` and disposes it when its window closes; see Page to PDF or picture in [architecture.md](architecture.md). Avalonia's own `Avalonia.Controls.WebView` was looked at and left: it is free now, but the tools need the browser's developer tools (whole-page pictures, pretending to be a phone), which Microsoft's wrapper gives directly.

## Agreed, to build

Each is a tool the person opens and uses. Rules go in `Webignite.Core` with tests; picture, font and PDF work goes in `Webignite.Tools`.

### Branding

- **Logo export pack.** One master logo in; every size and format out (PNG, JPG, WebP, SVG; transparent, white and black versions), named properly and zipped for the client.
- **Favicon and app icon maker.** One logo in, the full favicon set out, with the HTML to paste.
- **Palette builder.** Tints and shades from one colour, a palette pulled from a logo or photo, HEX, RGB, HSL and CMYK, a contrast check for text on a background, and the palette as CSS variables. An addition to the Colours tool.
- **Brand sheet maker.** A client's logo, colours and fonts laid out as a one-page PDF.
- **Social image sizer.** One picture cropped to Facebook, Instagram, LinkedIn, WhatsApp and link-preview sizes in one go.

### Websites

- **Screen ruler.** Measure pixels on screen from any program, like the colour picker.

### Websites, drawn by WebView2

- **Responsive preview.** A live page that can be scrolled and clicked, at any width.
- **Phone simulator.** The same preview as a named phone or tablet: its screen size, pixel density, touch and the address it tells sites it is, turned upright or sideways, as browser device toolbars do. It is still Edge's engine pretending, so a fault that only Safari on a real iPhone has won't show.
  - A range of devices to pick from, kept as a list in Core so one is added in a line: iPhones (a small older one such as the SE, the standard size of the last few years, and the largest Pro Max), Samsungs (a recent S, a large Ultra, a mid-range A and a 360-wide budget Android, which many South African visitors use, plus a fold), a Pixel, iPads (Mini, Air, the large Pro) and an Android tablet, then a laptop and a desktop. A box takes any other size. The exact sizes are looked up when it is built.

### Everyday

- **Text snippets from any program.** A short code or a shortcut pastes a saved block of text: banking details, standard replies, code.
- **Copy text from a picture.** Pick an area of the screen and get its words, through Windows' own text reading.
- **Compare.** Two files or two folders side by side, showing what differs.
- **Duplicate file finder** in Files.

## Order to build them in

Web first, as that is most of the work; branding last. Each step is its own PR.

1. ~~Small web tools~~ Built: Web helpers, under Web tools.
2. ~~DNS and mail record tool~~ Built: DNS records, under Web tools.
3. ~~Site crawler, reading HTML only~~ Built: Site crawler, under Web tools.
4. ~~WebView2 groundwork, with Save a web page as a PDF and Website screenshots~~ Built: Page to PDF or picture, under Web tools.
5. **Responsive preview and Phone simulator.** One window, on top of step 4.
6. **Site crawler for sites that need JavaScript.** Needs step 4.
7. **Screen ruler.** Reuses the colour picker's frozen screen.
8. **Text snippets from any program.**
9. **Copy text from a picture.** Reuses the screenshot picker.
10. **Compare**, then **Duplicate file finder**, both in Files.
11. **Palette builder**, in the Colours tool.
12. **Favicon and app icon maker**, then **Logo export pack.** They share the picture work.
13. **Social image sizer.**
14. **Brand sheet maker.** Last, as it uses the palette and the logo work.

## Looks

The app is getting more than one style to try side by side, so we can see what works. Colours and style are separate choices (Settings, Style), and Classic stays as it is.

- **Airy** covers the whole app: Classic's soft grey page (a white page was tried and was too bright), flat cards, soft boxes, light borderless toolbars, taller sidebar rows, and its own touches on Mail, writing a mail, the Projects list and board, and Chat. Every screen has been drawn in it and looked over. The client page's folding rows, Files' side list and rows, and Settings' side menu have had their own pass too. What's left is whatever shows up in daily use.
- **Light sidebar** works with every style and palette.
- **The menu is a choice too**: the sidebar, a Menu button that opens every page over the one on screen, or a dock along the bottom. All three stay; the point is to try them.
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
