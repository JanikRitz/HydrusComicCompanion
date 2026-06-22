# Hydrus Comic Web Reader - Architecture Plan

## Current Implementation Status
- [x] Blazor Server app shell is in place and MudBlazor is wired up.
- [x] EF Core + SQLite settings/cache database exists with migrations.
- [x] Hydrus settings are persisted locally and API service discovery is implemented.
- [x] Library and series detail pages are implemented against cached EF data.
- [?] Hydrus file discovery, metadata parsing, and sync pipeline.
- [ ] Media endpoint for streamed page images.
- [ ] CBZ export pipeline.
- [ ] Full reader view with URL-synced page navigation.

## Technology Stack

Framework: Blazor Server (.NET) + MudBlazor component library.

Why Blazor Server? Provides a unified, single-project architecture perfect for local network (LAN) usage. The server directly accesses the SQLite cache and Hydrus API, streaming UI updates to the browser via SignalR.

Database (Cache): Entity Framework Core (EF Core) using SQLite. SQLite is perfect for a local cache, requiring no separate server setup.

For implementation details and migration workflow, see `data-handling.md`.

Source of Truth: Hydrus Network Client (via local REST API).

## Robust Hydrus Tagging Taxonomy

To reliably parse flat files into a hierarchical comic structure, the Hydrus tagging must be strictly defined and adhered to.

Only writing tags into the my tags area of tags to distinguish from 'downloader tags' and 'public tag repository' and only using the my tags for building the comics (using the other Tag spaces for creating new comics).

Core Hierarchy Tags

- `series:[Series Name]` (e.g., `series:the sandman`)
- `volume:[Number]` (e.g., `volume:1` - Optional, defaults to 1 if missing)
- `chapter:[Number]` (e.g., `chapter:1` or `chapter:1.5` for sub-chapters)
- `page:[Number]` (e.g., `page:1` )

Metadata Tags (Syncable)

- `creator:[Name]`
- `genre:[Name]`
- `character:[Name]`
- `meta:cover page` (Used to query the library thumbnail, fallbacks to lowest page)

## Local Data & Caching Strategy (EF Core)

We do not want to query Hydrus for the library structure on every page load. The Blazor Server app will maintain an EF Core SQLite database that acts as an Index/Cache.

Entity Models

- Series: `Id`, `Title`, `CoverFileId` (Hash), `LastSynced`, `[Navigation] Chapters`, `[Navigation] Metadata`
- Chapter: `Id`, `SeriesId`, `VolumeNumber`, `ChapterNumber`, `Title`
- Page: `Id`, `ChapterId`, `FileHash` (Hydrus SHA256), `PageNumber`, `MimeType`
- Metadata: Key-Value mappings (e.g., Type: "Creator", Value: "Neil Gaiman") linked to Series.

Sync Logic (The "Discovery" Process)

The sync process is divided into two highly efficient steps utilizing the Hydrus Tag API:

1. Series Discovery (Lightweight): * The server queries Hydrus using GET /add_tags/search_tags?search=series:
  - Hydrus returns an array of all known series. The server cross-references this with the SQLite DB to identify new or missing series.
2. Deep Structure Sync (On-Demand or Background):
  - For a newly discovered series, the server queries `GET /get_files/search_files?tags=["series:the sandman"]`.
  - It takes those hashes, fetches their metadata, and maps the files into the `Volume -> Chapter -> Page` hierarchy in the SQLite DB, caching the structure for instant frontend reads.

## Hydrus API Integration

The Blazor Server app will use a registered HttpClient service to interact with the Hydrus API using the `Hydrus-Client-API-Access-Key` header.

Required Endpoints

1. Tag Search: `GET /add_tags/search_tags`  
  - Payload: `search=series:` (URL Encoded)
  - Use: Instantly retrieves a list of all series tags without querying file metadata.
2. File Discovery: `GET /get_files/search_files`
  - Payload: `tags=["series:the sandman"]` (JSON & URL Encoded), `file_service_key=[SelectedFileService]`
  - Use: Returns a list of File IDs/Hashes belonging to a specific series.
3. Metadata Parsing: `GET /get_files/file_metadata`
  - Payload: `hashes=[Array of FileHashes]`
  - Use: Retrieves all tags for the files. The C# logic will parse the namespaces (volume, chapter, page) and populate the EF Core DB.
4. Image Retrieval: `GET /get_files/file`
  - Payload: `hash=[FileHash]`
  - Use: Fetches the actual image bytes.
5. Metadata Writing: `POST /add_tags/add_tags`
  - Payload: `{ "hash": "...", "service_names_to_tags": { "my tags": ["creator:alan moore"] } }`
  - Use: Pushes external data scraped by the web app back into Hydrus.
6. Service Discovery: `GET /get_services`
  - Payload: None
  - Use: Retrieves all available file domains and tag services configured in the user's Hydrus client to populate the application's settings dropdowns.

## UI & Presentation (MudBlazor)

Note: the reader state should be encoded in the URL to make bookmarking easier (e.g., `/read/{SeriesId}/{ChapterId}/{PageNumber}`).

### Library View (Home)

- Layout: A responsive `MudGrid` of `MudCard` components.
- Content: Each card represents a `Series`.
- Image: The card image is loaded via a local media endpoint (e.g., `<img src="/media/image/{CoverFileHash}?thumb=true" />`).
- Interaction: Clicking a card routes to the Series/Chapter list.

### Series Details View

- Layout: Top section with Series metadata (Creators, Genres using `MudChip`). Bottom section is a `MudTable` or `MudList` of Chapters.
- Actions: * "Sync External Metadata" button (triggers scraping agent and pushes to Hydrus).
- "Download CBZ" button on individual chapters (triggers the archive generation process).

### The Reader View

- Layout: A full-screen immersive view. Hide the standard `MudAppBar` / `MudDrawer`.
- Presentation: A central `MudImage` or custom `<img>` tag centered on screen.
- Navigation: Left/Right arrow key listeners, and invisible click zones (left 20% of screen = previous, right 20% = next).
- State: The current `PageNumber` is tracked in the component state and synced to the URL.

### Serving Pages (Media Endpoint)

Because Blazor Server components communicate via SignalR, binary image data should be served via standard HTTP endpoints to allow browser caching.

- App registers a Minimal API endpoint: `app.MapGet("/media/page/{FileHash}", ...)`
- Frontend requests: `<img src="/media/page/{FileHash}" />`
- Endpoint logic checks `IMemoryCache` (optional).
- Endpoint calls Hydrus `GET /get_files/file`, receives the stream.
- Endpoint returns a `Results.Stream` to the frontend.

Note: This strictly hides the Hydrus API key from the browser and keeps Hydrus secure behind the Blazor server.

### Download as CBZ Pipeline

- To allow users to export readable archives directly from the app without duplicating storage:
- User clicks "Download CBZ" on a Chapter.
- The server queries the SQLite DB for all Page hashes associated with that `ChapterId`, ordered by `PageNumber`.
- The server initializes a memory stream and uses `System.IO.Compression.ZipArchive` to create a new ZIP file.
- It iterates over the page hashes, fetching the raw bytes from Hydrus (`GET /get_files/file`) and writing them sequentially into the `ZipArchive` (named dynamically, e.g., `001.jpg`, `002.jpg`).
- The memory stream is returned to the browser via JSInterop (`BlazorDownloadFile` or standard anchor tag stream) with a .cbz extension (e.g., `The_Sandman_Ch_01.cbz`).

## Configuration & App Settings

To ensure the application is flexible and decoupled from hardcoded values, a dedicated Settings View/Configuration File (`appsettings.json` and DB) will manage the following:

### Connection Settings

- Hydrus API URL: (e.g., `http://127.0.0.1:45869`)
- API Access Key: The permanent access key generated in the Hydrus client.

### Target Services (Populated via `GET /get_services`)

- Primary Tag Service: The specific tag domain to read from/write to (e.g., `my tags`).
- Target File Domain: The file service to scope searches to (e.g., `all local files` or `my files`), ensuring deleted files or remote files aren't accidentally queried.

### Taxonomy Mappings

Allows users to override the default namespaces if their Hydrus library uses different conventions:

- Series Namespace: Default `series:`
- Volume Namespace: Default `volume:`
- Chapter Namespace: Default `chapter:`
- Page Namespace: Default `page:`

### Sync Settings

Background Sync Interval: Time in minutes (e.g., 15, 60, or 0 for manual-only) to query Hydrus for newly added series/chapters.