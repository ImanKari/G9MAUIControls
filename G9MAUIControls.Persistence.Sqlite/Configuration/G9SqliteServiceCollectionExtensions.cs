using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Registers the persistence layer.
/// </summary>
public static class G9SqliteServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the SQLite persistence layer and returns the collection for chaining.
    ///     <para>
    ///         Registers the frozen <see cref="G9SqliteOptions" />, the connection provider, and the three
    ///         ambient services the layer resolves from the container:
    ///         <see cref="IG9SqliteDatabaseLocator" />, <see cref="IG9Clock" /> and
    ///         <see cref="IG9CurrentUserProvider" /> — each being whatever the builder settled on, default or
    ///         configured.
    ///     </para>
    ///     <para>
    ///         <b>Repositories are not registered.</b> <c>SqliteRepository&lt;T&gt;</c> is
    ///         <c>[RequiresUnreferencedCode]</c> (ADR-0014), so registering an open generic would put a
    ///         trim-unsafe activation behind a container call where no analyzer can see it and no consumer can
    ///         suppress it with a reason. Construct repositories explicitly, in your own registry/service type, and
    ///         put the <c>[UnconditionalSuppressMessage]</c> there with a written reason — that way the
    ///         suppression sits next to the code that knows why it is safe.
    ///     </para>
    ///     <example>
    ///         <code>
    ///         services.AddG9Sqlite(sqlite =>
    ///         {
    ///             sqlite.UseDatabaseLocator(new G9PerUserDatabaseLocator(() => session.UserId))
    ///                   .UseClock(new AppClock())
    ///                   .UseCurrentUserProvider(new SignedInUser(session))
    ///                   .AddMigration&lt;Migration_001&gt;()
    ///                   .Entity&lt;Sample&gt;(e => e
    ///                       .HasGuidId(x => x.SamplingId)
    ///                       .SoftDelete(x => x.IsDeleted)
    ///                       .AlwaysFilter(x => !x.IsDeleted)
    ///                       .Index(x => x.SamplingId)
    ///                       .Cache(G9CachePolicy.Debounced()))
    ///                   .AddInterceptor&lt;SyncMetadataInterceptor&gt;();
    ///         });
    ///         </code>
    ///     </example>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    ///     Configures the layer. Called immediately, so anything it captures must already exist — resolve
    ///     services through a closure over the provider rather than capturing instances, if they are
    ///     themselves DI-registered.
    /// </param>
    public static IServiceCollection AddG9Sqlite(this IServiceCollection services, Action<G9SqliteBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new G9SqliteBuilder();
        configure(builder);
        var options = builder.Build();

        services.AddSingleton(options);

        // The three ambient services, projected out of the frozen options into the container.
        //
        // This is not convenience. G9SqliteConnectionProvider takes IG9SqliteDatabaseLocator as a
        // CONSTRUCTOR parameter, so without these registrations the first resolve throws
        // "Unable to resolve service for type 'IG9SqliteDatabaseLocator' while attempting to activate
        // 'G9SqliteConnectionProvider'" — at runtime, on the first database touch, in a consumer that
        // configured everything correctly. The builder had already settled all three (with documented
        // defaults); they simply never reached DI. See LES-0017.
        //
        // TryAdd, not Add: a consumer may have registered its own locator or clock before calling this —
        // typically because something else in the app needs the same instance — and its registration must
        // win over the one the builder defaulted to.
        services.TryAddSingleton(options.DatabaseLocator);
        services.TryAddSingleton(options.Clock);
        services.TryAddSingleton(options.CurrentUser);

        // Singleton on purpose: it owns the one live SQLiteAsyncConnection and swaps it when the locator's
        // path changes. Scoped or transient would mean several connections to the same file, which is how a
        // "database is locked" storm starts.
        services.AddSingleton<G9SqliteConnectionProvider>();

        return services;
    }
}
