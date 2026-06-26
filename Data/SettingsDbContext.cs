using Microsoft.EntityFrameworkCore;

namespace HydrusComicCompanion.Data;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<HydrusSettingsRecord> HydrusSettings => Set<HydrusSettingsRecord>();

    public DbSet<ComicsRecord> Comic => Set<ComicsRecord>();

    public DbSet<ChapterRecord> Chapters => Set<ChapterRecord>();

    public DbSet<PageRecord> Pages => Set<PageRecord>();

    public DbSet<MetadataRecord> Metadata => Set<MetadataRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HydrusSettingsRecord>(entity =>
        {
            entity.ToTable("HydrusSettings");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ApiUrl).IsRequired();
            entity.Property(x => x.ProtectedApiAccessKey).IsRequired();
            entity.Property(x => x.PrimaryTagService).IsRequired();
            entity.Property(x => x.TagServiceKey).IsRequired();
            entity.Property(x => x.TargetFileDomain).IsRequired();
            entity.Property(x => x.TitleNamespace).IsRequired();
            entity.Property(x => x.VolumeNamespace).IsRequired();
            entity.Property(x => x.ChapterNamespace).IsRequired();
            entity.Property(x => x.PageNamespace).IsRequired();
            entity.Property(x => x.CoverPageTag).IsRequired();
            entity.Property(x => x.FullTitleNoteName).IsRequired();
            entity.Property(x => x.ComicCommentNoteName).IsRequired();
            entity.Property(x => x.OcrTextNoteName).IsRequired();
        });

        modelBuilder.Entity<ComicsRecord>(entity =>
        {
            entity.ToTable("Series");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title).IsRequired();
            entity.Property(x => x.DisplayTitle);
            entity.Property(x => x.Comment);
            entity.Property(x => x.CoverFileHash);
            entity.Property(x => x.LastSyncedAt);

            entity.HasMany(x => x.Chapters)
                .WithOne(x => x.Comics)
                .HasForeignKey(x => x.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Metadata)
                .WithOne(x => x.Comics)
                .HasForeignKey(x => x.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChapterRecord>(entity =>
        {
            entity.ToTable("Chapters");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.VolumeNumber);
            entity.Property(x => x.ChapterNumber);
            entity.Property(x => x.Title);

            entity.HasIndex(x => x.SeriesId);

            entity.HasMany(x => x.Pages)
                .WithOne(x => x.Chapter)
                .HasForeignKey(x => x.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PageRecord>(entity =>
        {
            entity.ToTable("Pages");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FileHash).IsRequired();
            entity.Property(x => x.PageNumber).IsRequired();
            entity.Property(x => x.MimeType);
            entity.Property(x => x.OcrText);

            entity.HasIndex(x => x.ChapterId);
        });

        modelBuilder.Entity<MetadataRecord>(entity =>
        {
            entity.ToTable("Metadata");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Key).IsRequired();
            entity.Property(x => x.Value).IsRequired();

            entity.HasIndex(x => x.SeriesId);
        });
    }
}
