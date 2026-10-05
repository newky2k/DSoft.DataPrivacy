using System;
using System.Linq;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Retention;
using DSoft.DataPrivacy.Rules;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

/// <summary>
/// Records that require each other and are deleted together. The model lists the entities by name, so Album,
/// Photo and Print are planned principal first, and Book is planned before the Shelf it requires.
/// </summary>
public sealed class DeletionOrderTests : IDisposable
{
    private static readonly DateTime Old = Seed.Now.AddYears(-9);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DeletionOrderTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MediaContext CreateContext() => new(_connection);

    private static RetentionPolicy Policy(string dataClass) => new(dataClass, RetentionPeriod.FromYears(8));

    private static RetentionRunOptions Run => new() { Now = Seed.Now };

    private int SeedPerson()
    {
        using var seed = CreateContext();
        var person = new Person { Name = "Jo Bloggs" };
        seed.Add(person);
        seed.SaveChanges();
        return person.Id;
    }

    /// <summary>An album holding a photo that has a print, all past the period.</summary>
    private int SeedAlbum(int personId, DateTime? photoTakenAt = null)
    {
        using var seed = CreateContext();
        var album = new Album { PersonId = personId, Title = "Holiday", CreatedAt = Old };
        seed.Add(album);
        seed.SaveChanges();

        var photo = new Photo { PersonId = personId, AlbumId = album.Id, Caption = "Jo at the beach", TakenAt = photoTakenAt ?? Old };
        seed.Add(photo);
        seed.SaveChanges();

        seed.Add(new Print { PersonId = personId, PhotoId = photo.Id, Size = "A4", OrderedAt = photoTakenAt ?? Old });
        seed.SaveChanges();
        return album.Id;
    }

    [Fact]
    public async Task A_retention_run_deletes_a_record_and_the_records_that_require_it()
    {
        SeedAlbum(SeedPerson());

        RetentionRunResult result;
        using (var context = CreateContext())
            result = await context.PersonalData().ApplyRetentionAsync(Policy("Media"), Run);

        using var check = CreateContext();
        Assert.Empty(check.Set<Album>());
        Assert.Empty(check.Set<Photo>());
        Assert.Empty(check.Set<Print>());
        Assert.Equal(3, result.Deleted);

        // The log stays in the order the records were planned.
        Assert.Equal(new[] { "Album", "Photo", "Print" }, result.Entries.Select(e => e.Entity));
    }

    [Fact]
    public async Task A_retention_run_deletes_both_when_the_dependant_is_planned_first()
    {
        var personId = SeedPerson();
        using (var seed = CreateContext())
        {
            var shelf = new Shelf { PersonId = personId, Label = "Jo's shelf", AddedAt = Old };
            seed.Add(shelf);
            seed.SaveChanges();
            seed.Add(new Book { PersonId = personId, ShelfId = shelf.Id, Inscription = "To Jo", AddedAt = Old });
            seed.SaveChanges();
        }

        RetentionRunResult result;
        using (var context = CreateContext())
            result = await context.PersonalData().ApplyRetentionAsync(Policy("Library"), Run);

        using var check = CreateContext();
        Assert.Empty(check.Set<Shelf>());
        Assert.Empty(check.Set<Book>());
        Assert.Equal(new[] { "Book", "Shelf" }, result.Entries.Select(e => e.Entity));
    }

    [Fact]
    public async Task An_erasure_deletes_a_record_and_the_records_that_require_it()
    {
        var personId = SeedPerson();
        SeedAlbum(personId);

        ErasureResult result;
        using (var context = CreateContext())
            result = await context.PersonalData().EraseAsync<Person>(personId);

        using var check = CreateContext();
        Assert.Empty(check.Set<Person>());
        Assert.Empty(check.Set<Album>());
        Assert.Empty(check.Set<Photo>());
        Assert.Empty(check.Set<Print>());
        Assert.Equal(4, result.Deleted);
        Assert.Equal(new[] { "Person", "Album", "Photo", "Print" }, result.Entries.Select(e => e.Entity));
    }

    [Fact]
    public async Task Each_record_is_still_offered_to_the_callback_in_plan_order()
    {
        var personId = SeedPerson();
        SeedAlbum(personId);

        var seen = new System.Collections.Generic.List<string>();
        using var context = CreateContext();
        await context.PersonalData().EraseAsync<Person>(personId, new ErasureOptions
        {
            OnRecord = (record, _) =>
            {
                seen.Add(record.Entity.Name);
                return Task.CompletedTask;
            },
        });

        Assert.Equal(new[] { "Person", "Album", "Photo", "Print" }, seen);
    }

    [Fact]
    public async Task A_record_that_requires_itself_or_an_earlier_record_of_its_type_is_deleted()
    {
        var personId = SeedPerson();
        using (var seed = CreateContext())
        {
            // The first reply answers itself; each later one answers the one before.
            seed.Add(new Reply { Id = 1, PersonId = personId, InReplyToId = 1, Text = "Jo wrote", SentAt = Old });
            seed.SaveChanges();
            seed.Add(new Reply { Id = 2, PersonId = personId, InReplyToId = 1, Text = "Jo wrote again", SentAt = Old });
            seed.Add(new Reply { Id = 3, PersonId = personId, InReplyToId = 2, Text = "Jo wrote more", SentAt = Old });
            seed.SaveChanges();
        }

        RetentionRunResult result;
        using (var context = CreateContext())
            result = await context.PersonalData().ApplyRetentionAsync(Policy("Threads"), Run);

        using var check = CreateContext();
        Assert.Empty(check.Set<Reply>());
        Assert.Equal(3, result.Deleted);
    }

    [Fact]
    public async Task Records_that_require_each_other_in_a_cycle_are_all_marked_for_deletion()
    {
        var personId = SeedPerson();
        using (var seed = CreateContext())
        {
            var first = new Reply { Id = 1, PersonId = personId, InReplyToId = 1, Text = "Jo wrote", SentAt = Old };
            seed.Add(first);
            seed.SaveChanges();
            seed.Add(new Reply { Id = 2, PersonId = personId, InReplyToId = 1, Text = "Jo wrote again", SentAt = Old });
            seed.SaveChanges();
            first.InReplyToId = 2;
            seed.SaveChanges();
        }

        // No database can remove such a pair one row at a time, so the changes are left for the caller to save.
        using var context = CreateContext();
        var result = await context.PersonalData().EraseAsync<Person>(personId, new ErasureOptions { SaveChanges = false });

        Assert.Equal(3, result.Deleted);
        Assert.All(context.ChangeTracker.Entries<Reply>(), e => Assert.Equal(EntityState.Deleted, e.State));
    }

    [Fact]
    public async Task A_record_is_still_kept_when_a_record_that_is_not_deleted_requires_it()
    {
        // The photo and its print are not due yet, so the album they require stays, without its personal data.
        var albumId = SeedAlbum(SeedPerson(), photoTakenAt: Seed.Now.AddYears(-1));

        RetentionRunResult result;
        using (var context = CreateContext())
            result = await context.PersonalData().ApplyRetentionAsync(Policy("Media"), Run);

        using var check = CreateContext();
        Assert.Equal("[erased]", check.Set<Album>().Single(a => a.Id == albumId).Title);
        Assert.Equal("Jo at the beach", check.Set<Photo>().Single().Caption);
        Assert.Single(check.Set<Print>());
        Assert.Equal(ErasureAction.Anonymise, Assert.Single(result.Entries).Action);
    }

    [Fact]
    public async Task A_dry_run_reports_the_same_entries_and_changes_nothing()
    {
        var personId = SeedPerson();
        SeedAlbum(personId);

        ErasureResult planned;
        using (var context = CreateContext())
            planned = await context.PersonalData().EraseAsync<Person>(personId, new ErasureOptions { DryRun = true });

        using (var check = CreateContext())
            Assert.Single(check.Set<Print>());

        ErasureResult applied;
        using (var context = CreateContext())
            applied = await context.PersonalData().EraseAsync<Person>(personId);

        Assert.Equal(
            applied.Entries.Select(e => (e.Entity, e.Key, e.Action)),
            planned.Entries.Select(e => (e.Entity, e.Key, e.Action)));
    }

    [DataSubject]
    public class Person
    {
        public int Id { get; set; }

        [PersonalData(PersonalDataCategory.DirectIdentifier)]
        public string Name { get; set; } = string.Empty;
    }

    [PersonalDataEntity(DataClass = "Media")]
    public class Album
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Title { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime CreatedAt { get; set; }
    }

    [PersonalDataEntity(DataClass = "Media")]
    public class Photo
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public int AlbumId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Caption { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime TakenAt { get; set; }
    }

    [PersonalDataEntity(DataClass = "Media")]
    public class Print
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public int PhotoId { get; set; }

        [NotPersonalData]
        public string Size { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime OrderedAt { get; set; }
    }

    [PersonalDataEntity(DataClass = "Library")]
    public class Shelf
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Label { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime AddedAt { get; set; }
    }

    [PersonalDataEntity(DataClass = "Library")]
    public class Book
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public int ShelfId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Inscription { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime AddedAt { get; set; }
    }

    [PersonalDataEntity(DataClass = "Threads")]
    public class Reply
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public int InReplyToId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Text { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public DateTime SentAt { get; set; }
    }

    private sealed class MediaContext : DbContext
    {
        public MediaContext(SqliteConnection connection)
            : base(new DbContextOptionsBuilder<MediaContext>().UseSqlite(connection).UseDataPrivacy().Options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Person>();

            // Every record belongs to the person, and requires another record that cannot go while it stays.
            modelBuilder.Entity<Album>().HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId);

            modelBuilder.Entity<Photo>().HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId);
            modelBuilder.Entity<Photo>().HasOne<Album>().WithMany().HasForeignKey(p => p.AlbumId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Print>().HasOne<Person>().WithMany().HasForeignKey(p => p.PersonId);
            modelBuilder.Entity<Print>().HasOne<Photo>().WithMany().HasForeignKey(p => p.PhotoId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Shelf>().HasOne<Person>().WithMany().HasForeignKey(s => s.PersonId);

            modelBuilder.Entity<Book>().HasOne<Person>().WithMany().HasForeignKey(b => b.PersonId);
            modelBuilder.Entity<Book>().HasOne<Shelf>().WithMany().HasForeignKey(b => b.ShelfId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Reply>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<Reply>().HasOne<Person>().WithMany().HasForeignKey(r => r.PersonId);
            modelBuilder.Entity<Reply>().HasOne<Reply>().WithMany().HasForeignKey(r => r.InReplyToId).OnDelete(DeleteBehavior.NoAction);
        }
    }
}
