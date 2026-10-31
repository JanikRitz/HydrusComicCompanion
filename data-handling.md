# Data Handling (SQLite + EF Core)

This document describes the current SQLite-backed persistence model and the EF Core workflow for schema changes.

## Current Storage Scope

The app persists **application settings** and a **collections cache** for comics and imagesets in SQLite through EF Core.

- Database file: `App_Data/settings.db`
- DbContext: `Data/SettingsDbContext.cs`
- Migrations: `Data/Migrations/`

## Runtime Behavior

At application startup, pending EF Core migrations are applied automatically:

- `Program.cs` creates a scoped context via `IDbContextFactory<SettingsDbContext>`
- `Database.MigrateAsync()` is executed before request handling

This means schema updates are managed through migrations, not `EnsureCreated`.

## Settings Persistence Model

Settings are stored in table `HydrusSettings` as a singleton row (`Id = 1`).

Columns:

- `Id`
- `ApiUrl`
- `ProtectedApiAccessKey`
- `PrimaryTagService`
- `TargetFileDomain`
- `TitleNamespace`
- `SetNamespace`
- `IndexNamespace`
- `VolumeNamespace`
- `ChapterNamespace`
- `PageNamespace`
- `BackgroundSyncIntervalMinutes`

`HydrusSettingsService` handles normalization and encryption/decryption of the API access key using ASP.NET Core Data Protection.

## Collection Cache Model

The local cache currently includes these tables:

- `Series` — collection cache root (`Title`, `Kind`, `CoverFileHash`, `LastSyncedAt`); identity includes both title and kind
- `Chapters` — `SeriesId`, `VolumeNumber?`, `ChapterNumber?`, `Title`
- `Pages` — `ChapterId`, `PageNumber`
- `PageVariants` — `PageId`, `FileHash`, `MimeType`, `OcrText`, `IsDefault`, `Label`, `ImageIndex?`
- `Metadata` — `SeriesId`, `Key`, `Value`

Comics default to the `comic` title namespace and `volume` → `chapter` → `page` ordering. Files sharing a logical page are variants; `variant:default` is preferred, with manual reader switching. Hash ordering provides a deterministic fallback.

In the comic reader, switch to the desired variant and click **Pin variant** to keep its label (case-insensitive) across pages and chapters. Pinning the default follows each page's default instead. If a pinned label is missing, the reader shows the default (or the first deterministically ordered variant), displays a notice, and retains the pin for later pages. Manual switching overrides only the current page visit. When no variant is pinned, navigating forward or backward always shows the page's default (or the first deterministically ordered variant); previous manual selections are not remembered. **Unpin** leaves the displayed image unchanged and restores default selection on subsequent navigation. Unlabeled alternates cannot be pinned. Pins last while reading the same comic; leaving the reader, reloading, or opening another comic clears them. Pinning does not modify Hydrus metadata or cache defaults.

Imagesets default to the `set` title namespace and `index` ordering. They reuse one internal cache chapter, but have no chapter/volume UI or variant hiding. Every image appears in the gallery and full-size navigation. `ImageIndex` preserves the nullable Hydrus index; cache page numbers are internal ordinals. Duplicate indices sort by hash, and missing indices sort last. All variant attributes are displayed; multiple attributes are stored in `Label` separated by newlines.

Discovery respects both configured `page` and `index` namespaces, as well as cover and single-page markers. Files tagged with both title namespaces belong to both collection types. Namespaces remain configurable in Settings.

The collection schema update does not rewrite Hydrus tags or convert existing saved namespace mappings. Existing installations can choose the desired comic title namespace in Settings.

On the home page, **Import new comics** discovers comics and imagesets in Hydrus using the configured namespaces. Only collections whose title-and-kind identity is absent from the local cache are imported. Each new collection receives the same full metadata sync as a regular refresh, including chapters, pages, variants, cover, metadata tags, and configured notes (display title, comment, and OCR). Existing cache entries are left untouched, even if they have not been synced yet; use the existing collection refresh actions to update those. Import progress can be stopped, keeping completed collections. Errors are shown on the home page; running import again retries collections that are still missing.

## Regression Checks

Run `dotnet run --project Tests/CollectionChecks.csproj` from the repository root. This package-free runner uses a simulated Hydrus API and an in-memory SQLite database; it never connects to a live Hydrus client or changes `App_Data/settings.db`.

## Design-Time EF Tooling

A design-time factory is configured for tooling:

- `Data/SettingsDbContextFactory.cs` (`IDesignTimeDbContextFactory<SettingsDbContext>`)

A local tool manifest is used to pin EF CLI version with .NET 10 runtime compatibility:

- `dotnet-tools.json`
- Tool: `dotnet-ef` `10.0.9`

## Migration Workflow

From repository root (`C:\ProgrammingProjects\HydrusComicCompanion`):

1. Restore local tools (first time or after clone):
   - `dotnet tool restore`
2. Create a new migration:
   - `dotnet tool run dotnet-ef migrations add <MigrationName> --context SettingsDbContext --output-dir Data/Migrations`
3. Apply migrations manually (optional, startup also applies):
   - `dotnet tool run dotnet-ef database update --context SettingsDbContext`

## Notes for Future Data Expansion

When adding Hydrus sync and media endpoints:

- Keep the cache entities under `Data/` unless the domain grows enough to justify splitting the DbContext
- Generate a migration immediately after each model change
- Keep startup migration application enabled to avoid schema drift across development environments
