# Data Handling (SQLite + EF Core)

This document describes the current SQLite-backed persistence model and the EF Core workflow for schema changes.

## Current Storage Scope

The app now persists both **application settings** and the first **comic library cache** tables in SQLite through EF Core.

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
- `VolumeNamespace`
- `ChapterNamespace`
- `PageNamespace`
- `BackgroundSyncIntervalMinutes`

`HydrusSettingsService` handles normalization and encryption/decryption of the API access key using ASP.NET Core Data Protection.

## Comic Cache Model

The local cache currently includes these tables:

- `Series` — title/work cache root (`Title`, `CoverFileHash`, `LastSyncedAt`)
- `Chapters` — `SeriesId`, `VolumeNumber?`, `ChapterNumber?`, `Title`
- `Pages` — `ChapterId`, `FileHash`, `PageNumber`, `MimeType`
- `Metadata` — `SeriesId`, `Key`, `Value`

These entities back the library and title detail screens and are ready for later Hydrus sync work.

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
