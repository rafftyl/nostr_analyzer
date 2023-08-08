using TestConsoleApp;

//change this to your own public key (hex format)
const string userHexPublicKey = "7324e05a946c66a7818fd0c0aca80b60fb1e95fde32357ef1a2087303dd0185e";
List<string> relays = new()
{
	"wss://relay.damus.io",
	"wss://relay.nostr.band",
	"wss://relayable.org"
};

await using var nostrConnection = new NostrConnection();
await nostrConnection.ConnectToRelaysAsync(relays);

await nostrConnection.SubscribeToContactListEventsAsync(userHexPublicKey, TimeSpan.FromDays(365));
await nostrConnection.WaitForContactListEose();
await nostrConnection.CloseContactListSubscription();

var timeSpan = TimeSpan.FromDays(3);
await nostrConnection.SubscribeToMessageEventsAsync(userHexPublicKey, timeSpan);
await nostrConnection.WaitForMessageEose();
await nostrConnection.CloseMessageSubscription();

//change those paths to point to your OpenAI key
//the contents of the file should look like:
//OPENAI_KEY: <your key here>
FeedAnalyzer analyzer = new("E:/OpenAI", "OpenAiApiKey.txt");
var mode = Mode.Summarize;
switch (mode)
{
	case Mode.Summarize:
	{
		async Task SummarizeAndPrintToConsole(List<string> postsToSummarize)
		{
			try
			{
				string summary = await analyzer.SummarizeAsync(postsToSummarize);
				Console.WriteLine(summary);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
			
			Console.WriteLine();
		}

		Console.WriteLine($"Summarizing {nostrConnection.Feed.Count} posts.");

		//this motherfucker can hit ChatGPT's token limit very fast
		//so the feed is chunked and chunks get summarized separately
		const int maxSignsInChunk = 5000;
		int currentLength = 0;
		List<string> postBuffer = new();
		foreach (var post in nostrConnection.Feed)
		{
			if (post.Length > maxSignsInChunk)
			{
				continue;
			}

			currentLength += post.Length;
			if (currentLength >= maxSignsInChunk)
			{
				await SummarizeAndPrintToConsole(postBuffer);
				postBuffer.Clear();
				currentLength = 0;
			}
			postBuffer.Add(post);
		}
		
		if (postBuffer.Count > 0)
		{
			await SummarizeAndPrintToConsole(postBuffer);
		}
		break;
	}
	case Mode.FindNodes:
	{
		string topic = "bitcoin";
		Console.WriteLine($"Analyzing {nostrConnection.Feed.Count} posts from last {timeSpan}, looking for info about {topic}");
		var foundPosts = await analyzer.GetPostsAboutATopicAsync(nostrConnection.Feed, topic);
		string resultPath = $"{topic}-from-{timeSpan.Days}-days-{timeSpan.Hours}-hours.txt";
		File.WriteAllText(resultPath, string.Join("\n\n===================\n\n", 
			foundPosts.Select(post => post.Replace(@"\n", "\n"))));
		break;
	}
	default:
		throw new ArgumentOutOfRangeException();
}

enum Mode
{
	Summarize,
	FindNodes
}