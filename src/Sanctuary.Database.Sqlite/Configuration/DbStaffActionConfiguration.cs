using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbStaffActionConfiguration : IEntityTypeConfiguration<DbStaffAction>
{
    public void Configure(EntityTypeBuilder<DbStaffAction> builder)
    {
        // History: no foreign keys, so a row outlives the account or character it names.
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).IsRequired().ValueGeneratedOnAdd();

        builder.HasIndex(a => new { a.TargetUserId, a.Created });
        builder.HasIndex(a => a.Created);

        builder.Property(a => a.Created).IsRequired();

        builder.Property(a => a.StaffUserId).IsRequired();
        builder.Property(a => a.StaffName).IsRequired().HasMaxLength(DbStaffAction.MaxStaffNameLength);
        builder.Property(a => a.Command).IsRequired().HasMaxLength(DbStaffAction.MaxCommandLength);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(DbStaffAction.MaxActionLength);

        builder.Property(a => a.TargetUserId).IsRequired(false);
        builder.Property(a => a.TargetCharacterId).IsRequired(false);
        builder.Property(a => a.TargetName).IsRequired(false).HasMaxLength(DbStaffAction.MaxTargetNameLength);
        builder.Property(a => a.Details).IsRequired(false).HasMaxLength(DbStaffAction.MaxDetailsLength);
    }
}
