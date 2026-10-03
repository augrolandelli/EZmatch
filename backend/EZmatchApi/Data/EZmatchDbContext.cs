using Microsoft.EntityFrameworkCore;

namespace EZmatchApi.Data;

/// <summary>Contexto EF Core de EZmatch (PostgreSQL). Las entidades se agregan en Fase 1.</summary>
public class EZmatchDbContext(DbContextOptions<EZmatchDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EZmatchDbContext).Assembly);
    }
}
