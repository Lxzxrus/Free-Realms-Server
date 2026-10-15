using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbCharacterCompletedCollectionConfiguration : IEntityTypeConfiguration<DbCharacterCompletedCollection>
{
    public void Configure(EntityTypeBuilder<DbCharacterCompletedCollection> builder)
    {
        builder.HasKey(completed => new { completed.CollectionId, completed.CharacterId });
        builder.Property(completed => completed.CollectionId).IsRequired().ValueGeneratedNever();
        builder.Property(completed => completed.Created).IsRequired();
    }
}
