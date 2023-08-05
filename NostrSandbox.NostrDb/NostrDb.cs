using Microsoft.EntityFrameworkCore;

namespace NostrSandbox.NostrDb;

public record UserData
{
    public int Id { get; set; }
    public string PublicKey { get; set; } = "";
    public ICollection<ContactListEntry> Contacts { get; set; } = new List<ContactListEntry>();
}

public record ContactListEntry
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int ContactId { get; set; }
    public UserData? Owner { get; set; }
    public UserData? Contact { get; set; }
}

public class NostrDbContext : DbContext
{
    public const string DatabaseFileName = "nostrDatabase.db";
    public DbSet<UserData> Users { get; set; }
    public DbSet<ContactListEntry> Contacts { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={DatabaseFileName}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactListEntry>()
            .HasOne(entry => entry.Owner)
            .WithMany(entry => entry.Contacts)
            .HasForeignKey(entry => entry.OwnerId);
        
        modelBuilder.Entity<ContactListEntry>()
            .HasOne(entry => entry.Contact)
            .WithMany()
            .HasForeignKey(entry => entry.ContactId);
    }
}