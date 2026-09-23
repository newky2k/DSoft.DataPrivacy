using DSoft.DataPrivacy;
using Microsoft.EntityFrameworkCore;

namespace DataPrivacySample;

// The person every other record resolves to.
[DataSubject]
[PersonalDataEntity(DataClass = "Members", Description = "Gym members")]
public class Member
{
    public int Id { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public string FullName { get; set; } = string.Empty;

    // Hashed on erasure, so the suppression list still recognises the address.
    [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact, Anonymisation = AnonymisationMethod.Hash)]
    public string Email { get; set; } = string.Empty;

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public DateOnly? DateOfBirth { get; set; }

    [PersonalData(PersonalDataCategory.Credential)]
    public string? PasswordHash { get; set; }

    [RetentionTrigger(RetentionTrigger.RelationshipEnded)]
    [NotPersonalData]
    public DateTime? LeftAt { get; set; }

    [AnonymisedAt]
    [NotPersonalData]
    public DateTime? AnonymisedAt { get; set; }
}

// Belongs to the member and goes with them. Found through the required, cascading relationship.
[PersonalDataEntity(DataClass = "Attendance")]
public class Visit
{
    public int Id { get; set; }

    public int MemberId { get; set; }

    [PersonalData(PersonalDataCategory.Location)]
    public string Site { get; set; } = string.Empty;

    [RetentionTrigger]
    [NotPersonalData]
    public DateTime At { get; set; }
}

// Payments are kept for tax records: the row stays, the name on it goes.
[PersonalDataEntity(DataClass = "Payments", Erasure = ErasureAction.Anonymise)]
public class Payment
{
    public int Id { get; set; }

    [DataSubjectKey]
    public int MemberId { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Financial)]
    public string CardholderName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [RetentionTrigger]
    [NotPersonalData]
    public DateTime TakenAt { get; set; }
}

// Health screening answers must be kept under the club's health and safety duty.
[RetainOnErasure(RetentionGround.LegalObligation, Reason = "Health screening records are kept for the insurer")]
public class HealthScreening
{
    public int Id { get; set; }

    [DataSubjectKey]
    public int MemberId { get; set; }

    [PersonalData(PersonalDataCategory.Health | PersonalDataCategory.FreeText)]
    public string Answers { get; set; } = string.Empty;
}

public sealed class GymContext(DbContextOptions<GymContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();

    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<HealthScreening> Screenings => Set<HealthScreening>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Visit>().HasOne<Member>().WithMany().HasForeignKey(v => v.MemberId);
        modelBuilder.Entity<Payment>().HasOne<Member>().WithMany().HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<HealthScreening>().HasOne<Member>().WithMany().HasForeignKey(s => s.MemberId).OnDelete(DeleteBehavior.Restrict);
    }
}
