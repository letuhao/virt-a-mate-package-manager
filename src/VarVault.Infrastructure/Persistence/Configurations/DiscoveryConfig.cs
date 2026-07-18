using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class TagConfig : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tag");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.NameKey).IsRequired();
        b.HasIndex(x => x.NameKey).IsUnique();
    }
}

internal sealed class PackageTagConfig : IEntityTypeConfiguration<PackageTag>
{
    public void Configure(EntityTypeBuilder<PackageTag> b)
    {
        b.ToTable("PackageTag");
        b.HasKey(x => new { x.PackageId, x.TagId });

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tag)
            .WithMany(t => t.PackageTags)
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CollectionConfig : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> b)
    {
        b.ToTable("Collection");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired();
    }
}

internal sealed class CollectionMemberConfig : IEntityTypeConfiguration<CollectionMember>
{
    public void Configure(EntityTypeBuilder<CollectionMember> b)
    {
        b.ToTable("CollectionMember");
        b.HasKey(x => new { x.CollectionId, x.PackageId });

        b.HasOne(x => x.Collection)
            .WithMany(c => c.Members)
            .HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContentItemPrefConfig : IEntityTypeConfiguration<ContentItemPref>
{
    public void Configure(EntityTypeBuilder<ContentItemPref> b)
    {
        b.ToTable("ContentItemPref");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.ContentItemId).IsUnique();

        b.HasOne(x => x.ContentItem)
            .WithMany()
            .HasForeignKey(x => x.ContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
