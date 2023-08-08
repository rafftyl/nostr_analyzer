// See https://aka.ms/new-console-template for more information

using TestConsoleApp;

//change this to your own public key (hex format)
const string userHexPublicKey = "7324e05a946c66a7818fd0c0aca80b60fb1e95fde32357ef1a2087303dd0185e";
List<string> relays = new()
{
	"wss://relay.damus.io",
	"wss://relay.nostr.band",
	"wss://relayable.org"
};

await using var clientGroup = new NostrConnection();
await clientGroup.ConnectToRelaysAsync(relays);

await clientGroup.SubscribeToContactListEventsAsync(userHexPublicKey, TimeSpan.FromDays(365));
await clientGroup.WaitForContactListEose();
await clientGroup.CloseContactListSubscription();

var timeSpan = TimeSpan.FromHours(1);
await clientGroup.SubscribeToMessageEventsAsync(userHexPublicKey, timeSpan);
await clientGroup.WaitForMessageEose();

//change those paths to point to your OpenAI key
//the contents of the file should look like:
//OPENAI_KEY: <your key here>
FeedAnalyzer analyzer = new("E:/OpenAI", "OpenAiApiKey.txt");
// string topic = "bitcoin";
// Console.WriteLine($"Analyzing {clientGroup.Feed.Count} posts from last {timeSpan}, looking for info about {topic}");
// var foundPosts = await analyzer.GetPostsAboutATopicAsync(clientGroup.Feed, topic);
//
// string appDataLocalPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
// string resultPath = Path.Combine(appDataLocalPath, "NostrSandbox", $"{topic}-from-{timeSpan.Days}-days-{timeSpan.Hours}-hours.txt");
// File.WriteAllText(resultPath, string.Join("\n\n===================\n\n", foundPosts.Select(post => post.Replace(@"\n", "\n"))));

try
{
	Console.WriteLine($"Summarizing {clientGroup.Feed.Count} posts.");
	//this motherfucker can hit ChatGPT's token limit very fast
	//try limiting the number of posts by changing timeSpan and discarding long notes
	string summary = await analyzer.SummarizeAsync(clientGroup.Feed.Where(post => post.Length < 500));
	Console.WriteLine(summary);
}
catch (Exception e)
{
	Console.WriteLine(e.Message);
}
