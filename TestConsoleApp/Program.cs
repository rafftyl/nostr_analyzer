// See https://aka.ms/new-console-template for more information

List<string> initialRelays = new()
{
	"wss://relay.damus.io",
	"wss://relay.nostr.band",
	"wss://relayable.org"
};

await using var clientGroup = new NostrClientGroup();
await clientGroup.ConnectToRelaysAsync(initialRelays);

await clientGroup.SubscribeToContactListEventsAsync();
await clientGroup.WaitForContactListEose();
await clientGroup.CloseContactListSubscription();

await clientGroup.SubscribeToMessageEventsAsync();
await clientGroup.WaitForMessageEose();

Console.WriteLine("Finished operation.");