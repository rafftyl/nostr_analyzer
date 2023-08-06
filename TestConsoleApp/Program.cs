// See https://aka.ms/new-console-template for more information

using System.Net.WebSockets;
using Microsoft.EntityFrameworkCore;
using NNostr.Client;
using NostrSandbox.NostrDb;

const string messageSubscription = "messages";
const string contactListSubscription = "contact-lists";
const string userPublicKey = "npub1wvjwqk55d3n20qv06rq2e2qtvra3a90auv340mc6yzrnq0wsrp0qkdmy82";

TimeSpan contactListTimespan = TimeSpan.FromDays(365);
TimeSpan messageListTimespan = TimeSpan.FromDays(3);

List<string> initialRelays = new()
{
    "wss://nostr.zebedee.cloud",
    "wss://nostr.bitcoiner.social",
    "wss://nos.lol",
    "wss://relay.plebstr.com",
    "wss://relay.current.fyi",
    "wss://offchain.pub",
    "wss://relay.nostr.band"
};

List<NostrClient> clients = new();
List<Task> connectTaskList = new();
foreach (var relayUri in initialRelays)
{
    Console.WriteLine($"Connecting to relay {relayUri}...");
    var client = new NostrClient(new Uri(relayUri));
    connectTaskList.Add(Task.Run(async () =>
    {
        try
        {
            await client.ConnectAndWaitUntilConnected();
        }
        catch (WebSocketException e)
        {
            Console.WriteLine(e.Message);
        }
    }));
    clients.Add(client);
    Console.WriteLine($"Connected to relay {relayUri}");
}

await Task.WhenAll(connectTaskList.ToArray());
Console.WriteLine($"Finished connecting to relays");

Console.WriteLine("Initializing database...");
string appDataLocalPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
string dataDirectory = Path.Combine(appDataLocalPath, "NostrSandbox");
if (!Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}
await using var dbContext = new NostrDbContext(dataDirectory);
dbContext.Database.EnsureCreated();

HashSet<string> processedMessageEvents = new();
CountdownEvent contactListProcessingCountdown = new(clients.Count);
CountdownEvent messageProcessingCountdown = new(clients.Count);
List<Task> contactListTasks = new();
foreach (var client in clients)
{
    client.EventsReceived += (object? sender, (string subscriptionId, NostrEvent[] events) arguments) =>
    {
        switch (arguments.subscriptionId)
        {
            case messageSubscription:
            {
                lock (processedMessageEvents)
                {
                    foreach (var receivedEvent in arguments.events)
                    {
                        if (processedMessageEvents.Contains(receivedEvent.Id))
                        {
                            continue;
                        }

                        Console.WriteLine($"Author: {receivedEvent.PublicKey}");
                        Console.WriteLine("Content:");
                        Console.WriteLine(receivedEvent.Content);
                        Console.WriteLine();

                        processedMessageEvents.Add(receivedEvent.Id);
                    }
                }

                break;
            }
            case contactListSubscription:
            {
                lock (dbContext)
                {
                    foreach (var receivedEvent in arguments.events)
                    {
                        UserData userData = dbContext.Users
                            .Include(user => user.Contacts)
                            .FirstOrDefault(user => user.PublicKey == receivedEvent.PublicKey);
                        if (userData == null)
                        {
                            Console.WriteLine($"Adding user {receivedEvent.PublicKey}");
                            userData = new UserData { PublicKey = receivedEvent.PublicKey };
                            dbContext.Users.Add(userData);
                        }

                        int newContactsAdded = 0;
                        var contactKeySet = receivedEvent.Tags
                            .Where(tag => tag.TagIdentifier == "p")
                            .Select(tag => tag.Data[0])
                            .ToHashSet();
                        foreach (var contactPublicKey in contactKeySet)
                        {
                            bool contactAlreadyOnContactList = userData.Contacts.Any(contactListEntry => contactListEntry.Contact.PublicKey == contactPublicKey);
                            if (contactAlreadyOnContactList)
                            {
                                continue;
                            }
                            
                            UserData contactUserData = dbContext.Users
                                .FirstOrDefault(user => user.PublicKey == receivedEvent.PublicKey);
                            if (contactUserData == null)
                            {
                                Console.WriteLine($"Adding user {contactPublicKey}");
                                contactUserData = new UserData() { PublicKey = contactPublicKey };
                                dbContext.Users.Add(contactUserData);
                            }

                            ++newContactsAdded;
                            userData.Contacts.Add(new ContactListEntry(){Contact = contactUserData});
                        }

                        if (newContactsAdded > 0)
                        {
                            Console.WriteLine($"Added {newContactsAdded} contacts to user {receivedEvent.PublicKey}");
                        }

                        int oldContactsRemoved = userData.Contacts
                            .RemoveAll(contactEntry => contactKeySet.Contains(contactEntry.Contact.PublicKey));

                        if (oldContactsRemoved > 0)
                        {
                            Console.WriteLine($"Removed {oldContactsRemoved} old contacts of user {receivedEvent.PublicKey}");
                        }
                    }

                    dbContext.SaveChanges();
                }
                break;
            }
        }
    };

    client.EoseReceived += (sender, subscriptionId) =>
    {
        switch (subscriptionId)
        {
            case contactListSubscription:
            {
                Console.WriteLine($"Received contact list EOSE from {(sender as NostrClient).Relay}");
                contactListProcessingCountdown.Signal();
                break;
            }
            case messageSubscription:
            {
                Console.WriteLine($"Received message EOSE from {(sender as NostrClient).Relay}");
                messageProcessingCountdown.Signal();
                break;
            }
        }
    };

    contactListTasks.Add(client.CreateSubscription(contactListSubscription,
        new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new[] { 3 },
                Since = DateTimeOffset.Now - contactListTimespan,
            }
        }));
}

await Task.WhenAll(contactListTasks.ToArray());
await Task.Run(() => contactListProcessingCountdown.Wait());
List<Task> closeTasks = new();
foreach (var client in clients)
{
    closeTasks.Add(client.CloseSubscription(contactListSubscription));
}
await Task.WhenAll(closeTasks.ToArray());

var user = dbContext.Users
    .Include(user => user.Contacts)
    .FirstOrDefault(user => user.PublicKey == userPublicKey);
if (user == null)
{
    Console.Error.WriteLine($"No contact list found for user {userPublicKey}. Aborting.");
    return;
}

var userContacts = user.Contacts.Select(entry => entry.Contact.PublicKey).ToArray();
List<Task> messageSubscriptionTasks = new();
foreach (var client in clients)
{
    messageSubscriptionTasks.Add(client.CreateSubscription(messageSubscription,
        new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new[] { 1 },
                Since = DateTimeOffset.Now - messageListTimespan,
                Authors = userContacts
            }
        }));
}
await Task.WhenAll(messageSubscriptionTasks.ToArray());
await Task.Run(() => messageProcessingCountdown.Wait());

foreach (var client in clients)
{
    client.Dispose();
}

Console.WriteLine("Finished operation.");
