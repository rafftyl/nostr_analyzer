﻿using Microsoft.EntityFrameworkCore;

namespace NostrSandbox.NostrDb;

public record UserData
{
    public int Id { get; set; }
    public string PublicKey { get; set; } = "";
    public List<ContactListEntry> Contacts { get; set; } = new ();
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
    private string dbDirectory;
    
    public const string DatabaseFileName = "nostrDatabase.db";
    public DbSet<UserData> Users { get; set; }
    public DbSet<ContactListEntry> Contacts { get; set; }

    public NostrDbContext(string dbDirectory)
    {
        this.dbDirectory = dbDirectory;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={Path.Combine(dbDirectory, DatabaseFileName)}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserData>(builder => builder.HasIndex(user => user.PublicKey).IsUnique());
        
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