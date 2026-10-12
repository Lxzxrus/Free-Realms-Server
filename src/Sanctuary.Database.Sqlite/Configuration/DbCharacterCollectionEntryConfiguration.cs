using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbCharacterCollectionEntryConfiguration : IEntityTypeConfiguration<DbCharacterCollectionEntry>
{
    public void Configure(EntityTypeBuilder<DbCharacterCollectionEntry> builder)
    {
        builder.HasKey(entry => new { entry.EntryId, entry.CharacterId });
        builder.Property(entry => entry.EntryId).IsRequired().ValueGeneratedNever();
        builder.Property(entry => entry.Created).IsRequired();
    }
}
