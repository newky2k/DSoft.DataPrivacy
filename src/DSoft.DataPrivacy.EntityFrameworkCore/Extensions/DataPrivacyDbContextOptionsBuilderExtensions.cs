using System;
using DSoft.DataPrivacy.EntityFrameworkCore.Auditing;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Turns on data privacy support for a context.</summary>
public static class DataPrivacyDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Reads the data privacy attributes on entity classes into the model and enables the personal data operations
    /// available from <c>context.PersonalData()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;ShopContext&gt;(options => options
    ///     .UseSqlServer(connectionString)
    ///     .UseDataPrivacy(privacy => privacy.UseHashKey(hashKey)));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseDataPrivacy(this DbContextOptionsBuilder optionsBuilder, Action<DataPrivacyOptionsBuilder>? configure = null)
    {
        if (optionsBuilder == null)
            throw new ArgumentNullException(nameof(optionsBuilder));

        var options = new DataPrivacyOptions();
        configure?.Invoke(new DataPrivacyOptionsBuilder(options));

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new DataPrivacyOptionsExtension(options));

        if (options.ChangeObservers.Count > 0)
            optionsBuilder.AddInterceptors(new PersonalDataChangeInterceptor(options.ChangeObservers));

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseDataPrivacy(DbContextOptionsBuilder, Action{DataPrivacyOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseDataPrivacy<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<DataPrivacyOptionsBuilder>? configure = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDataPrivacy((DbContextOptionsBuilder)optionsBuilder, configure);
}
