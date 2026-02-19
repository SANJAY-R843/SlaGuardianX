namespace SlaGuardianX.Data
{
    using Microsoft.EntityFrameworkCore;
    using SlaGuardianX.Models;

    /// <summary>
    /// Entity Framework Core database context for SLA Guardian X.
    /// Manages all database operations and entities.
    /// </summary>
    public class AppDbContext : DbContext
    {
        public DbSet<NetworkMetric> NetworkMetrics { get; set; }
        public DbSet<SlaResult> SlaResults { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Default SQLite configuration
                string dbPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "SlaGuardianX",
                    "sla_guardian.db");

                // Ensure directory exists
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath));

                optionsBuilder.UseSqlite($"Data Source={dbPath}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // NetworkMetric configuration
            modelBuilder.Entity<NetworkMetric>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Timestamp).IsRequired();
                entity.Property(e => e.Bandwidth).IsRequired();
                entity.Property(e => e.Latency).IsRequired();
                entity.Property(e => e.PacketLoss).IsRequired();
                entity.Property(e => e.Uptime).IsRequired();
            });

            // SlaResult configuration
            modelBuilder.Entity<SlaResult>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Timestamp).IsRequired();
                entity.Property(e => e.GuaranteedBandwidth).IsRequired();
                entity.Property(e => e.CurrentBandwidth).IsRequired();
                entity.Property(e => e.CompliancePercentage).IsRequired();
                entity.Property(e => e.IsViolated).IsRequired();
                entity.Property(e => e.RiskScore).IsRequired();
            });
        }
    }
}
