
using Microsoft.EntityFrameworkCore;

namespace LiquidProjections.EntityFrameworkCore
{
    public sealed class EntityFrameworkCoreProjectionContext : ProjectionContext
    {
        private bool wasHandled;
        
        public DbContext Session { get; set; }

        /// <summary>
        /// Indicates that at least one event in the current batch was mapped in the event map and thus was handled by the
        /// projector.
        /// </summary>
        internal bool WasHandled
        {
            get => wasHandled;
            set => wasHandled |= value;
        }
    }
}