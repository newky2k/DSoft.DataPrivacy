# DSoft.DataPrivacy

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
| `DSoft.DataPrivacy.EntityFrameworkCore` | net10.0 (EF Core 10) | The EF Core conventions, fluent API, export, erasure, retention and change log. |
| `DSoft.DataPrivacy.Redaction` | net10.0 | Log redaction through `Microsoft.Extensions.Compliance`. |

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

### Leaving records out of an export

Some records are governed separately, such as clinical records behind their own access gate and audit trail. Leave them out of the export and answer for them through that process:

```csharp
var export = await privacy.ExportAsync<Customer>(customerId, new DataSubjectExportOptions
{
    ExcludedTypes = { typeof(ClinicalNote) },            // by entity type, with derived types
    Exclude = entity => entity.DataClass == "Clinical",  // or by anything on the entity
});
```

Exclusion is decided before anything is read, so an excluded entity is never queried, and neither are records reached only through one. `export.ExcludedEntities` names what was left out. The data subject's own record is always exported.

### How erasure decides

For each record, `ErasureDecision` (a pure rule in `DSoft.DataPrivacy`) checks these in order:

1. A legal hold keeps the record under `RetentionGround.LegalClaims`.
2. An entity marked `[RetainOnErasure(ground)]` keeps the record, and the ground is logged with its citation.
3. An `ErasurePolicy` category rule keeps the record, for example "keep anything containing `Health`".
4. `ErasureOptions.RetainRecord` keeps the record on a ground of its own (see [Retaining individual records](#retaining-individual-records)).
5. An entity configured with `Erasure = Anonymise` keeps the record but anonymises its classified values.
6. Otherwise the record is deleted.

A planned deletion is changed to an anonymisation when kept records still depend on it through a required or restricting foreign key. This is why a customer with retained orders is anonymised rather than deleted, and why a deletion never cascades into records you meant to keep.

When a record and the records that require it are all deleted, they are removed dependants first, whatever order they were found in. The log lists them in the order they were found.

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

#### Retaining individual records

Some duties attach to a record, not to its type: "a certificate issued under an accredited body is kept for seven years; any other certificate is deleted". Decide those per record:

```csharp
var result = await db.PersonalData().EraseAsync<Person>(id, new ErasureOptions
{
    RetainRecord = (entity, row) => row is Certificate { Accredited: true }
        ? new RecordRetention(RetentionGround.LegalObligation, "Accredited certificates are kept for seven years")
        : null,
});
```

`RetainRecord` is called for each record that would otherwise be deleted or anonymised. A record it returns a value for is kept whole, exactly like one whose entity is marked `[RetainOnErasure]`: the log shows `Retain` with the ground, its citation and the reason, and the records it depends on are anonymised rather than deleted. Returning `null` leaves the record to its entity's configuration.

It can only add retention. A record already kept, by a legal hold, its entity or an `ErasurePolicy` rule, isn't offered and keeps the ground it has. A dry run calls it too, so the plan shows the same outcomes. The reason is written to the log, so don't put a personal value in it.

### Retention

```csharp
var policy = new RetentionPolicy("Marketing", RetentionPeriod.Parse("P2Y"), RetentionTrigger.CreatedAt);

RetentionRunResult run;
do run = await db.PersonalData().ApplyRetentionAsync(policy);
while (run.HasMore);
```

A policy matches entities by data class. Each entity needs a date marked `[RetentionTrigger(trigger)]` for the policy's trigger. Anonymising policies also need a nullable `[AnonymisedAt]` date, so the next batch doesn't select records it has already processed. Entities retained on erasure are skipped unless the policy sets `IncludeRetainedRecords`. The same dependency check as erasure applies.

Some records must meet a condition as well as the period, such as "kept while the person it is about still has a record". Add it to the policy:

```csharp
var policy = new RetentionPolicy("Trails", RetentionPeriod.Parse("P8Y"))
    .Where<AccessEntry>(e => !db.People.Any(p => p.Id == e.PersonId && p.AnonymisedAt == null));
```

The condition is part of the query that selects due records, so it runs in the database and a record that fails it is never loaded. It isn't counted, held or listed in `Skipped`. Entities with no condition are selected by the period alone, and several conditions for one type must all be met. A query on another set must use the context the policy runs on. For a policy that outlives a context, use `Where<AccessEntry>(db => e => ...)`, which is given the context on each run. A condition for a type outside the policy's data class is reported in `Skipped`.

A record under a legal hold must stay, whatever the period says. Pass the same test you give `ErasureOptions.IsOnLegalHold`:

```csharp
var run = await db.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions
{
    IsOnLegalHold = (entity, row) => holds.Covers(entity.ClrType, row),
});
```

A held record is neither deleted nor anonymised. `run.Held` counts them and `run.Skipped` names each by entity and key, on a dry run too. Held records don't use up the batch: the run reads past them, so the records behind them are still processed.

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

    Assert.Empty(privacy.Model.FindUnclassifiedAnywhere());        // every candidate column decided
    Assert.DoesNotContain(privacy.Validate(), i => i.Severity == PersonalDataIssueSeverity.Error);
}
```

- `FindUnclassifiedAnywhere` lists text, binary and date properties that carry neither `[PersonalData]` nor `[NotPersonalData]`, on every entity that holds personal data. That means data subjects, entities linked to one, entities with a classified property, and entities with a property whose name suggests personal data (`Email`, `Phone`, `FirstName`, `Address`, `PostCode`, `IpAddress` and so on). Text properties whose names start with `From`, `To` or `Recipient` count too, such as `FromName` and `ToAddress`; dates such as `FromDate` and `ToDate` don't. The last group catches inbound mail, event logs and audit snapshots, which have no foreign key to the person. It reads the EF model, so fluent classification counts. Pass your own test to replace the names: `FindUnclassifiedAnywhere(p => p.Name.EndsWith("Email"))`.
- `FindUnclassified` checks only data subjects and the entities linked to one.
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
    hmac.Key = configuration["Logging:RedactionKey"];   // base64, at least 44 characters
}));
services.AddLogging(logging => logging.EnableRedaction());   // from Microsoft.Extensions.Telemetry

[LoggerMessage(Level = LogLevel.Information, Message = "Reminder sent to {Email}")]
static partial void LogReminderSent(ILogger logger, [ContactData] string email);
```

Each category has its own attribute, named after it: `[ContactData]`, `[DirectIdentifierData]`, `[HealthData]`, `[FreeTextData]` and so on. The `[LoggerMessage]` source generator creates the attribute with no arguments, so `[PersonalDataClassification(...)]` cannot be used there. For a value in several categories, derive an attribute of your own. It is redacted as its most sensitive category:

```csharp
public sealed class CustomerEmailAttribute : PersonalDataClassificationAttribute
{
    public CustomerEmailAttribute()
        : base(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact)
    {
    }
}
```

Special category, criminal offence, credential, free text, child and financial values are erased from logs. Other identifiers are HMAC-hashed when a key is configured, so you can still correlate log lines about one person, and erased when no key is configured.

To map redactors yourself, use `PersonalDataTaxonomy.ErasedSets`, `IdentifyingSets` or `SetsFor(categories)`. A redactor is chosen by the exact classification set a value carries, so each category is mapped as a set of its own.

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

The assemblies are strong-named. Package versions are set by the release pipeline (`.github/workflows/release.yml`) as `1.0.yyMM.<run number>`, with an optional prerelease suffix.

MIT licensed.
