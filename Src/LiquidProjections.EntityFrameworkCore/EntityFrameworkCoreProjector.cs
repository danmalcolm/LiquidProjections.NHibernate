using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LiquidProjections.EntityFrameworkCore
{
    /// <summary>
    /// Projects events to projections of type <typeparamref name="TProjection"/> with key of type <typeparamref name="TKey"/>
    /// stored in a database accessed via NHibernate.
    /// Keeps track of its own state stored in the database as <typeparamref name="TState"/>.
    /// Can also have child projectors of type <see cref="IChildProjector"/> which project events
    /// in the same transaction just before the parent projector.
    /// Uses context of type <see cref="EntityFrameworkCoreProjectionContext"/>.
    /// Throws <see cref="ProjectionException"/> when it detects errors in the event handlers.
    /// </summary>
    public sealed class EntityFrameworkCoreProjector<TProjection, TKey, TState>
        where TProjection : class, new()
        where TState : class, IProjectorState, new()
    {
        private readonly Func<DbContext> sessionFactory;
        private readonly EventMapConfigurator<TProjection, TKey> mapConfigurator;
        private int batchSize = 1;
        private string stateKey = typeof(TProjection).Name;
        private HandleException exceptionHandler = (exception, _, __) => Task.FromResult(ExceptionResolution.Abort);

        /// <summary>
        /// Creates a new instance of <see cref="EntityFrameworkCoreProjector{TProjection,TKey,TState}"/>.
        /// </summary>
        /// <param name="sessionFactory">The delegate that creates a new <see cref="ISession"/>.</param>
        /// <param name="mapBuilder">
        /// The <see cref="IEventMapBuilder{TProjection,TKey,TContext}"/>
        /// with already configured handlers for all the required events
        /// but not yet configured how to handle custom actions, projection creation, updating and deletion.
        /// The <see cref="IEventMap{TContext}"/> will be created from it.
        /// </param>
        /// <param name="children">An optional collection of <see cref="IChildProjector"/> which project events
        /// in the same transaction just before the parent projector.</param>
        public EntityFrameworkCoreProjector(
            Func<DbContext> sessionFactory,
            IEventMapBuilder<TProjection, TKey, EntityFrameworkCoreProjectionContext> mapBuilder, Action<TProjection, TKey> setIdentity,
            IEnumerable<IChildProjector> children = null)
        {
            this.sessionFactory = sessionFactory;
            mapConfigurator = new EventMapConfigurator<TProjection, TKey>(mapBuilder, setIdentity, children);
        }

        /// <summary>
        /// How many transactions should be processed together in one database transaction. Defaults to one.
        /// </summary>
        public int BatchSize
        {
            get => batchSize;
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                batchSize = value;
            }
        }


        /// <summary>
        /// The key to store the projector state as <typeparamref name="TState"/>.
        /// </summary>
        public string StateKey
        {
            get => stateKey;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("State key is missing.", nameof(value));
                }

                stateKey = value;
            }
        }

        /// <summary>
        /// A delegate that will be executed when projecting a batch of transactions fails
        /// and which allows the consuming code to decide how to handle the exception. 
        /// </summary>
        public HandleException ExceptionHandler
        {
            get => exceptionHandler;
            set => exceptionHandler = value ?? throw new ArgumentNullException(nameof(value), "Retry policy is missing.");
        }

        /// <summary>
        /// Sets the behavior for when the state of the projector is persisted to the database. 
        /// </summary>
        public PersistStateBehavior PersistStateBehavior { get; set; } = PersistStateBehavior.EveryBatch;

        /// <summary>
        /// Allows enriching the projector state with additional details before the updated state is written to the database.
        /// </summary>
        /// <remarks>
        /// Is called within the scope of the NHibernate transaction that is created by <see cref="Handle"/>.
        /// </remarks>
        public EnrichState<TState> EnrichState { get; set; } = (state, transaction) => {};

        /// <summary>
        /// A cache that can be used to avoid loading projections from the database.
        /// </summary>
        public IProjectionCache<TProjection, TKey> Cache
        {
            get => mapConfigurator.Cache;
            set => mapConfigurator.Cache = value ?? throw new ArgumentNullException(nameof(value), "A cache cannot be null");
        }

        /// <summary>
        /// Defines a filter that can be used to skip certain projections from being updated.
        /// </summary>
        public Predicate<TProjection> Filter
        {
            get => mapConfigurator.Filter;
            set => mapConfigurator.Filter = value ?? throw new ArgumentNullException(nameof(value), "A filter cannot be null");
        }

        /// <summary>
        /// Instructs the projector to project a collection of ordered <paramref name="transactions"/> asynchronously
        /// in batches of the configured size <see cref="BatchSize"/>. Should cancel its work
        /// when the <paramref name="cancellationToken"/> is triggered.
        /// </summary>
        public async Task Handle(IReadOnlyList<Transaction> transactions, CancellationToken cancellationToken)
        {
            if (transactions == null)
            {
                throw new ArgumentNullException(nameof(transactions));
            }

            long? lastCheckpoint = GetLastCheckpoint();
            IEnumerable<Batch<Transaction>> transactionBatches = transactions
                .Where(t => (!lastCheckpoint.HasValue) || (t.Checkpoint > lastCheckpoint))
                .InBatchesOf(BatchSize);

            foreach (Batch<Transaction> batch in transactionBatches)
            {
                await ProjectUnderPolicy(batch.ToList(), batch.IsLast, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ProjectUnderPolicy(IList<Transaction> batch, bool isLastBatchOfPage, CancellationToken cancellationToken, int attempts = 0)
        {
            bool individualRetry = (attempts > 0);
            bool retry = false;
            do
            {
                try
                {
                    attempts++;
                    await ProjectTransactionBatch(batch, isLastBatchOfPage || retry, cancellationToken).ConfigureAwait(false);
                    retry = false;
                }
                catch (ProjectionException exception)
                {
                    ExceptionResolution resolution = await ExceptionHandler(exception, attempts, cancellationToken).ConfigureAwait(false);
                    switch (resolution)
                    {
                        case ExceptionResolution.Abort:
                            throw;

                        case ExceptionResolution.Retry:
                            retry = true;
                            break;
                        
                        case ExceptionResolution.RetryIndividual:
                            if (individualRetry)
                            {
                                throw new InvalidOperationException("You're already retrying individual transactions");
                            }
                            
                            foreach (Transaction transaction in batch)
                            {
                                await ProjectUnderPolicy(new[] {transaction}, true, cancellationToken, attempts);
                            }

                            break;

                        case ExceptionResolution.Ignore:
                            break;
                    }
                }
            }
            while (retry);
        }

        private async Task ProjectTransactionBatch(IList<Transaction> batch, bool isLastBatchOfPage, CancellationToken cancellationToken)
        {
            try
            {
                using (var dbContext = sessionFactory())
                using (var tx = dbContext.Database.BeginTransaction())
                {
                    bool dirty = false;
                    foreach (Transaction transaction in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        dirty |= await ProjectTransaction(transaction, dbContext).ConfigureAwait(false);
                    }

                    if (isLastBatchOfPage 
                        || PersistStateBehavior == PersistStateBehavior.EveryBatch
                        || (dirty && PersistStateBehavior == PersistStateBehavior.DirtyBatch))
                    {
                        StoreLastCheckpoint(dbContext, batch.Last());
                    }
                    dbContext.SaveChanges();
                    tx.Commit();
                }
            }
            catch (OperationCanceledException)
            {
                Cache.Clear();
            }
            catch (ProjectionException projectionException)
            {
                Cache.Clear();
                
                projectionException.Projector = typeof(TProjection).ToString();
                projectionException.SetTransactionBatch(batch);
                throw;
            }
            catch (Exception exception)
            {
                Cache.Clear();

                var projectionException = new ProjectionException("Projector failed to project transaction batch.", exception)
                {
                    Projector = typeof(TProjection).ToString()
                };

                projectionException.SetTransactionBatch(batch);
                throw projectionException;
            }
        }

        private async Task<bool> ProjectTransaction(Transaction transaction, DbContext dbContext)
        {
            bool dirty = false;
            foreach (EventEnvelope eventEnvelope in transaction.Events)
            {
                var context = new EntityFrameworkCoreProjectionContext
                {
                    TransactionId = transaction.Id,
                    Session = dbContext,
                    StreamId = transaction.StreamId,
                    TimeStampUtc = transaction.TimeStampUtc,
                    Checkpoint = transaction.Checkpoint,
                    EventHeaders = eventEnvelope.Headers,
                    TransactionHeaders = transaction.Headers
                };

                try
                {
                    await mapConfigurator.ProjectEvent(eventEnvelope.Body, context).ConfigureAwait(false);
                    dirty |= context.WasHandled;
                }
                catch (ProjectionException projectionException)
                {
                    projectionException.TransactionId = transaction.Id;
                    projectionException.CurrentEvent = eventEnvelope;
                    throw;
                }
                catch (Exception exception)
                {
                    throw new ProjectionException("Projector failed to project an event.", exception)
                    {
                        TransactionId = transaction.Id,
                        CurrentEvent = eventEnvelope
                    };
                }
            }

            return dirty;
        }

        private void StoreLastCheckpoint(DbContext session, Transaction transaction)
        {
            try
            {
                TState existingState = session.Find<TState>(StateKey);
                TState state = existingState ?? new TState {Id = StateKey};
                state.Checkpoint = transaction.Checkpoint;
                state.LastUpdateUtc = DateTime.UtcNow;

                if (existingState == null)
                {
                    session.Add(state);
                }
                
                EnrichState(state, transaction);
            }
            catch (Exception exception)
            {
                throw new ProjectionException("Projector failed to store last checkpoint.", exception);
            }
        }

        /// <summary>
        /// Determines the checkpoint of the last projected transaction.
        /// </summary>
        public long? GetLastCheckpoint()
        {
            using (var session = sessionFactory())
            {
                return session.Find<TState>(StateKey)?.Checkpoint;
            }
        }
    }

    /// <summary>
    /// Defines a predicate to filter projections processed through <see cref="EntityFrameworkCoreProjector{TProjection,TKey,TState}.Filter"/>
    /// </summary>
    /// <returns>
    /// Returns <c>true</c> if the projector should update or delete a projection. Should return <c>false</c> otherwise.
    /// </returns>
    public delegate bool Predicate<in TProjection>(TProjection projection);

    /// <summary>
    /// A delegate that can be implemented to retry projecting a batch of transactions when it fails.
    /// </summary>
    /// <returns>Returns true if the projector should retry to project the batch of transactions, false if it shoud fail with the specified exception.</returns>
    /// <param name="exception">
    /// The exception that occured that caused this batch to fail. Notice that the batch of exceptions is exposed through
    /// <see cref="ProjectionException.TransactionBatch"/>.
    /// </param>
    /// <param name="attempts">
    /// Number of attempts that were made to project this batch of transactions (includes the one that raised the exception).
    /// </param>
    /// <param name="cancellationToken">
    /// Is requested when the consuming system has canceled the subscription. 
    /// </param>
    public delegate Task<ExceptionResolution> HandleException(ProjectionException exception, int attempts, CancellationToken cancellationToken);

    /// <summary>
    /// Defines the behavior in case the <see cref="EntityFrameworkCoreProjector{TProjection,TKey,TState}"/> throws an exception.
    /// </summary>
    public enum ExceptionResolution
    {
        /// <summary>
        /// Ignore the exception and continue with the next batch of <see cref="Transaction"/>s.
        /// </summary>
        Ignore,
        
        /// <summary>
        /// Abort the projection attempt and re-throw the original exception back to the caller.
        /// </summary>
        Abort,
        
        /// <summary>
        /// Retry the entire batch of <see cref="Transaction"/>s.
        /// </summary>
        Retry,
        
        /// <summary>
        /// Retry each <see cref="Transaction"/> one by one, in their own NHIbernate transaction.
        /// This allows you to trace the exception to an individual exception. 
        /// </summary>
        RetryIndividual
    }
    /// <summary>
    /// Defines the signature of a method that can be used to update the projection state as explained 
    /// in <see cref="EntityFrameworkCoreProjector{TProjection,TKey,TState}.EnrichState"/>.
    /// </summary>
    public delegate void EnrichState<in TState>(TState state, Transaction transaction)
        where TState : IProjectorState;
}