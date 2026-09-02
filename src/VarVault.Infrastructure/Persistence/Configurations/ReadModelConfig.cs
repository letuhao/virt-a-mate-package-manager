using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class PackageListItemConfig : IEntityTypeConfiguration<PackageListItem>
{
    public void Configure(EntityTypeBuilder<PackageListItem> b)
    {
        b.ToTable("PackageListItem");
        b.HasKey(x => x.PackageId);
        b.Property(x => x.PackageId).ValueGeneratedNever();

        // Composite indexes backing gallery sort orders (data-arch §5.8) — trailing PK for a stable tiebreak.
        b.HasIndex(x => new { x.Class, x.LastUsedAt, x.PackageId });
        b.HasIndex(x => new { x.Creator, x.TotalSize, x.PackageId });
        b.HasIndex(x => new { x.PrimaryType, x.VarName });
        b.HasIndex(x => x.IsFavorite);
        b.HasIndex(x => x.HasMissingDeps);
        b.HasIndex(x => new { x.AddedAt, x.VarName, x.PackageId });
        b.HasIndex(x => new { x.InstalledAt, x.VarName, x.PackageId });
        b.HasIndex(x => x.PackageId).HasFilter("IsActive = 1");

        b.HasOne(x => x.Package)
            .WithOne()
            .HasForeignKey<PackageListItem>(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrashItemConfig : IEntityTypeConfiguration<TrashItem>
{
    public void Configure(EntityTypeBuilder<TrashItem> b)
    {
        b.ToTable("TrashItem");
        b.HasKey(x => x.Id);
        b.Property(x => x.OriginalPath).IsRequired();
        b.Property(x => x.TrashPath).IsRequired();

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SettingConfig : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.ToTable("Setting");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).ValueGeneratedNever();
        b.Property(x => x.Value).IsRequired();
    }
}
