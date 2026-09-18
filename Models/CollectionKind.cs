namespace HydrusComicCompanion.Models;

public enum CollectionKind
{
    Comic,
    Imageset
}

public sealed record CollectionIdentity(string Title, CollectionKind Kind);
