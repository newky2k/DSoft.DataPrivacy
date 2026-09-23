# GDPRCore: DSoft.DataPrivacy

Data privacy tooling for Entity Framework Core. You mark which data is personal and what kind it is, using attributes or the fluent API, and choose the data protection law that applies. The library then uses that classification to:

- **inventory** where personal data lives, to feed your record of processing activities and privacy impact assessments
- **export** everything held about a person, for access and portability requests
- **erase** a person: it deletes what can go, anonymises what other records still depend on, and keeps what a retention ground covers, with a log that holds no personal values and cites the provision relied on
- **apply retention policies** in batches (storage limitation)
- **report changes** to personal data, for a correction log or audit trail
- **redact logs** by the same categories
- **check coverage** in a unit test, so a new column can't ship without a decision

The classification is regulation-neutral. The laws themselves are **privacy regimes**. Two ship today:

| Regime | Law | Terms |
|---|---|---|
| `PrivacyRegimes.Gdpr` | EU GDPR (2016/679), and the UK GDPR with the Data Protection Act 2018 | controller, processor, special category data |
| `PrivacyRegimes.Popia` | South Africa's Protection of Personal Information Act 4 of 2013, with PAIA for access requests | responsible party, operator, special personal information |

The library is a tool, not legal advice. Its citations show which provision a decision relies on. Your data protection or information officer still decides the lawful bases, retention grounds and retention periods, and should confirm the citations for your processing. The library applies their decisions the same way every time.

## Packages

| Package | Targets | Use it for |
|---|---|---|
| `DSoft.DataPrivacy` | netstandard2.0 | Attributes, categories, regimes and pure rules. No EF dependency, so domain and entity libraries can classify their own types. |
| `DSoft.DataPrivacy.EntityFrameworkCore` | net8.0, net10.0 | The EF Core conventions, fluent API, export, erasure, retention and change log. |
| `DSoft.DataPrivacy.Redaction` | net8.0, net10.0 | Log redaction through `Microsoft.Extensions.Compliance`. |

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

[RetainOnErasure(RetentionGround.HealthOrSocialCare, Reason = "Health records retention schedule")]
public class ClinicalNote { /* ... */ }
```

Turn it on, with the law that applies to this deployment:

```csharp
services.AddDbContext<ShopContext>(options => options
    .UseSqlServer(connectionString)
    .UseDataPrivacy(privacy => privacy
        .UseRegime(PrivacyRegimes.Popia)              // or .UseRegime("gdpr") from configuration
        .UseHashKey(hashKey)));                       // 32+ secret bytes, kept stable
```

Use it:

```csharp
var privacy = db.PersonalData();

DataSubjectExport? export = await privacy.ExportAsync<Customer>(customerId);
string json = export!.ToJson();

ErasureResult preview = await privacy.EraseAsync<Customer>(customerId, new ErasureOptions { DryRun = true });
ErasureResult result  = await privacy.EraseAsync<Customer>(customerId);
```

In that model, erasing a customer:

- deletes their consents and other records nothing depends on
- anonymises each order (`DeliveryName` becomes `[erased]`, the total stays)
- keeps the clinical notes, and logs the ground with its citation: `s14(1)(a)` under POPIA, `Art 17(3)(c)` under the GDPR
- anonymises the customer in place rather than deleting them, because kept records still point at them

[`samples/DataPrivacySample`](samples/DataPrivacySample) is a runnable minimal API that shows every feature. Run it with `--DataPrivacy:Regime=popia` or `gdpr`.

## Concepts

### Regimes

A `PrivacyRegime` turns the neutral classification into law. It:

- cites the provision behind each `RetentionGround`, `LawfulBasis`, `SpecialDataCondition` and right, and returns `null` when the law has no such provision
- says which categories count as special data: POPIA includes criminal behaviour, and the GDPR handles that separately under Article 10
- lists the rights the law gives: POPIA has no portability right
- calculates request deadlines. Under the GDPR it's one month, or three when extended. Under POPIA an access request follows PAIA: 30 days, or 60 when extended, and other requests have no fixed period. Both move to the next working day, and you pass your own public holidays.
- gives the breach notification rule: 72 hours under the GDPR, and "as soon as reasonably possible" under POPIA

```csharp
var regime = db.PersonalData().Regime!;
DateTime? due = regime.RequestDeadline(DataSubjectRequestType.Access, receivedOn);
string? basis = regime.Cite(LawfulBasis.LegitimateInterests);   // "Art 6(1)(f)" or "s11(1)(f)"
bool special = regime.IsSpecial(PersonalDataCategory.CriminalOffence);
```

To support another law, derive from `PrivacyRegime`.

#### When several laws apply

More than one law often applies. The GDPR follows EU and UK residents wherever the organisation is based, so a South African business with UK customers answers to both POPIA and the GDPR. Apply them together:

```csharp
options.UseDataPrivacy(privacy => privacy.UseRegimes(PrivacyRegimes.Gdpr, PrivacyRegimes.Popia));
// or from configuration: privacy.UseRegime("gdpr,popia")
```

The laws are combined into one `CombinedPrivacyRegime` that gives the stricter answer every time, so meeting it meets each law:

| Question | Combined answer |
|---|---|
| Is a retention ground, lawful basis or special data condition valid? | Only if every law recognises it |
| Is this special data? | If any law says so |
| Which rights exist? | Every right any law gives |
| When is a request due? | The earliest deadline any law sets |
| Breach notification window | The shortest fixed window |
| Citation | Each law's provision, such as `GDPR Art 17(3)(c); POPIA s14(1)(a)` |

The regime is set per `DbContext`, which usually means per deployment, and retention and validation run across whole tables. So set it to every law the deployment can face, not just the law for one person.

### Categories

`PersonalDataCategory` is a flags enum. Combine categories to describe a value: an email address is `DirectIdentifier | Contact`, and a clinical note is `Health | FreeText`. Each special category has its own flag, and `SpecialCategory` masks the ones the GDPR and POPIA share. `FreeText` marks values that might hold anything, including other people's data. Exports flag these for review before release. `AuditCopy` marks copies kept to make a record self-describing, such as a "created by" name.

POPIA also protects existing companies (juristic persons) as data subjects. Any entity can be marked `[DataSubject]`.

### Data subjects and links

An entity marked `[DataSubject]` is a person, or under POPIA possibly a company. Every other entity is linked to a data subject by following foreign keys, up to four relationships deep by default. Each link is one of two kinds:

| Kind | Meaning | Export | Erasure |
|---|---|---|---|
| `Owner` | The record is about the person (their address, their order) | All values | Deleted, anonymised or retained, as configured |
| `Reference` | The record mentions the person ("created by", "assigned to") | Key only, by default | Left alone. The person is anonymised in place so the reference stays valid |

Declare the kind with `[DataSubjectKey(kind)]` on the foreign key or its navigation, or with `IsDataSubjectKey(kind)` in the fluent API. `DataSubjectLinkKind.None` excludes a relationship. Undeclared relationships are inferred: a required relationship that cascades on delete is `Owner`, and anything else is `Reference`. A path is `Owner` only when every step is. If a relationship leads somewhere by accident, for example `CreatedById` pointing at a user who is also a data subject, declare it.

### How erasure decides

For each record, `ErasureDecision` (a pure rule in `DSoft.DataPrivacy`) checks these in order:

1. A legal hold keeps the record under `RetentionGround.LegalClaims`.
2. An entity marked `[RetainOnErasure(ground)]` keeps the record, and the ground is logged with its citation.
3. An `ErasurePolicy` category rule keeps the record, for example "keep anything containing `Health`".
4. An entity configured with `Erasure = Anonymise` keeps the record but anonymises its classified values.
5. Otherwise the record is deleted.

A planned deletion is changed to an anonymisation when kept records still depend on it through a required or restricting foreign key. This is why a customer with retained orders is anonymised rather than deleted, and why a deletion never cascades into records you meant to keep.

Retention grounds are neutral, but each law recognises only some of them. `Consent`, `Contract` and `OperationalNeed` are grounds under POPIA s14(1), but not under GDPR Article 17(3). `Validate()` reports any ground the regime in force doesn't recognise.

Values are anonymised by `AnonymisationMethod`:

| Method | Effect |
|---|---|
| `Null` | Sets the value to `null`, or to the type's default |
| `Redact` | Replaces text with `[erased]` (configurable), cut to the column length |
| `Hash` | Replaces the value with an HMAC-SHA256 of it, so a suppression list can still match it |
| `Generalise` | Reduces a date to 1 January of its year |
| `Custom` | Uses a named anonymiser registered with `AddAnonymiser` |

`Default` picks `Null` for nullable values, `Redact` for required text, and `Generalise` for required dates. To keep one value on an anonymised record, mark it `Erasure = ErasureAction.Retain` with a `RetentionGround`, or use `.RetainOnErasure(ground)`.

The erasure log (`ErasureResult.Entries`) names each record's entity, key, action, retention ground, citation and the properties touched. It never holds a value. If a primary key is itself personal data, the key is hashed. Store the log as evidence. Store the subject keys you erased as well, so you can run the erasures again after restoring a backup.

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
options.UseDataPrivacy(privacy => privacy.ObserveChanges(new MyCorrectionLog()));
```

After each successful save, `IPersonalDataChangeObserver` receives the entity, key, state and the classified properties that changed. It never receives the values.

### Pure rules

These rules live in `DSoft.DataPrivacy`, with no database, so you can unit test them and call them from anywhere:

- `ErasureDecision`: record and value outcomes, as described above
- `RetentionPeriod`: calendar arithmetic and ISO 8601 parsing (`P6Y`, `P18M`)
- `ProcessingRestrictionPolicy`: what the law still allows while processing is restricted (GDPR Article 18(2), POPIA s14)
- `PersonalDataHasher`: the keyed hash used by `AnonymisationMethod.Hash`, for checking suppression lists
- `PrivacyRegime`: citations, special data, rights, deadlines and the breach rule, as above
- `LawfulBasis`, `SpecialDataCondition`, `RetentionGround`, `DataSubjectRequestType`: neutral enums for your own request and processing records, cited by the regime

## Keeping it complete

Add this test to your suite:

```csharp
[Fact]
public void Personal_data_is_fully_described()
{
    using var db = CreateContext();
    var privacy = db.PersonalData();

    Assert.Empty(privacy.Model.FindUnclassified());                // every candidate column decided
    Assert.DoesNotContain(privacy.Validate(), i => i.Severity == PersonalDataIssueSeverity.Error);
}
```

- `FindUnclassified` lists text, binary and date properties on entities linked to a person that carry neither `[PersonalData]` nor `[NotPersonalData]`.
- `Validate` reports:
  - entities holding personal data with no route to a person
  - records owned by two people
  - retention grounds the regime doesn't recognise
  - hash columns too short to hold a hash
  - unregistered anonymisers
  - a missing regime
- `Inventory().ToCsv()` lists every classified column with its categories, whether it is special data under the regime, its data class, links and erasure behaviour, and citations.

If you have types but no `DbContext`, such as a netstandard2.0 entity library, use `PersonalDataCoverage.FindUnclassified(types)` and `PersonalDataRegistry` from `DSoft.DataPrivacy`.

## Fluent API

| Attribute | Fluent API |
|---|---|
| `[DataSubject]` | `entity.IsDataSubject()` |
| `[PersonalDataEntity(DataClass = "x")]` | `entity.HasDataClass("x")` |
| `[PersonalDataEntity(Erasure = ...)]` | `entity.OnErasure(ErasureAction.Anonymise)` |
| `[RetainOnErasure(ground)]` | `entity.RetainOnErasure(ground, reason)` |
| `[PersonalData(categories, ...)]` | `property.IsPersonalData(categories, anonymisation, dataClass, anonymiser)` |
| `[NotPersonalData]` | `property.IsNotPersonalData(reason)` |
| `[DataSubjectKey(kind)]` | `property.IsDataSubjectKey(kind)` |
| `[RetentionTrigger(trigger)]` | `property.IsRetentionTrigger(trigger)` |
| `[AnonymisedAt]` | `property.IsAnonymisedAt()` |

Where both are used, the fluent API wins. Owned types are supported: classify their properties, and they are exported and anonymised with their owner.

ASP.NET Core Identity's own `[PersonalData]` attribute is recognised as `DirectIdentifier`. If a file imports both `Microsoft.AspNetCore.Identity` and `DSoft.DataPrivacy`, alias one of the two `PersonalData` attributes.

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

Classification is stored as model annotations. The package registers design-time services through a `buildTransitive` targets file, and these keep the annotations out of migration snapshots and `Designer.cs` files, because they describe how data is handled, not the schema. To keep the annotations in snapshots, set `<DataPrivacyDesignTimeServices>false</DataPrivacyDesignTimeServices>`. If you reference the project instead of the package, import `buildTransitive/DSoft.DataPrivacy.EntityFrameworkCore.targets` yourself, as the sample does.

## Limitations

- Complex types (`ComplexProperty`) are not yet classified or anonymised.
- Exports read CLR properties. Shadow properties other than keys are not exported.
- A record that belongs to two people through two owner links is erased with either of them. `Validate` warns about this. Declare one link as a reference if that is wrong.
- Retention compares dates in the database. SQLite cannot compare `DateTimeOffset`, so use `DateTime` trigger columns there.
- Regime citations cover the provisions this library uses. They are not a complete statement of either law.

## Building

```bash
dotnet build GDPRCore.slnx
dotnet test GDPRCore.slnx
dotnet build GDPRCore.slnx -c Release   # also produces the packages
```

The assemblies are strong-named. Package versions are set by the release pipeline (`azure-pipelines-release.yml`).

MIT licensed.
