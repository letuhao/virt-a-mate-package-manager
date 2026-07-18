using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class DependencyConfig : IEntityTypeConfiguration<Dependency>
{
    public void Configure(EntityTypeBuilder<Dependency> b)
    {
        b.ToTable("Dependency");
        b.HasKey(x => x.Id);

        b.Property(x => x.DependsOnRefKey).IsRequired();
        b.Property(x => x.DependsOnRefRaw).IsRequired();

        b.HasIndex(x => new { x.VarFileId, x.DependsOnRefKey }).IsUnique();
        b.HasIndex(x => x.DependsOnRefKey);
        b.HasIndex(x => x.ResolvedPackageId);

        b.HasOne(x => x.VarFile)
            .WithMany(v => v.Dependencies)
            .HasForeignKey(x => x.VarFileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ResolvedPackage)
            .WithMany()
            .HasForeignKey(x => x.ResolvedPackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class UserSaveConfig : IEntityTypeConfiguration<UserSave>
{
    public void Configure(EntityTypeBuilder<UserSave> b)
    {
        b.ToTable("UserSave");
        b.HasKey(x => x.Id);
        b.Property(x => x.Path).IsRequired();
        b.HasIndex(x => x.Path).IsUnique();
    }
}

internal sealed class SaveDependencyConfig : IEntityTypeConfiguration<SaveDependency>
{
    public void Configure(EntityTypeBuilder<SaveDependency> b)
    {
        b.ToTable("SaveDependency");
        b.HasKey(x => x.Id);

        b.Property(x => x.DependsOnRefKey).IsRequired();
        b.HasIndex(x => x.DependsOnRefKey);
        b.HasIndex(x => new { x.UserSaveId, x.DependsOnRefKey }).IsUnique();

        b.HasOne(x => x.UserSave)
            .WithMany(u => u.Dependencies)
            .HasForeignKey(x => x.UserSaveId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ResolvedPackage)
            .WithMany()
            .HasForeignKey(x => x.ResolvedPackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
