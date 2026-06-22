using Microsoft.EntityFrameworkCore;

namespace HydrusComicCompanion.Data;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<HydrusSettingsRecord> HydrusSettings => Set<HydrusSettingsRecord>();

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
            entity.Property(x => x.TargetFileDomain).IsRequired();
            entity.Property(x => x.SeriesNamespace).IsRequired();
            entity.Property(x => x.VolumeNamespace).IsRequired();
            entity.Property(x => x.ChapterNamespace).IsRequired();
            entity.Property(x => x.PageNamespace).IsRequired();
        });
    }
}
