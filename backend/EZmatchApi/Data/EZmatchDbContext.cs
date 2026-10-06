using EZmatchApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Data;

/// <summary>Contexto EF Core de EZmatch (PostgreSQL, snake_case).</summary>
public class EZmatchDbContext(DbContextOptions<EZmatchDbContext> options) : DbContext(options)
{
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<SlotTemplate> SlotTemplates => Set<SlotTemplate>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Necesaria para el constraint de exclusión (uuid con = dentro de un índice GiST).
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EZmatchDbContext).Assembly);
    }
}
