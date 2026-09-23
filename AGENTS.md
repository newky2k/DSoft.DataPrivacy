# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Commands

```bash
# Build the entire solution
dotnet build GDPRCore.slnx

# Build in Release (also produces the NuGet packages, under src/*/bin/Release)
dotnet build GDPRCore.slnx -c Release

# Run all tests
dotnet test GDPRCore.slnx

# Run one test class
dotnet test tests/DSoft.DataPrivacy.Tests --filter "FullyQualifiedName~ErasureTests"

# Run the sample API under a chosen law
dotnet run --project samples/DataPrivacySample -- --DataPrivacy:Regime=popia
```

Warnings are errors. The tests must be green before a change is done.

## Architecture

The repository is called GDPRCore, but the library is regulation-neutral: the packages are `DSoft.DataPrivacy.*`, and each data protection law is a `PrivacyRegime`. There are three NuGet packages, a test project and a sample:

```
DSoft.DataPrivacy                            netstandard2.0, no dependencies
    ↑                            ↑
DSoft.DataPrivacy.EntityFrameworkCore        DSoft.DataPrivacy.Redaction
net10.0, EF Core 10                         net10.0, Microsoft.Extensions.Compliance
```

**`DSoft.DataPrivacy`**: everything that must not depend on EF, so domain libraries can classify their own types.
- `Attributes/`: `[PersonalData]`, `[NotPersonalData]`, `[DataSubject]`, `[DataSubjectKey]`, `[PersonalDataEntity]`, `[RetainOnErasure]`, `[RetentionTrigger]`, `[AnonymisedAt]`
- `Classification/`: `PersonalDataCategory` (flags) and the neutral enums: `RetentionGround`, `LawfulBasis`, `SpecialDataCondition`, `DataSubjectRequestType`
- `Regimes/`: `PrivacyRegime`, and `GdprRegime` and `PopiaRegime` exposed through `PrivacyRegimes`. `CombinedPrivacyRegime` (from `PrivacyRegimes.Combine`, `UseRegimes`, or `UseRegime("gdpr,popia")`) applies several laws at once and always gives the stricter answer: grounds must be recognised by every law, special data is the union, rights are the union, deadlines are the earliest. A regime cites the provision behind each neutral value (`null` when the law has none), defines special data, lists rights, calculates request deadlines and gives the breach rule.
- `Registry/`: `PersonalDataAttributeReader`, the only place attributes are interpreted; `PersonalDataRegistry` and `PersonalDataCoverage` build on it
- `Rules/`: pure decisions (`ErasureDecision`, `RetentionPeriod`, `ProcessingRestrictionPolicy`)

**`DSoft.DataPrivacy.EntityFrameworkCore`**:
- `UseDataPrivacy()` adds `DataPrivacyOptionsExtension`, which registers `PersonalDataAttributeConvention`. The convention copies the attributes into model annotations (`Metadata/PrivacyAnnotationNames`, prefix `Privacy:`, primitive values only) at data-annotation precedence, so the fluent API in `Extensions/` wins. `UseRegime()` sets the law in force.
- `Metadata/PersonalDataModel` reads the annotations from the runtime model and discovers the foreign-key paths from each entity to a data subject (`DataSubjectLink`, `Owner` or `Reference`).
- `db.PersonalData()` returns `PersonalDataOperations`: `ExportAsync`, `EraseAsync`, `ApplyRetentionAsync`, `Inventory` and `Validate`. `Validate` also checks retention grounds against the regime.
- `Querying/SubjectQuery` builds the correlated `EXISTS` queries along a link, with no navigation properties needed.
- `Erasure/ErasurePlan` is shared by erasure and retention. It turns a planned deletion into an anonymisation when kept records still depend on the row, and cites the regime in the log.
- `Design/DataPrivacyDesignTimeServices`, registered by `buildTransitive/*.targets`, keeps the annotations out of migration snapshots.
- `Auditing/PersonalDataChangeInterceptor` reports which classified properties changed, never their values.

## Rules

- Keep the core regulation-neutral. Anything that differs between laws (a citation, a deadline, what counts as special data, which rights exist) belongs in a `PrivacyRegime`, not in an enum's meaning or an `if` on the law. Add a law by deriving from `PrivacyRegime` and registering it in `PrivacyRegimes`, with tests in `RegimeTests`.
- Citations are guidance, not legal advice. Cite at section level when unsure of a subsection, and return `null` rather than guess.
- Decision logic that can be pure goes in `DSoft.DataPrivacy/Rules`, with tests in `RuleTests`.
- Never put a personal value in a log, exception message or `ErasureLogEntry`.
- Annotation values stay primitive (bool, int, long, string), so they survive compiled models.
- Tests use SQLite in memory. SQLite cannot compare `DateTimeOffset`, so use `DateTime` for retention triggers in tests.
- No product-specific names: this is an open-source library.

## Key configuration

- **Shared build settings** (`Directory.Build.props`): nullable, warnings as errors, NuGet metadata, the `DSIcon.png` package icon, SourceLink in Release, and strong-name signing for any project containing `DSoft.snk`. Projects under `src/` generate their package on build.
- **Package versions** are managed centrally in `Directory.Packages.props`. The packages target .NET 10 (the abstractions stay netstandard2.0).
- **Strong naming**: each package project and the test project has its own `DSoft.snk`. Do not remove these.
- **Package version** is not set in any project. The release pipeline injects it with `/p:Version=$(Build.BuildNumber)`.

## CI pipelines

GitHub Actions (`.github/workflows/`):

- `ci.yml`: runs on pull requests to `main`. Builds Release and runs the tests. Publishes nothing.
- `release.yml`: runs on every push to `main` (changes only to Markdown or workflow files are skipped; run it by hand to test a workflow change). Builds Release as `1.0.yyMM.<run number>-prerelease`, runs the tests, uploads the packages as the `drop` artifact and pushes them to nuget.org with Trusted Publishing (OIDC, `NUGET_USER` secret, `nuget` environment), then tags the commit `v<version>` and creates a GitHub prerelease with the packages attached.

Azure Pipelines (kept alongside):

- `azure-pipelines-mergetest.yml`: triggered manually for PR validation. Builds Release and runs the tests. Publishes nothing.
- `azure-pipelines-release.yml`: triggers on `main`. Builds Release, runs the tests, and publishes `**/DSoft.*.nupkg` as the `drop` artifact.
