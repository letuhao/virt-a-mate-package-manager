using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class ScanRunConfig : IEntityTypeConfiguration<ScanRun>
{
    public void Configure(EntityTypeBuilder<ScanRun> b)
    {
        b.ToTable("ScanRun");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.PublicId).IsUnique();
        b.HasIndex(x => new { x.RepositoryId, x.Generation });
        b.HasIndex(x => x.Phase);
    }
}

internal sealed class DirtyPackageConfig : IEntityTypeConfiguration<DirtyPackage>
{
    public void Configure(EntityTypeBuilder<DirtyPackage> b)
    {
        b.ToTable("DirtyPackage");
        b.HasKey(x => x.PackageId);
        b.HasIndex(x => x.MarkedAt);
    }
}
