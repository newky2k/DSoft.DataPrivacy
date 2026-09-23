using System;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// What the package's buildTransitive targets add to a consuming project.
[assembly: Microsoft.EntityFrameworkCore.Design.DesignTimeServicesReference(
    "DSoft.DataPrivacy.EntityFrameworkCore.Design.DataPrivacyDesignTimeServices, DSoft.DataPrivacy.EntityFrameworkCore")]

namespace DSoft.DataPrivacy.Tests;

#pragma warning disable EF1001 // DesignTimeServicesBuilder is what the dotnet-ef tool uses.

public sealed class DesignTimeTests
{
    [Fact]
    public void Migration_snapshots_leave_out_the_privacy_annotations()
    {
        using var database = new ShopDatabase();
        using var context = database.CreateContext();

        var assembly = typeof(DesignTimeTests).Assembly;
        var services = new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(null), Array.Empty<string>()).Build(context);
        var migration = services.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Tests");

        Assert.Contains("Customers", migration.SnapshotCode);
        Assert.DoesNotContain("Privacy:", migration.SnapshotCode);
        Assert.DoesNotContain("Privacy:", migration.MetadataCode);
    }
}
