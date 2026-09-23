# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the entire solution
dotnet build GDPRCore.slnx

# Build in Release (also produces the NuGet packages, under src/*/bin/Release)
dotnet build GDPRCore.slnx -c Release

# Run all tests (net8.0 and net10.0)
dotnet test GDPRCore.slnx

# Run one test class
dotnet test tests/DSoft.EntityFrameworkCore.GDPR.Tests --filter "FullyQualifiedName~ErasureTests"

# Run the sample API
dotnet run --project samples/GdprSample
```

Warnings are errors. Both test target frameworks must be green before a change is done.

## Architecture

Three NuGet packages, a test project and a sample:

```
DSoft.EntityFrameworkCore.GDPR.Abstractions   netstandard2.0, no dependencies
    ↑                         ↑
DSoft.EntityFrameworkCore.GDPR               DSoft.EntityFrameworkCore.GDPR.Redaction
net8.0; net10.0, EF Core                     net8.0; net10.0, Microsoft.Extensions.Compliance
```

**`DSoft.EntityFrameworkCore.GDPR.Abstractions`**: everything that must not depend on EF, so domain libraries can classify their own types.
- `Attributes/`: `[PersonalData]`, `[NotPersonalData]`, `[DataSubject]`, `[DataSubjectKey]`, `[PersonalDataEntity]`, `[RetainOnErasure]`, `[RetentionTrigger]`, `[AnonymisedAt]`
- `Classification/`: `PersonalDataCategory` (flags) and the article enums
- `Registry/`: `PersonalDataAttributeReader`, the only place attributes are interpreted; `PersonalDataRegistry` and `PersonalDataCoverage` build on it
- `Rules/`: pure decisions (`ErasureDecision`, `RetentionPeriod`, `DataSubjectRequestDeadline`, `ProcessingRestrictionPolicy`)

**`DSoft.EntityFrameworkCore.GDPR`**:
- `UseGdpr()` adds `GdprOptionsExtension`, which registers `PersonalDataAttributeConvention`. The convention copies the attributes into model annotations (`Metadata/GdprAnnotationNames`, primitive values only) at data-annotation precedence, so the fluent API in `Extensions/` wins.
- `Metadata/PersonalDataModel` reads the annotations from the runtime model and discovers the foreign-key paths from each entity to a data subject (`DataSubjectLink`, `Owner` or `Reference`).
- `db.PersonalData()` returns `PersonalDataOperations`: `ExportAsync`, `EraseAsync`, `ApplyRetentionAsync` and `Validate`.
- `Querying/SubjectQuery` builds the correlated `EXISTS` queries along a link, with no navigation properties needed.
- `Erasure/ErasurePlan` is shared by erasure and retention. It turns a planned deletion into an anonymisation when kept records still depend on the row.
- `Design/GdprDesignTimeServices`, registered by `buildTransitive/*.targets`, keeps the annotations out of migration snapshots.
- `Auditing/PersonalDataChangeInterceptor` reports which classified properties changed, never their values.

## Rules

- Decision logic that can be pure goes in `Abstractions/Rules`, with tests in `RuleTests`.
- Never put a personal value in a log, exception message or `ErasureLogEntry`.
- Annotation values stay primitive (bool, int, long, string), so they survive compiled models.
- Tests use SQLite in memory. SQLite cannot compare `DateTimeOffset`, so use `DateTime` for retention triggers in tests.
- No product-specific names: this is an open-source library.

## Key configuration

- **Shared build settings** (`Directory.Build.props`): nullable, warnings as errors, NuGet metadata, the `DSIcon.png` package icon, SourceLink in Release, and strong-name signing for any project containing `DSoft.snk`. Projects under `src/` generate their package on build.
- **Package versions** are managed centrally in `Directory.Packages.props`. The EF Core and Compliance versions differ per target framework.
- **Strong naming**: each package project and the test project has its own `DSoft.snk`. Do not remove these.
- **Package version** is not set in any project. The release pipeline injects it with `/p:Version=$(Build.BuildNumber)`.

## CI pipelines

- `azure-pipelines-mergetest.yml`: triggered manually for PR validation. Builds Release and runs the tests. Publishes nothing.
- `azure-pipelines-release.yml`: triggers on `main`. Builds Release, runs the tests, and publishes `**/DSoft.*.nupkg` as the `drop` artifact.
