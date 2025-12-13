using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("content_items");
        
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(c => c.VarPackageId)
            .HasColumnName("var_package_id")
            .IsRequired();
        
        builder.Property(c => c.ContentType)
            .HasColumnName("content_type")
            .HasConversion<int>()
            .IsRequired();
        
        builder.Property(c => c.Path)
            .HasColumnName("relative_path")
            .IsRequired();
        
        builder.Property(c => c.IsPreset)
            .HasColumnName("is_preset")
            .HasDefaultValue(false)
            .IsRequired();
        
        builder.Property(c => c.PreviewImagePath)
            .HasColumnName("preview_image_path");
        
        builder.Property(c => c.FileSize)
            .HasColumnName("file_size");
        
        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        // Indexes
        builder.HasIndex(c => c.VarPackageId)
            .HasDatabaseName("idx_content_items_var_package");
        
        builder.HasIndex(c => c.ContentType)
            .HasDatabaseName("idx_content_items_type");
        
        builder.HasIndex(c => new { c.VarPackageId, c.Path })
            .IsUnique()
            .HasDatabaseName("uk_content_items_var_package_path");
        
        // Relationships
        builder.HasOne(c => c.VarPackage)
            .WithMany(v => v.ContentItems)
            .HasForeignKey(c => c.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

