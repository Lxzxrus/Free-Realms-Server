using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbPlotVoteConfiguration : IEntityTypeConfiguration<DbPlotVote>
{
    public void Configure(EntityTypeBuilder<DbPlotVote> builder)
    {
        // One vote per account per plot per week. HouseId has no foreign key: the votes outlive the house, as history.
        builder.HasKey(v => new { v.HouseId, v.UserId, v.WeekStart });
        builder.HasIndex(v => new { v.WeekStart, v.HouseId });

        builder.Property(v => v.OwnerUserId).IsRequired();
        builder.Property(v => v.CharacterId).IsRequired();
        builder.Property(v => v.Value).IsRequired();
        builder.Property(v => v.Created).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(v => v.Updated).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
