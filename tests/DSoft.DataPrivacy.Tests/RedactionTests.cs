using System;
using System.Collections.Generic;
using System.Linq;
using DSoft.DataPrivacy.Redaction;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

#pragma warning disable EXTEXP0002 // HMAC redaction is marked experimental by Microsoft.Extensions.Compliance.Redaction.

public sealed partial class RedactionTests
{
    private const string Email = "jo@example.com";
    private const int KeyId = 7;

    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static TheoryData<PersonalDataCategory> Erased => Categories(PersonalDataTaxonomy.ErasedCategories);

    public static TheoryData<PersonalDataCategory> Identifying => Categories(PersonalDataTaxonomy.IdentifyingCategories);

    [Theory]
    [MemberData(nameof(Identifying))]
    public void Identifying_categories_are_hashed_when_a_key_is_configured(PersonalDataCategory category)
    {
        var redacted = Provider(hashed: true).GetRedactor(PersonalDataTaxonomy.For(category)).Redact(Email);

        AssertHashed(redacted);
    }

    [Theory]
    [MemberData(nameof(Erased))]
    public void High_risk_categories_are_erased_even_with_a_key(PersonalDataCategory category)
    {
        var redacted = Provider(hashed: true).GetRedactor(PersonalDataTaxonomy.For(category)).Redact(Email);

        Assert.Equal(string.Empty, redacted);
    }

    [Theory]
    [MemberData(nameof(Identifying))]
    [MemberData(nameof(Erased))]
    public void Every_category_is_erased_without_a_key(PersonalDataCategory category)
    {
        var redacted = Provider(hashed: false).GetRedactor(PersonalDataTaxonomy.For(category)).Redact(Email);

        Assert.Equal(string.Empty, redacted);
    }

    [Fact]
    public void The_same_value_hashes_the_same_so_log_lines_can_be_correlated()
    {
        var redactor = Provider(hashed: true).GetRedactor(PersonalDataTaxonomy.For(PersonalDataCategory.Contact));

        Assert.Equal(redactor.Redact(Email), redactor.Redact(Email));
        Assert.NotEqual(redactor.Redact(Email), redactor.Redact("else@example.com"));
    }

    [Fact]
    public void A_value_in_several_categories_is_redacted_as_its_most_sensitive()
    {
        var provider = Provider(hashed: true);

        AssertHashed(provider.GetRedactor(PersonalDataTaxonomy.For(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact)).Redact(Email));
        Assert.Equal(string.Empty, provider.GetRedactor(PersonalDataTaxonomy.For(PersonalDataCategory.Contact | PersonalDataCategory.Health)).Redact(Email));
    }

    [Fact]
    public void Every_category_has_a_parameterless_attribute()
    {
        var attributes = typeof(PersonalDataClassificationAttribute).Assembly.GetExportedTypes()
            .Where(t => t.IsSubclassOf(typeof(PersonalDataClassificationAttribute)))
            .Select(t => (PersonalDataClassificationAttribute)Activator.CreateInstance(t)!)
            .ToList();

        foreach (var category in PersonalDataTaxonomy.ErasedCategories.Flags().Concat(PersonalDataTaxonomy.IdentifyingCategories.Flags()))
        {
            var attribute = Assert.Single(attributes, a => a.Categories == category);
            Assert.Equal(category + "DataAttribute", attribute.GetType().Name);
            Assert.Equal(PersonalDataTaxonomy.For(category), attribute.Classification);
        }
    }

    [Fact]
    public void LoggerMessage_parameters_are_redacted_by_their_category()
    {
        var messages = new List<string>();
        using var services = new ServiceCollection()
            .AddRedaction(redaction => Configure(redaction, hashed: true))
            .AddLogging(logging => logging.EnableRedaction().AddProvider(new RecordingLoggerProvider(messages)))
            .BuildServiceProvider();
        var logger = services.GetRequiredService<ILogger<RedactionTests>>();

        LogReminderSent(logger, Email);
        LogReminderSent(logger, Email);
        LogSignedIn(logger, Email);
        LogDiagnosis(logger, "Asthma");

        AssertHashed(messages[0].Substring("Reminder sent to ".Length));
        Assert.Equal(messages[0], messages[1]);
        AssertHashed(messages[2].Substring("Signed in as ".Length));
        Assert.Equal("Diagnosis recorded: ", messages[3]);
        Assert.DoesNotContain(messages, m => m.Contains(Email) || m.Contains("Asthma"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reminder sent to {Email}")]
    private static partial void LogReminderSent(ILogger logger, [ContactData] string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Signed in as {Email}")]
    private static partial void LogSignedIn(ILogger logger, [CustomerEmail] string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Diagnosis recorded: {Diagnosis}")]
    private static partial void LogDiagnosis(ILogger logger, [HealthData] string diagnosis);

    private static IRedactorProvider Provider(bool hashed)
        => new ServiceCollection()
            .AddRedaction(redaction => Configure(redaction, hashed))
            .BuildServiceProvider()
            .GetRequiredService<IRedactorProvider>();

    // The fallback leaves values alone, so a value only comes back redacted when its classification is mapped.
    private static void Configure(IRedactionBuilder redaction, bool hashed)
    {
        redaction.SetFallbackRedactor<NullRedactor>();
        redaction.AddPersonalDataRedactors(hashed
            ? hmac =>
            {
                hmac.KeyId = KeyId;
                hmac.Key = Key;
            }
            : null);
    }

    private static void AssertHashed(string redacted)
    {
        Assert.StartsWith(KeyId + ":", redacted);
        Assert.True(redacted.Length > 20, redacted);
        Assert.DoesNotContain(Email, redacted);
    }

    private static TheoryData<PersonalDataCategory> Categories(PersonalDataCategory categories)
    {
        var data = new TheoryData<PersonalDataCategory>();
        foreach (var category in categories.Flags())
            data.Add(category);

        return data;
    }

    // A combination needs an attribute of its own: the source generator creates it with no arguments.
    public sealed class CustomerEmailAttribute : PersonalDataClassificationAttribute
    {
        public CustomerEmailAttribute()
            : base(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact)
        {
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly List<string> _messages;

        public RecordingLoggerProvider(List<string> messages)
        {
            _messages = messages;
        }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _messages.Add(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
