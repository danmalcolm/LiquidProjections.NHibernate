using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NHibernate;

namespace LiquidProjections.EFCore
{
    internal sealed class EventMapConfigurator<TProjection, TKey>
        where TProjection : class, new()
    {
        private readonly Action<TProjection, TKey> setIdentity;
        private readonly IEventMap<EntityFrameworkCoreProjectionContext> map;
        private readonly IEnumerable<IChildProjector> children;
        private IProjectionCache<TProjection, TKey> cache = new PassthroughCache<TProjection, TKey>();

        public EventMapConfigurator(
            IEventMapBuilder<TProjection, TKey, EntityFrameworkCoreProjectionContext> mapBuilder, Action<TProjection, TKey> setIdentity,
            IEnumerable<IChildProjector> children = null)
        {
            this.setIdentity = setIdentity;
            if (mapBuilder == null)
            {
                throw new ArgumentNullException(nameof(mapBuilder));
            }

            map = BuildMap(mapBuilder);
            this.children = children?.ToList() ?? new List<IChildProjector>();
        }

        public IProjectionCache<TProjection, TKey> Cache
        {
            get => cache;
            set => cache = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Predicate<TProjection> Filter { get; set; } = _ => true;

        private IEventMap<EntityFrameworkCoreProjectionContext> BuildMap(
            IEventMapBuilder<TProjection, TKey, EntityFrameworkCoreProjectionContext> mapBuilder)
        {
            return mapBuilder.Build(new ProjectorMap<TProjection, TKey, EntityFrameworkCoreProjectionContext>
            {
                Create = OnCreate,
                Update = OnUpdate,
                Delete = OnDelete,
                Custom = (context, projector) => projector()
            });
        }

        private async Task OnCreate(TKey key, EntityFrameworkCoreProjectionContext context, Func<TProjection, Task> projector, Func<TProjection, bool> shouldOverwrite)
        {
            TProjection projection = await cache.Get(key, () => Task.FromResult(context.Session.Get<TProjection>(key)));
            if ((projection == null) || shouldOverwrite(projection))
            {
                if (projection == null)
                {
                    projection = new TProjection();
                    setIdentity(projection, key);
                    await projector(projection).ConfigureAwait(false);

                    context.Session.Save(projection);
                    cache.Add(projection);
                }
                else
                {
                    // Reattach it to the session
                    // See also https://stackoverflow.com/questions/2932716/nhibernate-correct-way-to-reattach-cached-entity-to-different-session
                    context.Session.Lock(projection, LockMode.None);
                    await projector(projection).ConfigureAwait(false);
                }
            }
        }

        private async Task OnUpdate(TKey key, EntityFrameworkCoreProjectionContext context, Func<TProjection, Task> projector, Func<bool> createIfMissing)
        {
            TProjection projection = await cache.Get(key, () => Task.FromResult(context.Session.Get<TProjection>(key)));
            if ((projection == null) && createIfMissing())
            {
                projection = new TProjection();
                setIdentity(projection, key);

                await projector(projection).ConfigureAwait(false);
                context.Session.Save(projection);
                cache.Add(projection);
            }
            else
            {
                if (projection != null && Filter(projection))
                {
                    context.Session.Lock(projection, LockMode.None);
                    await projector(projection).ConfigureAwait(false);
                }
            }
        }

        private async Task<bool> OnDelete(TKey key, EntityFrameworkCoreProjectionContext context)
        {
            TProjection existingProjection = 
                await cache.Get(key, () => Task.FromResult(context.Session.Get<TProjection>(key)));

            if (existingProjection != null)
            {
                context.Session.Delete(existingProjection);
                cache.Remove(key);

                return true;
            }

            return false;
        }

        public async Task ProjectEvent(object anEvent, EntityFrameworkCoreProjectionContext context)
        {
            foreach (IChildProjector child in children)
            {
                await child.ProjectEvent(anEvent, context).ConfigureAwait(false);
            }

            context.WasHandled = await map.Handle(anEvent, context).ConfigureAwait(false);
        }
    }
}