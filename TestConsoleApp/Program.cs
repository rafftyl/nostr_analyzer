// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using NNostr.Client;

const string messageSubscription = "last-three-days";
const string contactListSubscription = "contact-lists";
const string contactListCacheFile = "contactLists.json";
const string userPublicKey = "npub1wvjwqk55d3n20qv06rq2e2qtvra3a90auv340mc6yzrnq0wsrp0qkdmy82";

HashSet<string> connectedRelays = new();
List<string> initialRelays = new()
{
    "wss://nostr.wine",
    "wss://nostr.bitcoiner.social",
    "wss://nos.lol",
    "wss://relay.current.fyi",
    "wss://relay.nostr.band",
    "wss://offchain.pub"
};

List<NostrClient> clients = new();
foreach (var relayUri in initialRelays)
{
    Console.WriteLine($"Connecting to relay {relayUri}...");
    var client = new NostrClient(new Uri(relayUri));
    await client.ConnectAndWaitUntilConnected();
    clients.Add(client);
    Console.WriteLine($"Connected to relay {relayUri}");
}

Console.WriteLine($"Finished connecting to relays");

bool hasCachedContactLists = File.Exists(contactListCacheFile);
Dictionary<string, List<string>>? contactListsPerUser = hasCachedContactLists 
    ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(contactListCacheFile))
    : new();
if (contactListsPerUser == null)
{
    Console.Error.WriteLine("Failed to deserialize contact list cache. Delete the file manually");
    return;
}

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
                lock (contactListsPerUser)
                {
                    foreach (var receivedEvent in arguments.events)
                    {
                        if (!contactListsPerUser.TryGetValue(receivedEvent.PublicKey, out var contactList))
                        {
                            contactList = new List<string>();
                            contactListsPerUser.Add(receivedEvent.PublicKey, contactList);
                        }

                        foreach (var tag in receivedEvent.Tags)
                        {
                            if (tag.TagIdentifier == "p")
                            {
                                contactList.Add(tag.Data[0]);
                            }
                        }
                    }
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

    if (!hasCachedContactLists)
    {
        contactListTasks.Add(client.CreateSubscription(contactListSubscription,
            new[]
            {
                new NostrSubscriptionFilter()
                {
                    Kinds = new[] { 3 }
                }
            }));
    }
}

if (!hasCachedContactLists)
{
    await Task.WhenAll(contactListTasks.ToArray());
    await Task.Run(() => contactListProcessingCountdown.Wait());

    List<Task> closeTasks = new();
    foreach (var client in clients)
    {
        closeTasks.Add(client.CloseSubscription(contactListSubscription));
    }
    await Task.WhenAll(closeTasks.ToArray());

    Console.WriteLine("Fetched contact lists. Caching...");
    await using var file = File.OpenWrite(contactListCacheFile);
    JsonSerializer.Serialize(file, contactListsPerUser);
    Console.WriteLine("Caching done");
}

if (!contactListsPerUser.TryGetValue(userPublicKey, out var userContactList))
{
    Console.Error.WriteLine($"No contact list found for user {userPublicKey}. Aborting.");
    return;
}

List<Task> messageSubscriptionTasks = new();
foreach (var client in clients)
{
    messageSubscriptionTasks.Add(client.CreateSubscription(messageSubscription,
        new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new[] { 1 },
                Since = DateTimeOffset.Now - TimeSpan.FromDays(3),
                Authors = userContactList.ToArray()
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
