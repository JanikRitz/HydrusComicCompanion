# Manager collection catalog

`GET /api/manager/comics?q=...&limit=...&sort=...&kind=...` returns a JSON array of cached comics and imagesets:

```json
[
	{ "id": 42, "title": "Example comic", "kind": "Comic", "readerPath": "read/42/17/1", "coverHash": "abc123", "updatedAt": "2026-11-21T14:00:00+00:00" },
	{ "id": 43, "title": "Example album", "kind": "Imageset", "readerPath": "collection/43/image/def456", "coverHash": "def456", "updatedAt": "2026-11-21T14:00:00+00:00" }
]
```

- `kind` is `Comic` or `Imageset`. `readerPath` is relative to the Companion base URL, without a leading slash. It points to the first readable comic page (using its one-based position in its chapter) or the first readable image in gallery order. It does not use reading progress. It is null if there is no cached readable file; the details page is available at `/collection/{id}` for either kind.
- `coverHash` prefers the cached cover, then the first readable file. When present, use `/media/thumbnail/{coverHash}`.
- `q` omitted, empty, or whitespace-only browses the cache. Other queries match a case-insensitive literal substring of the title or display title, an exact current page tag across Hydrus services, or a case-insensitive literal substring of the configured OCR note on any cached page variant. An unqualified tag matches the value after the first colon (or the whole bare tag); a qualified tag must match the entire tag.
- `limit` defaults to 50 and is capped at 200. Non-positive and non-integer values return HTTP 400.
- `sort` accepts `Default`, `TitleAscending`, `TitleDescending`, `DateNewest`, `DateOldest`, or `Random`, case-insensitively. `Default` is title ascending. Date sorts put missing timestamps last. Unknown sorts return HTTP 400.
- The optional `kind` query parameter accepts `Comic` or `Imageset`, case-insensitively. It filters before sorting and applying `limit`, so each kind can be searched independently. Omit it to search both kinds together. Other values, including an empty value, return HTTP 400.
- `title` prefers the display title. `updatedAt` is the last Companion cache sync timestamp, not a Hydrus modification date. `coverHash` and `updatedAt` can be null.

The endpoint reads only the Companion SQLite cache. Existing collections without cached page tags, including previously synced imagesets, need a normal sync to populate them. OCR is available only for Hydrus notes already synced under the configured OCR note name. Manager search and scheduled sync never open external OCR sidecars or call the OCR editor. The existing explicit OCR import remains separate and can write Hydrus notes.

Set `PSM_COMIC_SYNC_MINUTES` to a positive integer in the Companion process environment to enable a full catalog sync at startup and again that many minutes after each pass finishes. Unset, zero, negative, and invalid values leave scheduling disabled. This is the existing full sync, including imagesets, with no Hydrus writes. It serializes with manual sync operations inside one Companion process; it does not coordinate across replicas or with arbitrary Hydrus edits. As with manual full sync, it refreshes discovered collections and does not prune cached entries that disappear from discovery.

No new token or authentication scheme is required. The endpoint uses the same network trust boundary as the Companion app. Do not expose it directly to untrusted callers: even without raw tags or OCR in responses, matches can reveal whether a queried term exists. Use a trusted network or an authenticated reverse proxy.