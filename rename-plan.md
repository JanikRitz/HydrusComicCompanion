# Rename & Taxonomy Migration Plan (title -> volume? -> chapter? -> page)

## Scope
Update the app from `series -> volume -> chapter -> page` to `title -> volume -> chapter -> page`, where:
- `title` is the structural root namespace.
- `volume` and `chapter` are both independently optional.
- `creator` and `series` are non-structural metadata used for disambiguation of identical canonical titles.

## Places to Change

### 1) Configuration & Settings Model
- `Models/HydrusSettings.cs`
  - Rename `SeriesNamespace` -> `TitleNamespace`.
  - Keep `VolumeNamespace`, `ChapterNamespace`, `PageNamespace`.
  - Update defaults to `title:`, `volume:`, `chapter:`, `page:`.
- `Data/HydrusSettingsRecord.cs`
  - Rename persisted column/property `SeriesNamespace` -> `TitleNamespace`.
- `Services/HydrusSettingsService.cs`
  - Mapping and normalization updates for `TitleNamespace`.
  - Backward compatibility path for existing DB values.
- `Components/Pages/Settings.razor`
  - UI label changes: “Series Namespace” -> “Title Namespace”.
  - Optionality text for volume/chapter.

### 2) EF Core Domain Model & Schema
- `Data/SeriesRecord.cs`
  - Rework naming/semantics to represent canonical title entity (or introduce `TitleRecord`).
  - Add disambiguation metadata fields if stored directly (creator/series), or keep in metadata table with deterministic lookup rules.
- `Data/ChapterRecord.cs`
  - Make `VolumeNumber` nullable (`int?`).
  - Make `ChapterNumber` nullable (`decimal?`).
- `Data/SettingsDbContext.cs`
  - Update entity configuration for nullable chapter/volume fields.
  - Update naming if `Series` table/entity is renamed.
- `Data/Migrations/20260622175009_AddComicLibrarySchema.cs`
- `Data/Migrations/20260622175009_AddComicLibrarySchema.Designer.cs`
- `Data/Migrations/SettingsDbContextModelSnapshot.cs`
  - Add new migration(s) for schema transition (rename/add columns, nullable changes, table rename if chosen).
  - Ensure existing SQLite data migration path is valid.

### 3) Sync Pipeline (Hydrus -> Cache)
- `Services/HydrusSyncService.cs`
  - Discovery/search should use `title:` namespace.
  - Structural parse must require `title` + `page`, while treating `volume` and `chapter` as optional.
  - Grouping strategy for missing tags:
    - no volume + no chapter: single logical chapter bucket.
    - no volume + chapter present: chapter-only grouping.
    - volume + chapter present: full grouping.
  - Ensure sort/order logic handles null `VolumeNumber`/`ChapterNumber` consistently.
  - `IsStructuralNamespace` should include `title` instead of `series`.
- `Services/HydrusApiService.cs`
  - `DiscoverSeriesAsync`/related logic should be converted to title-focused discovery (method rename likely required).
  - Search prefix and namespace extraction should use `TitleNamespace`.

### 4) Import Pipeline (Archive -> Hydrus + Cache)
- `Models/ComicImportModels.cs`
  - `ComicImportRequest`: make volume optional (`int?`).
  - Chapter modeling should allow chapterless imports.
- `Services/ComicImportService.cs`
  - Tag emission must always include `title` + `page`.
  - Emit `volume` and `chapter` tags only when provided.
  - Local cache merge logic must handle nullable volume/chapter keys.
  - Conflict detection keys must support chapterless and/or volumeless imports.
- `Components/Pages/Import.razor`
  - Metadata step UX: volume optional.
  - Chapter placement step should allow no chapter tags when user wants flat pages.

### 5) UI/UX & Routing Terminology
- `Components/Pages/Home.razor`
- `Components/Pages/Series.razor`
- `Components/Pages/ComicSeriesCard.razor`
- `Components/Pages/DeleteSeriesDialog.razor`
- `Components/Pages/Reader.razor`
  - Rename user-facing terminology from “Series” to “Title” where structural.
  - Keep displaying creator/series metadata as disambiguation context.
  - Reader header/order logic must tolerate null volume/chapter values.

### 6) Service Interfaces & Contracts
- `Services/IHydrusApiService.cs`
- `Services/IHydrusSyncService.cs`
- `Services/IComicImportService.cs`
- `Models/HydrusApiModels.cs`
  - Rename methods/properties/contracts where they are hard-coded to series semantics.
  - Ensure naming is coherent across DI registrations and callers.

### 7) App Bootstrap & Docs
- `Program.cs`
  - Validate DI wiring after interface/method/entity renames.
- `data-handling.md`
- `plan.md`
  - Keep docs aligned with title-first, optional volume/chapter model.

### 8) Validation Pass
- Build + migration validation for SQLite schema updates.
- Verify settings load/save and sync/import flows end-to-end.
- Verify reader navigation for:
  - title + page only
  - title + chapter + page
  - title + volume + chapter + page
