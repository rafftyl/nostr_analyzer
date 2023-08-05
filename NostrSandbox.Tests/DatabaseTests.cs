using Microsoft.EntityFrameworkCore;
using NostrSandbox.NostrDb;

namespace NostrSandbox.Tests;

[TestFixture]
public class DatabaseTests
{
    [Test]
    public void DatabaseTest()
    {
        if (File.Exists(NostrDbContext.DatabaseFileName))
        {
            File.Delete(NostrDbContext.DatabaseFileName);
        }
        
        using var dbContext = new NostrDbContext();
        dbContext.Database.EnsureCreated();
        UserData user1 = new() { PublicKey = "user1_public_key" };
        UserData user2 = new() { PublicKey = "user2_public_key" };
        UserData user3 = new() { PublicKey = "user3_public_key" };

        dbContext.Users.Add(user1);
        dbContext.Users.Add(user2);
        dbContext.Users.Add(user3);
        
        user1.Contacts.Add(new ContactListEntry(){Contact = user2});
        user1.Contacts.Add(new ContactListEntry(){Contact = user3});
        user2.Contacts.Add(new ContactListEntry(){Contact = user3});
        user3.Contacts.Add(new ContactListEntry(){Contact = user1});
        dbContext.SaveChanges();
        
        var user1Contacts = dbContext.Contacts
            .Where(contactListEntry => contactListEntry.Owner.PublicKey == "user1_public_key")
            .Include(contactListEntry => contactListEntry.Contact).ToList();
        foreach (var contactListEntry in user1Contacts)
        {
            Assert.That(contactListEntry.Contact, Is.Not.Null);
            Console.WriteLine(contactListEntry.Contact.PublicKey);
        }
        Assert.That(user1Contacts, Has.Count.EqualTo(2));
    }
}