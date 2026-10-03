using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZmatchApi.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(120);
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasIndex(c => new { c.ClubId, c.Phone }).IsUnique();

        builder.HasOne(c => c.Club)
            .WithMany()
            .HasForeignKey(c => c.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// El constraint de exclusión anti-superposición no es modelable en EF:
/// se crea con SQL en la migración InitialCreate (spec §5.3).
/// </summary>
public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.Property(b => b.Price).HasPrecision(12, 2);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.CancelReason).HasMaxLength(300);
        builder.ToTable(t => t.HasCheckConstraint("ck_bookings_range", "ends_at > starts_at"));

        builder.HasIndex(b => new { b.ClubId, b.StartsAt });
        builder.HasIndex(b => new { b.CustomerId, b.StartsAt });

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(b => b.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Court)
            .WithMany()
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Customer)
            .WithMany()
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.Property(b => b.Reason).HasMaxLength(200);
        builder.ToTable(t => t.HasCheckConstraint("ck_blocks_range", "ends_at > starts_at"));

        builder.HasIndex(b => new { b.CourtId, b.StartsAt });

        builder.HasOne(b => b.Court)
            .WithMany()
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
