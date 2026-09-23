using System;
using DSoft.EntityFrameworkCore.GDPR.Auditing;
using DSoft.EntityFrameworkCore.GDPR.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Turns on GDPR support for a context.</summary>
public static class GdprDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Reads the GDPR attributes on entity classes into the model and enables the personal data operations
    /// available from <c>context.PersonalData()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;ShopContext&gt;(options => options
    ///     .UseSqlServer(connectionString)
    ///     .UseGdpr(gdpr => gdpr.UseHashKey(hashKey)));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseGdpr(this DbContextOptionsBuilder optionsBuilder, Action<GdprOptionsBuilder>? configure = null)
    {
        if (optionsBuilder == null)
            throw new ArgumentNullException(nameof(optionsBuilder));

        var options = new GdprOptions();
        configure?.Invoke(new GdprOptionsBuilder(options));

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new GdprOptionsExtension(options));

        if (options.ChangeObservers.Count > 0)
            optionsBuilder.AddInterceptors(new PersonalDataChangeInterceptor(options.ChangeObservers));

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseGdpr(DbContextOptionsBuilder, Action{GdprOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseGdpr<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<GdprOptionsBuilder>? configure = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseGdpr((DbContextOptionsBuilder)optionsBuilder, configure);
}
