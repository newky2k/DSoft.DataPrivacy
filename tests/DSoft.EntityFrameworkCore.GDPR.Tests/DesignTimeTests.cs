using System;
using DSoft.EntityFrameworkCore.GDPR.Tests.TestModel;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// What the package's buildTransitive targets add to a consuming project.
[assembly: Microsoft.EntityFrameworkCore.Design.DesignTimeServicesReference(
    "DSoft.EntityFrameworkCore.GDPR.Design.GdprDesignTimeServices, DSoft.EntityFrameworkCore.GDPR")]

namespace DSoft.EntityFrameworkCore.GDPR.Tests;

#pragma warning disable EF1001 // DesignTimeServicesBuilder is what the dotnet-ef tool uses.

public sealed class DesignTimeTests
{
    [Fact]
    public void Migration_snapshots_leave_out_the_gdpr_annotations()
    {
        using var database = new ShopDatabase();
        using var context = database.CreateContext();

        var assembly = typeof(DesignTimeTests).Assembly;
        var services = new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(null), Array.Empty<string>()).Build(context);
        var migration = services.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Tests");

        Assert.Contains("Customers", migration.SnapshotCode);
        Assert.DoesNotContain("Gdpr:", migration.SnapshotCode);
        Assert.DoesNotContain("Gdpr:", migration.MetadataCode);
    }
}
