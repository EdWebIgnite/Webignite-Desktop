# What may come later

Tools and ideas that were agreed as worth doing. Nothing here is built. When one is started, take it off this list and add it to [architecture.md](architecture.md) in the same PR.

## The browser rule

The app has no browser engine of its own, to stay small and light on memory. WebView2 is allowed for the web tools below that need a page drawn as a browser draws it, on these terms:

- It uses the WebView2 that Windows already has and keeps up to date. None is shipped with the app. A PC without it is told so, with where to get it, and the rest of the app carries on.
- It starts when one of those tools' windows opens and is shut down when the window closes. While open it costs roughly 200 to 400 MB in its own `msedgewebview2.exe` processes; closed, nothing.
- Nothing at start-up touches it. `tools/Measure` should check that no WebView2 process is left after a tool closes.
- Mail keeps its own light renderer. Nothing outside the web tools uses WebView2.
- WebView2 is Windows only, so `tools/Snapshot` (which runs on Linux) needs a stand-in, as `IVideoTools` is for video.

Still to check before building: the best way to host WebView2 in Avalonia 12.

## Agreed, to build

Each is a tool the person opens and uses. Rules go in `Webignite.Core` with tests; picture, font and PDF work goes in `Webignite.Tools`.

### Branding

- **Logo export pack.** One master logo in; every size and format out (PNG, JPG, WebP, SVG; transparent, white and black versions), named properly and zipped for the client.
- **Favicon and app icon maker.** One logo in, the full favicon set out, with the HTML to paste.
- **Palette builder.** Tints and shades from one colour, a palette pulled from a logo or photo, HEX, RGB, HSL and CMYK, a contrast check for text on a background, and the palette as CSS variables. An addition to the Colours tool.
- **Brand sheet maker.** A client's logo, colours and fonts laid out as a one-page PDF.
- **Social image sizer.** One picture cropped to Facebook, Instagram, LinkedIn, WhatsApp and link-preview sizes in one go.

### Websites

- **Site crawler.** Every page of a site with its title, description, status, broken links and missing alt text, saved as a spreadsheet. Reads the HTML directly; uses WebView2 only for a site that builds its pages with JavaScript.
- **DNS and mail record tool.** Look up any record, check it has spread, and build SPF, DKIM and DMARC records to paste in at the host.
- **Screen ruler.** Measure pixels on screen from any program, like the colour picker.
- **Small web tools.** Lorem ipsum, placeholder pictures, px to rem, CSS and JS minify, JSON tidy, slug and case converter, UTM link builder.

### Websites, drawn by WebView2

- **Save a web page as a PDF.**
- **Website screenshots.** A page at set widths (phone, tablet, desktop), the whole page top to bottom included, saved to the client's folder or opened in Mark-up.
- **Responsive preview.** A live page that can be scrolled and clicked, at any width.
- **Phone simulator.** The same preview as a named phone or tablet: its screen size, pixel density, touch and the address it tells sites it is, turned upright or sideways, as browser device toolbars do. It is still Edge's engine pretending, so a fault that only Safari on a real iPhone has won't show.

### Everyday

- **Text snippets from any program.** A short code or a shortcut pastes a saved block of text: banking details, standard replies, code.
- **Copy text from a picture.** Pick an area of the screen and get its words, through Windows' own text reading.
- **Compare.** Two files or two folders side by side, showing what differs.
- **Duplicate file finder** in Files.

## Later

### WordPress connector

One list of every client site that runs WordPress, showing the version of core, each theme and each plugin, and what has an update waiting. Updates are applied from the list, one site or several at once, only on the person's own click.

- It talks to WordPress's own REST API (`/wp-json/wp/v2/plugins`, `/themes`) signed in with an application password, which WordPress makes per user under Users, Profile. No plugin of ours has to be installed on the site.
- The application password is a site login, so it belongs in the `site_logins` table through `services.Clients` and never travels to assistants, the connector or Xero.
- Rules (what counts as out of date, what may be updated together) go in `Webignite.Core/Web` with tests; the calls go in `Webignite.Services`.
- Premium plugins that update through their own licence servers (Elementor Pro and the like) may not show an update through the API; the list should say so instead of showing them as up to date.
- Worth adding at the same time: licence renewals for premium plugins and who pays for each.
