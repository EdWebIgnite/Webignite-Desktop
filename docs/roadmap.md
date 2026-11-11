# What may come later

Ideas that were agreed as worth doing but not yet. Nothing here is built. When one is started, take it off this list and add it to [architecture.md](architecture.md) in the same PR.

## Later

### WordPress connector

One list of every client site that runs WordPress, showing the version of core, each theme and each plugin, and what has an update waiting. Updates are applied from the list, one site or several at once, only on the person's own click.

- It talks to WordPress's own REST API (`/wp-json/wp/v2/plugins`, `/themes`) signed in with an application password, which WordPress makes per user under Users, Profile. No plugin of ours has to be installed on the site.
- The application password is a site login, so it belongs in the `site_logins` table through `services.Clients` and never travels to assistants, the connector or Xero.
- Rules (what counts as out of date, what may be updated together) go in `Webignite.Core/Web` with tests; the calls go in `Webignite.Services`.
- Premium plugins that update through their own licence servers (Elementor Pro and the like) may not show an update through the API; the list should say so instead of showing them as up to date.
- Worth adding at the same time: licence renewals for premium plugins and who pays for each.
