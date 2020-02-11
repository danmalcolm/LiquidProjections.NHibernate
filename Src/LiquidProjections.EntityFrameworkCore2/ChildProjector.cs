﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiquidProjections.EFCore
{
    /// <summary>
    /// Projects events to projections of type <typeparamref name="TProjection"/> with key of type <typeparamref name="TKey"/>
    /// stored in a database accessed via NHibernate
    /// just before the parent projector in the same transaction.
    /// Throws <see cref="ProjectionException"/> when it detects errors in the event handlers.
    /// </summary>
    public sealed class ChildProjector<TProjection, TKey> : IChildProjector
        where TProjection : class, new()
    {
        private readonly EventMapConfigurator<TProjection, TKey> mapConfigurator;

        /// <summary>
        /// Creates a new instance of <see cref="ChildProjector{TProjection,TKey}"/>.
        /// </summary>
        /// <param name="mapBuilder">
        /// The <see cref="IEventMapBuilder{TProjection,TKey,TContext}"/>
        /// with already configured handlers for all the required events
        /// but not yet configured how to handle custom actions, projection creation, updating and deletion.
        /// The <see cref="IEventMap{TContext}"/> will be created from it.
        /// </param>
        /// <param name="children">An optional collection of <see cref="IChildProjector"/> which project events
        /// in the same transaction just before the parent projector.</param>
        public ChildProjector(
            IEventMapBuilder<TProjection, TKey, EntityFrameworkCoreProjectionContext> mapBuilder, Action<TProjection, TKey> setIdentity,
            IEnumerable<IChildProjector> children = null)
        {
            mapConfigurator = new EventMapConfigurator<TProjection, TKey>(mapBuilder, setIdentity, children);
        }

        /// <summary>
        /// A cache that can be used to avoid loading projections from the database.
        /// </summary>
        public IProjectionCache<TProjection, TKey> Cache
        {
            get => mapConfigurator.Cache;
            set => mapConfigurator.Cache = value ?? throw new ArgumentNullException(nameof(value), "A cache cannot be null");
        }

        async Task IChildProjector.ProjectEvent(object anEvent, EntityFrameworkCoreProjectionContext context)
        {
            if (anEvent == null)
            {
                throw new ArgumentNullException(nameof(anEvent));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            try
            {
                await mapConfigurator.ProjectEvent(anEvent, context).ConfigureAwait(false);
            }
            catch (ProjectionException projectionException)
            {
                if (string.IsNullOrEmpty(projectionException.ChildProjector))
                {
                    projectionException.ChildProjector = typeof(TProjection).ToString();
                }

                throw;
            }
            catch (Exception exception)
            {
                throw new ProjectionException("Projector failed to project an event.", exception)
                {
                    ChildProjector = typeof(TProjection).ToString()
                };
            }
        }
    }
}