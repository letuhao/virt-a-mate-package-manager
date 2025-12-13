using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data.Configurations;

public class DependencyConfiguration : IEntityTypeConfiguration<Dependency>
{
    public void Configure(EntityTypeBuilder<Dependency> builder)
    {
        builder.ToTable("dependencies");
        
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(d => d.VarPackageId)
            .HasColumnName("var_package_id")
            .IsRequired();
        
        builder.Property(d => d.DependencyName)
            .HasColumnName("dependency_name")
            .HasMaxLength(500)
            .IsRequired();
        
        builder.Property(d => d.ResolvedVarPackageId)
            .HasColumnName("resolved_var_package_id");
        
        builder.Property(d => d.VersionConstraint)
            .HasColumnName("version_constraint")
            .HasMaxLength(50);
        
        builder.Property(d => d.IsOptional)
            .HasColumnName("is_optional")
            .HasDefaultValue(false)
            .IsRequired();
        
        builder.Property(d => d.IsResolved)
            .HasColumnName("is_resolved")
            .HasDefaultValue(false)
            .IsRequired();
        
        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        // Indexes
        builder.HasIndex(d => d.VarPackageId)
            .HasDatabaseName("idx_dependencies_var_package");
        
        builder.HasIndex(d => d.ResolvedVarPackageId)
            .HasDatabaseName("idx_dependencies_resolved");
        
        builder.HasIndex(d => d.DependencyName)
            .HasDatabaseName("idx_dependencies_name");
        
        builder.HasIndex(d => d.IsResolved)
            .HasDatabaseName("idx_dependencies_resolved_flag");
        
        // Partial index for unresolved dependencies (performance optimization)
        builder.HasIndex(d => d.IsResolved)
            .HasFilter("\"is_resolved\" = false")
            .HasDatabaseName("idx_dependencies_unresolved");
        
        builder.HasIndex(d => new { d.VarPackageId, d.DependencyName })
            .IsUnique()
            .HasDatabaseName("uk_dependencies_var_package_name");
        
        // Relationships
        builder.HasOne(d => d.VarPackage)
            .WithMany(v => v.Dependencies)
            .HasForeignKey(d => d.VarPackageId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(d => d.ResolvedVarPackage)
            .WithMany()
            .HasForeignKey(d => d.ResolvedVarPackageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

