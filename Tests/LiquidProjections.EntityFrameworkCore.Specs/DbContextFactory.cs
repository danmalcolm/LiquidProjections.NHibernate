using System;

namespace LiquidProjections.EntityFrameworkCore.Specs
{
    class DbContextFactory : IDbContextFactory
    {
        private Func<TestDbContext> _create;

        public DbContextFactory(Func<TestDbContext> create)
        {
            _create = create;
        }

        public TestDbContext Create()
        {
            return _create();
        }
    }
}