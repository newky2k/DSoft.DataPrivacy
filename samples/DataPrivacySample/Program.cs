using DSoft.DataPrivacy;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Retention;
using DSoft.DataPrivacy.Regimes;
using DSoft.DataPrivacy.Rules;
using DataPrivacySample;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Keep the key in a secret store in a real application, and never change it: old hashes stop matching.
var hashKey = Convert.FromBase64String(builder.Configuration["DataPrivacy:HashKey"] ?? Convert.ToBase64String(new byte[32]));

// The law in force for this deployment: "gdpr" or "popia". Run with --DataPrivacy:Regime=popia to switch.
var regime = PrivacyRegimes.Get(builder.Configuration["DataPrivacy:Regime"] ?? "gdpr");

var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

builder.Services.AddDbContext<GymContext>(options => options
    .UseSqlite(connection)
    .UseDataPrivacy(privacy => privacy.UseRegime(regime).UseHashKey(hashKey)));

builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GymContext>();
    db.Database.EnsureCreated();
    SeedData(db);
}

// Where personal data lives: the start of a record of processing activities.
app.MapGet("/privacy/inventory", (GymContext db) => Results.Text(db.PersonalData().Inventory().ToCsv(), "text/csv"));

// Problems that would make an export incomplete or an erasure fail.
app.MapGet("/privacy/issues", (GymContext db) => db.PersonalData().Validate().Select(i => i.ToString()));

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

// The law in force: its terms, the rights it gives and its breach rule.
app.MapGet("/privacy/regime", () => new
{
    regime.Name,
    regime.Legislation,
    regime.ControllerTerm,
    regime.ProcessorTerm,
    Rights = regime.RequestTypes.Select(t => $"{t} ({regime.Cite(t)})"),
    regime.BreachNotificationRule,
});

// When a response to a request received on a given date is due under the law in force.
app.MapGet("/privacy/deadline", (DataSubjectRequestType type, DateOnly received, bool? extended) =>
    regime.RequestDeadline(type, received.ToDateTime(TimeOnly.MinValue), extended ?? false) is DateTime due
        ? Results.Ok(due.ToString("yyyy-MM-dd"))
        : Results.Ok($"{regime.Name} sets no fixed period for {type}; respond as soon as reasonably practicable."));

// A retention sweep: visits are kept for two years. Run this from a scheduled job.
app.MapPost("/privacy/retention/visits", async (GymContext db) =>
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
