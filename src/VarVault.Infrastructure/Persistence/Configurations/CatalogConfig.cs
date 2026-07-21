using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class PackageConfig : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.ToTable("Package");
        b.HasKey(x => x.Id);

        b.Property(x => x.VarName).IsRequired();
        b.Property(x => x.IdentityKey).IsRequired();
        b.Property(x => x.Creator).IsRequired();
        b.Property(x => x.PackageName).IsRequired();
        b.Property(x => x.VersionToken).IsRequired();

        b.HasIndex(x => x.IdentityKey).IsUnique();
        b.HasIndex(x => x.VarName).IsUnique();
        // Not unique alone — VersionSort collides across creators/packages; the composite orders versions.
        b.HasIndex(x => new { x.Creator, x.PackageName, x.VersionSort });
        b.HasIndex(x => x.IsFavorite);

        // Canonical copy: nullable, ON DELETE SET NULL. Deleting the canonical VarFile nulls this
        // pointer and must NOT delete the Package. A distinct navigation avoids ambiguity with
        // the VarFile→Package (PackageId) relationship.
        b.HasOne(x => x.CanonicalVarFile)
            .WithMany()
            .HasForeignKey(x => x.CanonicalVarFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class VarFileConfig : IEntityTypeConfiguration<VarFile>
{
    public void Configure(EntityTypeBuilder<VarFile> b)
    {
        b.ToTable("VarFile");
        b.HasKey(x => x.Id);

        b.Property(x => x.RelativePath).IsRequired();

        b.HasIndex(x => new { x.RepositoryId, x.RelativePath }).IsUnique();
        b.HasIndex(x => x.PackageId);
        b.HasIndex(x => x.ContentSignature);
        b.HasIndex(x => x.PayloadSignature);
        b.HasIndex(x => x.ContentSignatureNoPath);
        b.HasIndex(x => x.ContentHash).HasFilter("\"ContentHash\" IS NOT NULL");
        b.HasIndex(x => new { x.IngestState, x.LeaseExpiresAt });
        b.HasIndex(x => new { x.RepositoryId, x.SeenGeneration });

        // Owning package: nullable (unparsed names → Unrecognized bucket). Deleting a Package
        // detaches its files rather than destroying physical facts.
        b.HasOne(x => x.Package)
            .WithMany(p => p.VarFiles)
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Repository)
            .WithMany(r => r.VarFiles)
            .HasForeignKey(x => x.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fix/superseded lineage (self references) — never cascade.
        b.HasOne(x => x.FixedFromVarFile)
            .WithMany()
            .HasForeignKey(x => x.FixedFromVarFileId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.SupersededByVarFile)
            .WithMany()
            .HasForeignKey(x => x.SupersededByVarFileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ContentItemConfig : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> b)
    {
        b.ToTable("ContentItem");
        b.HasKey(x => x.Id);

        b.Property(x => x.EntryPath).IsRequired();

        // Keyed on VarFileId (content is physical), NOT PackageId.
        b.HasIndex(x => x.VarFileId);
        b.HasIndex(x => x.Type);

        b.HasOne(x => x.VarFile)
            .WithMany(v => v.ContentItems)
            .HasForeignKey(x => x.VarFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackageContentCountConfig : IEntityTypeConfiguration<PackageContentCount>
{
    public void Configure(EntityTypeBuilder<PackageContentCount> b)
    {
        b.ToTable("PackageContentCount");
        b.HasKey(x => new { x.PackageId, x.Type });

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
