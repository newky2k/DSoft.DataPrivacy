using DSoft.EntityFrameworkCore.GDPR;
using DSoft.EntityFrameworkCore.GDPR.Erasure;
using DSoft.EntityFrameworkCore.GDPR.Retention;
using DSoft.EntityFrameworkCore.GDPR.Rules;
using GdprSample;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Keep the key in a secret store in a real application, and never change it: old hashes stop matching.
var hashKey = Convert.FromBase64String(builder.Configuration["Gdpr:HashKey"] ?? Convert.ToBase64String(new byte[32]));

var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

builder.Services.AddDbContext<GymContext>(options => options
    .UseSqlite(connection)
    .UseGdpr(gdpr => gdpr.UseHashKey(hashKey)));

builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GymContext>();
    db.Database.EnsureCreated();
    SeedData(db);
}

// Where personal data lives: the start of a record of processing activities.
app.MapGet("/gdpr/inventory", (GymContext db) => Results.Text(db.PersonalData().Model.Inventory().ToCsv(), "text/csv"));

// Problems that would make an export incomplete or an erasure fail.
app.MapGet("/gdpr/issues", (GymContext db) => db.PersonalData().Validate().Select(i => i.ToString()));

// A subject access request: everything held about one member.
app.MapGet("/members/{id:int}/export", async (int id, GymContext db) =>
    await db.PersonalData().ExportAsync<Member>(id) is { } export
        ? Results.Text(export.ToJson(), "application/json")
        : Results.NotFound());

// An erasure request. Add ?dryRun=true to see what would happen first.
app.MapPost("/members/{id:int}/erase", async (int id, bool? dryRun, GymContext db) =>
{
    var result = await db.PersonalData().EraseAsync<Member>(id, new ErasureOptions { DryRun = dryRun ?? false });
    return result.Found ? Results.Ok(result) : Results.NotFound();
});

// The deadline for answering a request received on a given date.
app.MapGet("/gdpr/deadline", (DateOnly received, bool? extended) =>
    DataSubjectRequestDeadline.Calculate(received.ToDateTime(TimeOnly.MinValue), extended ?? false).ToString("yyyy-MM-dd"));

// A retention sweep: visits are kept for two years. Run this from a scheduled job.
app.MapPost("/gdpr/retention/visits", async (GymContext db) =>
{
    var policy = new RetentionPolicy("Attendance", RetentionPeriod.FromYears(2));
    return await db.PersonalData().ApplyRetentionAsync(policy);
});

app.Run();

static void SeedData(GymContext db)
{
    var member = new Member { FullName = "Jo Bloggs", Email = "jo@example.com", DateOfBirth = new DateOnly(1990, 5, 17), PasswordHash = "AQAAAAEAACcQ" };
    db.Members.Add(member);
    db.SaveChanges();

    db.Visits.AddRange(
        new Visit { MemberId = member.Id, Site = "Town Centre", At = DateTime.UtcNow.AddYears(-3) },
        new Visit { MemberId = member.Id, Site = "Riverside", At = DateTime.UtcNow.AddDays(-2) });
    db.Payments.Add(new Payment { MemberId = member.Id, CardholderName = "J Bloggs", Amount = 35m, TakenAt = DateTime.UtcNow.AddMonths(-1) });
    db.Screenings.Add(new HealthScreening { MemberId = member.Id, Answers = "No heart conditions; mild asthma" });
    db.SaveChanges();
}
