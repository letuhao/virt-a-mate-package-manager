# 08 — Hub Integration API (hub.virtamate.com)

`FormHub` + `HubItem` are an in-app browser for the official VaM content hub. They let the user browse/search hosted resources, see what they already own, and **generate download-link lists** for missing dependencies and updates. ⚠ The app does **not** download files itself — it produces URL lists and opens browser pages; the user pastes the URLs into an external batch downloader ("Chrono") while logged into the hub in their browser.

✎ **Rebuild opportunity:** a modern rebuild could integrate authenticated downloading directly against these same endpoints.

## Endpoint 1 — `POST https://hub.virtamate.com/citizenx/api.php`

`application/json` body, always includes `"source":"VaM"` and an `"action"`. Response is JSON (parsed with SimpleJSON).

| action | extra request fields | response fields used |
|---|---|---|
| `getInfo` | — | filter vocabulary: arrays `category` (used as *PayType*, e.g. Free/Paid), `location`, `type` (used as *Category*), `sort`; objects `tags` (keys), `users` (keys → creators) |
| `getResources` | `latest_image:"Y"`, `location`, `category`(paytype), `type`(category, omitted if All), `username`, `tags`, `search`, `searchall:"true"`, `sort` (`primary[,secondary]`), `perpage:48`, `page` | `pagination{total_found,total_pages,page}` + `resources[]` |
| `getResourceDetail` | `latest_image:"Y"`, `resource_id` | `hubFiles[]` (`{filename, urlHosted}`) + `dependencies{}` (map file → `[{filename, downloadUrl}]`) |
| `findPackages` | `packages` (comma-joined var names, e.g. `Creator.Pkg.latest,…`) | `packages{}` → children `{downloadUrl, filename}` |

### `resources[]` item shape (getResources)
`category, type, title, version_string, tag_line, rating_avg, rating_count, download_count, last_update` (unix secs), `image_url, icon_url, username, resource_id, download_url`, and optionally `hubFiles[]`.

## Endpoint 2 — `GET https://s3cdn.virtamate.com/data/packages.json`
A flat JSON map `"{filename}.var" : "{downloadId}"` of every package known to the hub. Used by the "scan for updates" feature.

## Browser launches (`Process.Start`)
- Resource image click → `https://hub.virtamate.com/resources/{resource_id}/`
- "Go To Download" → the resource's `download_url`.
- "Google" fallback (from other forms) → `https://www.google.com/search?q={name(.latest→.1)} var`.

## HTTP mechanics
- One shared static `HttpClient`, 60 s timeout, **no authentication** (the generated download URLs require the user to be logged into the hub in their browser).
- Browsing: `httpClient.CancelPendingRequests()` (aborts an in-flight request when the filter changes) then `PostAsync(...).ContinueWith(...)` → on success `BeginInvoke(RefreshResource)` back onto the UI thread.
- `getInfo`/`findPackages`/`getResourceDetail`/`packages.json` use awaited one-shot calls.

## UI & ownership matching
- **Filter bar** populated from `getInfo`: Hosted(location), PayType, Category, Creator, Tags, primary/secondary Sort, page, Search. Default filter = PayType "Free". Any change → `GetResources(...)`.
- **Results grid**: a `FlowLayoutPanel` pre-seeded with exactly **48 reused `HubItem` controls** (shown/hidden per page, not recreated).
- **`RefreshResource`** per page: for each item, `SetResource(json)` and compute an ownership status by comparing hub `hubFiles` versions against the local DB via `form1.VarExistName(pkg + ".latest")`:
  - `"In Repository"` (own ≥ hub) — click locates the var in Form1.
  - `"{local} Upgrade to {hub}"` / `"Generate Download List"` — click → `getResourceDetail` → emit download links.
  - `"Go To Download"` (no hubFiles, has download_url) — opens the browser.
- `HubItem` renders a 5-star rating (quarter-star granularity, hand-drawn PictureBoxes), download count, localized last-update date, async preview image + creator icon; clicking type/creator raises `ClickFilter` to re-query.
- `HubItem.GetResourceDetail` builds a `varDownloadUrl` map from `hubFiles[].urlHosted` plus every dependency `downloadUrl`, then raises `GenLinkList`; FormHub keeps only names where `form1.VarExistName(name)` is `"missing"` or version-mismatch (`endsWith("$")`) and adds them to the download list.

## Two batch scanners
1. **Scan Hub for All Missing Depends** — `form1.MissingDependencies()` → `findPackages` → keep packages with a real `downloadUrl` whose `filename` isn't already owned (`!form1.FindByvarName`).
2. **Scan Hub for Updates** — `GET packages.json` → collapse to highest version per `Creator.Package` (keep its download id) → for each locally owned (`VarExistName(pkg+".latest") != "missing"`) with hub version > local → `findPackages` → resolve to download URLs.

## Download-list panel
An auto-hiding `SplitContainer` with a `ListView` of varName + URL. **Copy to Clipboard** dumps all URLs newline-joined (for pasting into Chrono). ⚠ Not persisted — closing warns if non-empty.

## DB/FS footprint
Read-only against the local DB (through `VarExistName`/`FindByvarName`/`MissingDependencies`). No DB or disk writes except clipboard and OS-level image caching. `FormHub` itself has **no direct DataSet access** (all commented out).

Continue to [09 — MMDLoader & LoadScene](./09-MMDLoader-and-LoadScene.md).
