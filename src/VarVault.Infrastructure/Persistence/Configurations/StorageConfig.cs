using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryConfig : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> b)
    {
        b.ToTable("Repository");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // app-assigned stable GUID

        b.Property(x => x.Name).IsRequired();
        b.Property(x => x.MountPath).IsRequired();

        b.HasIndex(x => x.MountPath);
        b.HasIndex(x => x.Tier);
    }
}
