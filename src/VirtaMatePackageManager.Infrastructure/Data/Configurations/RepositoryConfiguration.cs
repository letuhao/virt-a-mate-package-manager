using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable("repositories");
        
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(r => r.Path)
            .HasColumnName("path")
            .IsRequired();
        
        builder.Property(r => r.Description)
            .HasColumnName("description");
        
        builder.Property(r => r.Priority)
            .HasColumnName("priority")
            .HasDefaultValue(0)
            .IsRequired();
        
        builder.Property(r => r.Enabled)
            .HasColumnName("enabled")
            .HasDefaultValue(true)
            .IsRequired();
        
        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        
        // Indexes
        builder.HasIndex(r => r.Path)
            .IsUnique()
            .HasDatabaseName("uk_repositories_path");
        
        builder.HasIndex(r => r.Enabled)
            .HasFilter("\"enabled\" = true")
            .HasDatabaseName("idx_repositories_enabled");
        
        builder.HasIndex(r => new { r.Priority, r.Enabled })
            .HasDatabaseName("idx_repositories_priority");
        
        // Relationships
        builder.HasMany(r => r.VarPackages)
            .WithOne(v => v.Repository)
            .HasForeignKey(v => v.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

