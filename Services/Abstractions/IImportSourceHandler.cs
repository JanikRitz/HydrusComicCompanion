using HydrusComicCompanion.Models;

namespace HydrusComicCompanion.Services.Abstractions;

/// <summary>
/// Represents different sources for importing comic content into the system.
/// </summary>
public enum ImportSource
{
    /// <summary>Import from a local CBZ/CBR archive file.</summary>
    Archive,

    /// <summary>Import by syncing an existing title from Hydrus.</summary>
    Title,

    /// <summary>Import existing Hydrus titles using a custom tag service and namespace mapping.</summary>
    HydrusMapped,

    /// <summary>Import from an open Hydrus page (future feature).</summary>
    OpenHydrusPage,

    /// <summary>Import from a Calibre book library (future feature).</summary>
    CalibreBook
}
