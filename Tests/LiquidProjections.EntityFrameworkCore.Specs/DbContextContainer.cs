using System;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    internal sealed class DbContextContainer : IDisposable
    {
        public DbContextContainer(IDbContextFactory factory)
        {
            DbContextFactory = factory;
        }

        public IDbContextFactory DbContextFactory { get; }

        public void Dispose()
        {
            
        }
    }
}