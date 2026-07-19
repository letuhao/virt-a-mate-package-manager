using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence.Configurations;

internal sealed class ActivityEntryConfig : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> b)
    {
        b.ToTable("ActivityEntry");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.Description).IsRequired();
        b.HasIndex(x => x.TimestampUnixMs);
    }
}
