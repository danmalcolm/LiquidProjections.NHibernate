namespace LiquidProjections.EntityFrameworkCore.Specs
{
    public interface IDbContextFactory
    {
        TestDbContext Create();
    }
}