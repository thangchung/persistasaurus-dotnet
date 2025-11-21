using Microsoft.EntityFrameworkCore;

namespace Persistasaurus.Data;

/// <summary>
/// Entity Framework Core DbContext for Persistasaurus execution log.
/// </summary>
public class PersistasaurusDbContext : DbContext
{
    /// <summary>
    /// Gets or sets the execution log entries.
    /// </summary>
    public DbSet<ExecutionLogEntry> ExecutionLog { get; set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistasaurusDbContext"/> class.
    /// </summary>
    public PersistasaurusDbContext()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistasaurusDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public PersistasaurusDbContext(DbContextOptions<PersistasaurusDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=execution_log.db");
        }
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionLogEntry>(entity =>
        {
            entity.HasKey(e => new { e.FlowId, e.Step });
            
            entity.Property(e => e.FlowId).IsRequired();
            entity.Property(e => e.Step).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.ClassName).IsRequired();
            entity.Property(e => e.MethodName).IsRequired();
            entity.Property(e => e.Status).IsRequired()
                .HasConversion<string>();
            entity.Property(e => e.Attempts).IsRequired().HasDefaultValue(1);
        });
    }
}
