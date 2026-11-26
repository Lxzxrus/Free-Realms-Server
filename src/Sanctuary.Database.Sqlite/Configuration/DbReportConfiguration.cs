using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbReportConfiguration : IEntityTypeConfiguration<DbReport>
{
    public void Configure(EntityTypeBuilder<DbReport> builder)
    {
        // History: no foreign keys, so a row outlives the account or character it names.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).IsRequired().ValueGeneratedOnAdd();

        builder.HasIndex(r => new { r.Status, r.Created });
        builder.HasIndex(r => new { r.ReporterUserId, r.Created });
        builder.HasIndex(r => r.TargetUserId);

        builder.Property(r => r.Kind).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.Created).IsRequired();

        builder.Property(r => r.ReporterUserId).IsRequired();
        builder.Property(r => r.ReporterCharacterId).IsRequired();
        builder.Property(r => r.ReporterName).IsRequired().HasMaxLength(DbReport.MaxReporterNameLength);

        builder.Property(r => r.TargetName).IsRequired(false).HasMaxLength(DbReport.MaxTargetNameLength);
        builder.Property(r => r.TargetCharacterId).IsRequired(false);
        builder.Property(r => r.TargetUserId).IsRequired(false);
        builder.Property(r => r.HouseId).IsRequired(false);

        builder.Property(r => r.Zone).IsRequired().HasMaxLength(DbReport.MaxZoneLength);
        builder.Property(r => r.Position).IsRequired().HasMaxLength(DbReport.MaxPositionLength);
        builder.Property(r => r.ClientText).IsRequired().HasMaxLength(DbReport.MaxClientTextLength);
        builder.Property(r => r.ServerContext).IsRequired().HasMaxLength(DbReport.MaxServerContextLength);

        builder.Property(r => r.ClosedByUserId).IsRequired(false);
        builder.Property(r => r.ClosedAt).IsRequired(false);
        builder.Property(r => r.CloseNote).IsRequired(false).HasMaxLength(DbReport.MaxCloseNoteLength);
    }
}
