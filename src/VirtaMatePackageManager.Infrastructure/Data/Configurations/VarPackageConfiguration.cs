using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class VarPackageConfiguration : IEntityTypeConfiguration<VarPackage>
{
    public void Configure(EntityTypeBuilder<VarPackage> builder)
    {
        builder.ToTable("var_packages");
        
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(v => v.RepositoryId)
            .HasColumnName("repository_id")
            .IsRequired();
        
        builder.Property(v => v.VarName)
            .HasColumnName("var_name")
            .HasMaxLength(500)
            .IsRequired();
        
        builder.Property(v => v.CreatorName)
            .HasColumnName("creator_name")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(v => v.PackageName)
            .HasColumnName("package_name")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(v => v.Version)
            .HasColumnName("version")
            .HasMaxLength(50)
            .IsRequired();
        
        builder.Property(v => v.FilePath)
            .HasColumnName("file_path")
            .IsRequired();
        
        builder.Property(v => v.FileSize)
            .HasColumnName("file_size")
            .IsRequired();
        
        builder.Property(v => v.FileHash)
            .HasColumnName("file_hash")
            .HasMaxLength(64);
        
        builder.Property(v => v.RelativePath)
            .HasColumnName("relative_path");
        
        // Metadata fields
        builder.Property(v => v.LicenseType)
            .HasColumnName("license_type")
            .HasMaxLength(50);
        
        builder.Property(v => v.Description)
            .HasColumnName("description");
        
        builder.Property(v => v.Credits)
            .HasColumnName("credits");
        
        builder.Property(v => v.Instructions)
            .HasColumnName("instructions");
        
        builder.Property(v => v.PromotionalLink)
            .HasColumnName("promotional_link");
        
        builder.Property(v => v.ProgramVersion)
            .HasColumnName("program_version")
            .HasMaxLength(50);
        
        builder.Property(v => v.PreviewImagePath)
            .HasColumnName("preview_image_path");
        
        // Timestamps
        builder.Property(v => v.FileCreatedAt)
            .HasColumnName("file_created_at");
        
        builder.Property(v => v.FileModifiedAt)
            .HasColumnName("file_modified_at");
        
        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        
        builder.Property(v => v.LastScannedAt)
            .HasColumnName("last_scanned_at");
        
        // UNIQUE constraint on var_name (globally unique)
        builder.HasIndex(v => v.VarName)
            .IsUnique()
            .HasDatabaseName("uk_var_name");
        
        // UNIQUE constraint on repository_id + file_path
        builder.HasIndex(v => new { v.RepositoryId, v.FilePath })
            .IsUnique()
            .HasDatabaseName("uk_var_package_repo_path");
        
        // Indexes
        builder.HasIndex(v => v.CreatorName)
            .HasDatabaseName("idx_var_packages_creator");
        
        builder.HasIndex(v => v.PackageName)
            .HasDatabaseName("idx_var_packages_package");
        
        builder.HasIndex(v => v.Version)
            .HasDatabaseName("idx_var_packages_version");
        
        builder.HasIndex(v => v.RepositoryId)
            .HasDatabaseName("idx_var_packages_repository");
        
        builder.HasIndex(v => new { v.CreatorName, v.PackageName, v.Version })
            .HasDatabaseName("idx_var_packages_composite");
        
        builder.HasIndex(v => v.UpdatedAt)
            .HasDatabaseName("idx_var_packages_updated");
        
        // Index on file_hash for duplicate detection and content matching
        builder.HasIndex(v => v.FileHash)
            .HasDatabaseName("idx_var_packages_file_hash");
        
        // Index on file_modified_at for change detection during scanning
        builder.HasIndex(v => v.FileModifiedAt)
            .HasDatabaseName("idx_var_packages_file_modified");
        
        // Composite index for search queries (creator + package)
        builder.HasIndex(v => new { v.CreatorName, v.PackageName })
            .HasDatabaseName("idx_var_packages_creator_package");
        
        // Relationships
        builder.HasOne(v => v.Repository)
            .WithMany(r => r.VarPackages)
            .HasForeignKey(v => v.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasMany(v => v.Dependencies)
            .WithOne(d => d.VarPackage)
            .HasForeignKey(d => d.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasMany(v => v.ContentItems)
            .WithOne(c => c.VarPackage)
            .HasForeignKey(c => c.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasMany(v => v.Installations)
            .WithOne(i => i.VarPackage)
            .HasForeignKey(i => i.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

