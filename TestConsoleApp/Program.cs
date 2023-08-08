// See https://aka.ms/new-console-template for more information

using TestConsoleApp;

List<string> relays = new()
{
	"wss://relay.damus.io",
	"wss://relay.nostr.band",
	"wss://relayable.org"
};

await using var clientGroup = new NostrClientGroup();
await clientGroup.ConnectToRelaysAsync(relays);

await clientGroup.SubscribeToContactListEventsAsync();
await clientGroup.WaitForContactListEose();
await clientGroup.CloseContactListSubscription();

await clientGroup.SubscribeToMessageEventsAsync();
await clientGroup.WaitForMessageEose();

FeedAnalyzer analyzer = new();
string topic = "food";
try
{
	var summary = await analyzer.GetPostsAboutATopicAsync(clientGroup.Feed, topic);
	Console.WriteLine(string.Join("\n\n", summary));
}
catch (Exception e)
{
	Console.WriteLine($"Exception thrown when creating the summary:");
	Console.WriteLine(e.Message);
}
