using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        builder.ToTable("installations");
        
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(i => i.VarPackageId)
            .HasColumnName("var_package_id")
            .IsRequired();
        
        builder.Property(i => i.InstallationTargetId)
            .HasColumnName("installation_target_id")
            .IsRequired();
        
        builder.Property(i => i.SymlinkPath)
            .HasColumnName("symlink_path")
            .IsRequired();
        
        builder.Property(i => i.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true)
            .IsRequired();
        
        builder.Property(i => i.InstalledAt)
            .HasColumnName("installed_at")
            .IsRequired();
        
        builder.Property(i => i.InstalledBy)
            .HasColumnName("installed_by")
            .HasMaxLength(255);
        
        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        
        // Indexes
        builder.HasIndex(i => i.VarPackageId)
            .HasDatabaseName("idx_installations_var_package");
        
        builder.HasIndex(i => i.InstallationTargetId)
            .HasDatabaseName("idx_installations_target");
        
        builder.HasIndex(i => i.SymlinkPath)
            .IsUnique()
            .HasDatabaseName("uk_installations_symlink_path");
        
        builder.HasIndex(i => new { i.VarPackageId, i.InstallationTargetId })
            .IsUnique()
            .HasDatabaseName("uk_installations_var_package_target");
        
        builder.HasIndex(i => i.InstalledAt)
            .HasDatabaseName("idx_installations_installed_at");
        
        // Composite index for queries filtering by target and enabled status
        builder.HasIndex(i => new { i.InstallationTargetId, i.IsEnabled })
            .HasDatabaseName("idx_installations_target_enabled");
        
        // Relationships
        builder.HasOne(i => i.VarPackage)
            .WithMany(v => v.Installations)
            .HasForeignKey(i => i.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(i => i.InstallationTarget)
            .WithMany(t => t.Installations)
            .HasForeignKey(i => i.InstallationTargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

