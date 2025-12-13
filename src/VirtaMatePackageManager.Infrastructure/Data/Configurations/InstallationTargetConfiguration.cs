using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class InstallationTargetConfiguration : IEntityTypeConfiguration<InstallationTarget>
{
    public void Configure(EntityTypeBuilder<InstallationTarget> builder)
    {
        builder.ToTable("installation_targets");
        
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(t => t.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(t => t.Path)
            .HasColumnName("path")
            .IsRequired();
        
        builder.Property(t => t.ProfileName)
            .HasColumnName("profile_name")
            .HasMaxLength(100);
        
        builder.Property(t => t.Description)
            .HasColumnName("description");
        
        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false)
            .IsRequired();
        
        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        
        // Indexes
        builder.HasIndex(t => t.Path)
            .IsUnique()
            .HasDatabaseName("uk_installation_targets_path");
        
        builder.HasIndex(t => t.IsActive)
            .HasDatabaseName("idx_installation_targets_active");
        
        builder.HasIndex(t => t.ProfileName)
            .HasDatabaseName("idx_installation_targets_profile");
        
        // Unique constraint: Only one active target at a time
        // This is enforced at application level as EF Core doesn't support partial unique indexes well
        // Will be handled via validation/business logic
        
        // Relationships
        builder.HasMany(t => t.Installations)
            .WithOne(i => i.InstallationTarget)
            .HasForeignKey(i => i.InstallationTargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

