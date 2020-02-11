using LiquidProjections.EntityFrameworkCore.Specs.EntityFrameworkCoreProjectorSpecs;
using Microsoft.EntityFrameworkCore;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {

        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        }

        public virtual DbSet<ProjectorState> ProjectorStates { get; set; }

        public virtual DbSet<TrackingState> TrackingStates { get; set; }

        public virtual DbSet<ProductCatalogEntry> ProductCatalogEntries { get; set; }

        public virtual DbSet<ProductCatalogChildEntry> ProductCatalogChildEntries { get; set; }

    }
}