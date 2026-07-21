using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class ProfileConfig : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> b)
    {
        b.ToTable("Profile");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ActivationLinkConfig : IEntityTypeConfiguration<ActivationLink>
{
    public void Configure(EntityTypeBuilder<ActivationLink> b)
    {
        b.ToTable("ActivationLink");
        b.HasKey(x => x.Id);
        b.Property(x => x.LinkPath).IsRequired();

        b.HasIndex(x => x.ProfileId);
        b.HasIndex(x => x.VarFileId);

        b.HasOne(x => x.Profile)
            .WithMany(p => p.Links)
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.VarFile)
            .WithMany()
            .HasForeignKey(x => x.VarFileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.RequestedByPreset)
            .WithMany()
            .HasForeignKey(x => x.RequestedByPresetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class LoadingPresetConfig : IEntityTypeConfiguration<LoadingPreset>
{
    public void Configure(EntityTypeBuilder<LoadingPreset> b)
    {
        b.ToTable("LoadingPreset");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();

        b.HasOne(x => x.Profile)
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PresetMemberConfig : IEntityTypeConfiguration<PresetMember>
{
    public void Configure(EntityTypeBuilder<PresetMember> b)
    {
        b.ToTable("PresetMember");
        b.HasKey(x => x.Id);

        b.Property(x => x.PackageRefKey).IsRequired();
        b.HasIndex(x => x.PresetId);
        b.HasIndex(x => new { x.PresetId, x.PackageRefKey }).IsUnique();

        b.HasOne(x => x.Preset)
            .WithMany(p => p.Members)
            .HasForeignKey(x => x.PresetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ResolvedPackage)
            .WithMany()
            .HasForeignKey(x => x.ResolvedPackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class VarAliasConfig : IEntityTypeConfiguration<VarAlias>
{
    public void Configure(EntityTypeBuilder<VarAlias> b)
    {
        b.ToTable("VarAlias");
        b.HasKey(x => x.Id);

        b.Property(x => x.MissingRefKey).IsRequired();
        b.HasIndex(x => x.MissingRefKey);

        b.HasOne(x => x.ResolvedPackage)
            .WithMany()
            .HasForeignKey(x => x.ResolvedPackageId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Preset)
            .WithMany()
            .HasForeignKey(x => x.PresetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProfilePackageLinkConfig : IEntityTypeConfiguration<ProfilePackageLink>
{
    public void Configure(EntityTypeBuilder<ProfilePackageLink> b)
    {
        b.ToTable("ProfilePackageLink");
        b.HasKey(x => new { x.ProfileId, x.PackageId });

        b.HasIndex(x => x.PackageId);
        b.HasIndex(x => new { x.ProfileId, x.InstalledAt });

        b.HasOne(x => x.Profile)
            .WithMany(p => p.PackageLinks)
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
