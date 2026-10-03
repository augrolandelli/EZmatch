using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZmatchApi.Data.Configurations;

public class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(120);
        builder.Property(c => c.Slug).HasMaxLength(60);
        builder.Property(c => c.Address).HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.TimeZone).HasMaxLength(60);
        builder.Property(c => c.BotInstructions).HasMaxLength(2000);

        builder.HasIndex(c => c.Slug).IsUnique();
        // Un inbox de Chatwoot pertenece a un único club.
        builder.HasIndex(c => c.ChatwootInboxId).IsUnique();
    }
}

public class CourtConfiguration : IEntityTypeConfiguration<Court>
{
    public void Configure(EntityTypeBuilder<Court> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(60);
        builder.Property(c => c.Sport).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(c => c.Club)
            .WithMany(c => c.Courts)
            .HasForeignKey(c => c.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SlotTemplateConfiguration : IEntityTypeConfiguration<SlotTemplate>
{
    public void Configure(EntityTypeBuilder<SlotTemplate> builder)
    {
        builder.Property(s => s.Price).HasPrecision(12, 2);
        builder.ToTable(t => t.HasCheckConstraint("ck_slot_templates_duration", "duration_minutes > 0"));

        builder.HasIndex(s => new { s.CourtId, s.DayOfWeek, s.StartTime }).IsUnique();

        builder.HasOne(s => s.Court)
            .WithMany(c => c.SlotTemplates)
            .HasForeignKey(s => s.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
