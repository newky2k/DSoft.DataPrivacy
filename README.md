# GDPRCore

GDPR tooling for Entity Framework Core. You mark which data is personal and what kind it is, using attributes or the fluent API. The library then uses that classification to:

- **inventory** where personal data lives, to feed your record of processing activities (Article 30) and DPIAs
- **export** everything held about a person, for access and portability requests (Articles 15 and 20)
- **erase** a person (Article 17): it deletes what can go, anonymises what other records still depend on, and keeps what an exemption covers, with a log that holds no personal values
- **apply retention policies** in batches (Article 5(1)(e), storage limitation)
- **report changes** to personal data, for a rectification log or audit trail
- **redact logs** by the same categories
- **check coverage** in a unit test, so a new column can't ship without a decision

It works with the UK GDPR and the EU GDPR. It is a tool, not legal advice. Your controller and DPO still decide the lawful bases, exemptions and retention periods. This library applies their decisions the same way every time.

## Packages

| Package | Targets | Use it for |
|---|---|---|
| `DSoft.EntityFrameworkCore.GDPR.Abstractions` | netstandard2.0 | Attributes, categories and pure rules. No EF dependency, so domain and entity libraries can classify their own types. |
| `DSoft.EntityFrameworkCore.GDPR` | net8.0, net10.0 | The EF Core conventions, fluent API, export, erasure, retention and change log. |
| `DSoft.EntityFrameworkCore.GDPR.Redaction` | net8.0, net10.0 | Log redaction through `Microsoft.Extensions.Compliance`. |

## Quick start

Classify the model:

```csharp
[DataSubject]                                         // the person everything resolves to
public class Customer
{
    public int Id { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public string Name { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact,
        Anonymisation = AnonymisationMethod.Hash)]    // erased emails still match a suppression list
    public string Email { get; set; }

    [PersonalData(PersonalDataCategory.Credential)]   // never exported
    public string? PasswordHash { get; set; }

    [NotPersonalData("Loyalty tier code")]
    public string Tier { get; set; }
}

[PersonalDataEntity(DataClass = "Sales", Erasure = ErasureAction.Anonymise)]  // kept for accounting
public class Order
{
    public int Id { get; set; }

    [DataSubjectKey]                                  // this order belongs to the customer
    public int CustomerId { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public string DeliveryName { get; set; }

    public decimal Total { get; set; }
}

[RetainOnErasure(ErasureExemption.PublicHealth, Reason = "Health records retention schedule")]
public class ClinicalNote { /* ... */ }
```

Turn it on:

```csharp
services.AddDbContext<ShopContext>(options => options
    .UseSqlServer(connectionString)
    .UseGdpr(gdpr => gdpr.UseHashKey(hashKey)));      // 32+ secret bytes, kept stable
```

Use it:

```csharp
var gdpr = db.PersonalData();

DataSubjectExport? export = await gdpr.ExportAsync<Customer>(customerId);
string json = export!.ToJson();

ErasureResult preview = await gdpr.EraseAsync<Customer>(customerId, new ErasureOptions { DryRun = true });
ErasureResult result  = await gdpr.EraseAsync<Customer>(customerId);
```

In that model, erasing a customer:

- deletes their consents and other records nothing depends on
- anonymises each order (`DeliveryName` becomes `[erased]`, the total stays)
- keeps the clinical notes under Article 17(3)(c)
- anonymises the customer in place rather than deleting them, because kept records still point at them

[`samples/GdprSample`](samples/GdprSample) is a runnable minimal API that shows every feature.

## Concepts

### Categories

`PersonalDataCategory` is a flags enum. Combine categories to describe a value: an email address is `DirectIdentifier | Contact`, and a clinical note is `Health | FreeText`. The Article 9 special categories each have their own flag, and `SpecialCategory` masks all of them. `CriminalOffence` covers Article 10. `FreeText` marks values that might hold anything, including other people's data. Exports flag these for review before release. `AuditCopy` marks copies kept to make a record self-describing, such as a "created by" name.

### Data subjects and links

An entity marked `[DataSubject]` is a person. Every other entity is linked to a person by following foreign keys, up to four relationships deep by default. Each link is one of two kinds:

| Kind | Meaning | Export | Erasure |
|---|---|---|---|
| `Owner` | The record is about the person (their address, their order) | All values | Deleted, anonymised or retained, as configured |
| `Reference` | The record mentions the person ("created by", "assigned to") | Key only, by default | Left alone. The person is anonymised in place so the reference stays valid |

Declare the kind with `[DataSubjectKey(kind)]` on the foreign key or its navigation, or with `IsDataSubjectKey(kind)` in the fluent API. `DataSubjectLinkKind.None` excludes a relationship. Undeclared relationships are inferred: a required relationship that cascades on delete is `Owner`, and anything else is `Reference`. A path is `Owner` only when every step is. If a relationship leads somewhere by accident, for example `CreatedById` pointing at a user who is also a data subject, declare it.

### How erasure decides

For each record, `ErasureDecision` (a pure rule in the Abstractions package) checks these in order:

1. A legal hold keeps the record (Article 17(3)(e)).
2. An entity marked `[RetainOnErasure(exemption)]` keeps the record, and the ground is logged.
3. An `ErasurePolicy` category rule keeps the record, for example "keep anything containing `Health`".
4. An entity configured with `Erasure = Anonymise` keeps the record but anonymises its classified values.
5. Otherwise the record is deleted.

A planned deletion is changed to an anonymisation when kept records still depend on it through a required or restricting foreign key. This is why a customer with retained orders is anonymised rather than deleted, and why a deletion never cascades into records you meant to keep.

Values are anonymised by `AnonymisationMethod`:

| Method | Effect |
|---|---|
| `Null` | Sets the value to `null`, or to the type's default |
| `Redact` | Replaces text with `[erased]` (configurable), cut to the column length |
| `Hash` | Replaces the value with an HMAC-SHA256 of it, so a suppression list can still match it |
| `Generalise` | Reduces a date to 1 January of its year |
| `Custom` | Uses a named anonymiser registered with `AddAnonymiser` |

`Default` picks `Null` for nullable values, `Redact` for required text, and `Generalise` for required dates. To keep one value on an anonymised record, mark it `Erasure = ErasureAction.Retain` with a `RetentionExemption`, or use `.RetainOnErasure(exemption)`.

The erasure log (`ErasureResult.Entries`) names each record's entity, key, action, exemption and the properties touched. It never holds a value. If a primary key is itself personal data, the key is hashed. Store the log as evidence. Store the subject keys you erased as well, so you can run the erasures again after restoring a backup.

Data held outside the database, such as files, blobs and search indexes, is handled through `ErasureOptions.OnRecord`, which is called with each entity before it changes.

### Retention

```csharp
var policy = new RetentionPolicy("Marketing", RetentionPeriod.Parse("P2Y"), RetentionTrigger.CreatedAt);

RetentionRunResult run;
do run = await db.PersonalData().ApplyRetentionAsync(policy);
while (run.HasMore);
```

A policy matches entities by data class. Each entity needs a date marked `[RetentionTrigger(trigger)]` for the policy's trigger. Anonymising policies also need a nullable `[AnonymisedAt]` date, so the next batch doesn't select records it has already processed. Entities retained on erasure are skipped unless the policy sets `IncludeRetainedRecords`. The same dependency check as erasure applies.

### Change log

```csharp
options.UseGdpr(gdpr => gdpr.ObserveChanges(new MyRectificationLog()));
```

After each successful save, `IPersonalDataChangeObserver` receives the entity, key, state and the classified properties that changed. It never receives the values.

### Pure rules

These rules live in the Abstractions package, with no database, so you can unit test them and call them from anywhere:

- `ErasureDecision`: record and value outcomes, as described above
- `RetentionPeriod`: calendar arithmetic and ISO 8601 parsing (`P6Y`, `P18M`)
- `DataSubjectRequestDeadline`: one month from receipt, or three when extended; the last day of the month when there is no matching date; the next working day after a weekend (pass your own holiday calendar)
- `ProcessingRestrictionPolicy`: what Article 18(2) still allows while processing is restricted
- `PersonalDataHasher`: the keyed hash used by `AnonymisationMethod.Hash`, for checking suppression lists
- `LawfulBasis`, `SpecialCategoryCondition`, `ErasureExemption`, `DataSubjectRequestType`: the article references as enums, for your own request and processing records

## Keeping it complete

Add this test to your suite:

```csharp
[Fact]
public void Personal_data_is_fully_described()
{
    using var db = CreateContext();
    var gdpr = db.PersonalData();

    Assert.Empty(gdpr.Model.FindUnclassified());                   // every candidate column decided
    Assert.DoesNotContain(gdpr.Validate(), i => i.Severity == PersonalDataIssueSeverity.Error);
}
```

- `FindUnclassified` lists text, binary and date properties on entities linked to a person that carry neither `[PersonalData]` nor `[NotPersonalData]`.
- `Validate` reports entities holding personal data with no route to a person, records owned by two people, hash columns too short to hold a hash, and unregistered anonymisers.
- `Model.Inventory().ToCsv()` lists every classified column with its categories, data class, links and erasure behaviour.

If you have types but no `DbContext`, such as a netstandard2.0 entity library, use `PersonalDataCoverage.FindUnclassified(types)` and `PersonalDataRegistry` from the Abstractions package.

## Fluent API

| Attribute | Fluent API |
|---|---|
| `[DataSubject]` | `entity.IsDataSubject()` |
| `[PersonalDataEntity(DataClass = "x")]` | `entity.HasDataClass("x")` |
| `[PersonalDataEntity(Erasure = ...)]` | `entity.OnErasure(ErasureAction.Anonymise)` |
| `[RetainOnErasure(exemption)]` | `entity.RetainOnErasure(exemption, reason)` |
| `[PersonalData(categories, ...)]` | `property.IsPersonalData(categories, anonymisation, dataClass, anonymiser)` |
| `[NotPersonalData]` | `property.IsNotPersonalData(reason)` |
| `[DataSubjectKey(kind)]` | `property.IsDataSubjectKey(kind)` |
| `[RetentionTrigger(trigger)]` | `property.IsRetentionTrigger(trigger)` |
| `[AnonymisedAt]` | `property.IsAnonymisedAt()` |

Where both are used, the fluent API wins. Owned types are supported: classify their properties, and they are exported and anonymised with their owner.

ASP.NET Core Identity's own `[PersonalData]` attribute is recognised as `DirectIdentifier`. If a file imports both `Microsoft.AspNetCore.Identity` and this library's namespace, alias one of the two `PersonalData` attributes.

## Log redaction

```csharp
services.AddRedaction(r => r.AddPersonalDataRedactors(hmac =>
{
    hmac.KeyId = 1;
    hmac.Key = configuration["Logging:RedactionKey"];
}));

[LoggerMessage(Level = LogLevel.Information, Message = "Reminder sent to {Email}")]
static partial void LogReminderSent(ILogger logger,
    [PersonalDataClassification(PersonalDataCategory.Contact)] string email);
```

Special category, criminal offence, credential, free text, child and financial values are erased from logs. Other identifiers are HMAC-hashed when a key is configured, so you can still correlate log lines about one person, and erased when no key is configured.

## Migrations

Classification is stored as model annotations. The package registers design-time services through a `buildTransitive` targets file, and these keep the annotations out of migration snapshots and `Designer.cs` files, because they describe how data is handled, not the schema. To keep the annotations in snapshots, set `<GdprDesignTimeServices>false</GdprDesignTimeServices>`. If you reference the project instead of the package, import `buildTransitive/DSoft.EntityFrameworkCore.GDPR.targets` yourself, as the sample does.

## Limitations

- Complex types (`ComplexProperty`) are not yet classified or anonymised.
- Exports read CLR properties. Shadow properties other than keys are not exported.
- A record that belongs to two people through two owner links is erased with either of them. `Validate` warns about this. Declare one link as a reference if that is wrong.
- Retention compares dates in the database. SQLite cannot compare `DateTimeOffset`, so use `DateTime` trigger columns there.

## Building

```bash
dotnet build GDPRCore.slnx
dotnet test GDPRCore.slnx
dotnet pack GDPRCore.slnx -c Release
```

MIT licensed.
