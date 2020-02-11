using System.Threading.Tasks;

namespace LiquidProjections.EntityFrameworkCore
{
    /// <summary>
    /// Projects events to projections stored in a database accessed via NHibernate
    /// just before the parent projector in the same transaction.
    /// </summary>
    public interface IChildProjector
    {
        /// <summary>
        /// Asynchronously projects event <paramref name="anEvent"/> using context <paramref name="context"/>.
        /// </summary>
        Task ProjectEvent(object anEvent, EntityFrameworkCoreProjectionContext context);
    }
}