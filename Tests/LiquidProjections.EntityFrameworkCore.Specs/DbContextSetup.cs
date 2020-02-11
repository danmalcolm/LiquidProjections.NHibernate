using System;
using Microsoft.EntityFrameworkCore;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    internal sealed class DbContextSetup
    {
        private const string ConnectionString = "Data Source=LiquidProjections.EntityFrameworkCore.Specs.db";

        public DbContextContainer Build()
        {
            Guid databaseId = Guid.NewGuid();
            
            //var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseId.ToString()).Options;
            string connectionString = $"Data Source={databaseId.ToString()}.db";
            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;

            TestDbContext CreateDbContext() => new TestDbContext(options);
            using (var dbContext = CreateDbContext())
            {
                dbContext.Database.EnsureCreated();
            }
            
            return new DbContextContainer(new DbContextFactory(CreateDbContext));
        }
    }
}