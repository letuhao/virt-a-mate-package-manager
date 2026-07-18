using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class UsageEventConfig : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> b)
    {
        b.ToTable("UsageEvent");
        b.HasKey(x => x.Id);

        // Stored as an INTEGER UTC epoch (ms) — never a locale-formatted datetime string.
        b.Property(x => x.TimestampUnixMs);

        b.HasIndex(x => new { x.PackageId, x.TimestampUnixMs });

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UsageStatConfig : IEntityTypeConfiguration<UsageStat>
{
    public void Configure(EntityTypeBuilder<UsageStat> b)
    {
        b.ToTable("UsageStat");
        b.HasKey(x => x.PackageId); // 1:1 with Package
        b.Property(x => x.PackageId).ValueGeneratedNever();

        b.HasIndex(x => x.Class);

        b.HasOne(x => x.Package)
            .WithOne()
            .HasForeignKey<UsageStat>(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MigrationJobConfig : IEntityTypeConfiguration<MigrationJob>
{
    public void Configure(EntityTypeBuilder<MigrationJob> b)
    {
        b.ToTable("MigrationJob");
        b.HasKey(x => x.Id);

        // One live job per file: unique on VarFileId while not terminal (Done=6, Failed=7, Cancelled=8).
        b.HasIndex(x => x.VarFileId)
            .IsUnique()
            .HasFilter("\"State\" NOT IN (6, 7, 8)");

        b.HasOne(x => x.VarFile)
            .WithMany()
            .HasForeignKey(x => x.VarFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
