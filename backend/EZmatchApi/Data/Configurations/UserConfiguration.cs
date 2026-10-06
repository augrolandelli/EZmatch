using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZmatchApi.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.Property(u => u.FullName).HasMaxLength(120);
        builder.Property(u => u.PasswordHash).HasMaxLength(200);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.ClubId);

        // Owner y Staff siempre pertenecen a un club; SuperAdmin a ninguno.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_users_club_by_role",
            "(role = 'SuperAdmin' AND club_id IS NULL) OR (role <> 'SuperAdmin' AND club_id IS NOT NULL)"));

        builder.HasOne(u => u.Club)
            .WithMany()
            .HasForeignKey(u => u.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
