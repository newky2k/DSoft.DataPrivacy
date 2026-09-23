using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DSoft.EntityFrameworkCore.GDPR.Metadata;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DSoft.EntityFrameworkCore.GDPR.Design;

/// <summary>
/// Keeps the GDPR annotations out of migration snapshots and designer files. They describe how data is handled,
/// not the schema, so migrations never need them. The package registers this for you through
/// <see cref="DesignTimeServicesReferenceAttribute"/>; set the MSBuild property <c>GdprDesignTimeServices</c>
/// to <c>false</c> to keep the annotations in snapshots instead.
/// </summary>
public sealed class GdprDesignTimeServices : IDesignTimeServices
{
    private const string AnnotationCodeGenerator = "Microsoft.EntityFrameworkCore.Design.IAnnotationCodeGenerator";

    private const string SnapshotGeneratorDependencies =
        "Microsoft.EntityFrameworkCore.Migrations.Design.CSharpSnapshotGeneratorDependencies, Microsoft.EntityFrameworkCore.Design";

    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        // The tools configure referenced services before the provider's, so the provider's annotation code
        // generator is not registered yet. Wrap it where the snapshot generator receives it, at resolution time.
        var dependenciesType = Type.GetType(SnapshotGeneratorDependencies, throwOnError: false);
        if (dependenciesType == null)
            return;

        var existing = serviceCollection.LastOrDefault(d => d.ServiceType == dependenciesType);
        if (existing != null)
            serviceCollection.Remove(existing);

        serviceCollection.Add(new ServiceDescriptor(
            dependenciesType,
            provider => CreateDependencies(provider, dependenciesType),
            existing?.Lifetime ?? ServiceLifetime.Singleton));
    }

    private static object CreateDependencies(IServiceProvider provider, Type dependenciesType)
    {
        var constructor = dependenciesType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters()
            .Select(p =>
            {
                var service = provider.GetRequiredService(p.ParameterType);
                return p.ParameterType.FullName == AnnotationCodeGenerator ? AnnotationFilter.Wrap(p.ParameterType, service) : service;
            })
            .ToArray();

        return constructor.Invoke(arguments);
    }

    /// <summary>Forwards every call to the provider's generator, dropping GDPR annotations from what it keeps.</summary>
    public class AnnotationFilter : DispatchProxy
    {
        private object _inner = null!;

        internal static object Wrap(Type interfaceType, object inner)
        {
            var proxy = (AnnotationFilter)Create(interfaceType, typeof(AnnotationFilter));
            proxy._inner = inner;
            return proxy;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result;
            try
            {
                result = targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            if (targetMethod.Name == "FilterIgnoredAnnotations" && result is IEnumerable<IAnnotation> annotations)
                return annotations.Where(a => !a.Name.StartsWith(GdprAnnotationNames.Prefix, StringComparison.Ordinal)).ToList();

            return result;
        }
    }
}
