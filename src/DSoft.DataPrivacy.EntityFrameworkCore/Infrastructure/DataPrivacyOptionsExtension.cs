using System.Collections.Generic;
using DSoft.DataPrivacy.EntityFrameworkCore.Conventions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;

/// <summary>The options extension added by <c>UseDataPrivacy</c>.</summary>
public sealed class DataPrivacyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Creates the extension with default options.</summary>
    public DataPrivacyOptionsExtension()
        : this(new DataPrivacyOptions())
    {
    }

    internal DataPrivacyOptionsExtension(DataPrivacyOptions options)
    {
        Options = options;
    }

    /// <summary>The configured options.</summary>
    public DataPrivacyOptions Options { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
        => new EntityFrameworkServicesBuilder(services).TryAdd<IConventionSetPlugin, DataPrivacyConventionSetPlugin>();

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using DataPrivacy ";

        // Only the convention plugin is registered, and it does not depend on the options.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["DataPrivacy"] = "1";
    }
}

internal sealed class DataPrivacyConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new PersonalDataAttributeConvention());
        return conventionSet;
    }
}
