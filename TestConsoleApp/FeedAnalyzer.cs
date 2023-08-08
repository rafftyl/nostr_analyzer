using OpenAI_API;
using OpenAI_API.Chat;

namespace TestConsoleApp;

class FeedAnalyzer
{
	private readonly OpenAIAPI openAiApi;
	
	public FeedAnalyzer(string openAiKeyDirectory, string openAiKeyFile)
	{
		var authenticationStruct = APIAuthentication.LoadFromPath(openAiKeyDirectory, openAiKeyFile);
		openAiApi = new(authenticationStruct);
	}
	
	public async Task<bool> IsPostAboutTopicAsync(string post, string topic)
	{
		var chat = openAiApi.Chat.CreateConversation(new ChatRequest()
		{
			MaxTokens = 5,
		});

		string prompt = "You're an assistant helping me to browse my social media feed, " +
		                "checking if posts are about a given topic. " +
		                "I'll paste requests in the following format:\n\n" +
		                "Topic: <topic name goes here>\n" +
		                "Post:\n <here goes a social media post>\n\n" +
		                "Posts will be presented in the following format:\n" +
		                "Author: <a string of signs identifying the author of a given post>\n" +
		                "Date: <date>" + 
		                "PostId: <a unique identifier of a given post>\n" +
		                "Content:\n <the post's content (plain text)>\n\n" +
		                "In response, I want you to write \"yes\" if the post contains information about the topic " +
		                "and \"no\" otherwise";

		string initialResponse = "I understand. I will write \"yes\" if a given post contains information about the specified topic " +
		                         "and \"no\" otherwise";

		string request = $"Topic: {topic}]\nPost:\n{post}";

		chat.AppendUserInput(prompt);
		chat.AppendExampleChatbotOutput(initialResponse);
		chat.AppendUserInput(request);
		string result = await chat.GetResponseFromChatbotAsync();
		return result.ToLower() == "yes";
	}

	public async Task<List<string>> GetPostsAboutATopicAsync(IEnumerable<string> allPosts, string topic)
	{
		List<string> selectedPosts = new();
		var postList = allPosts.ToList();
		for (var postIndex = 0; postIndex < postList.Count; postIndex++)
		{
			var post = postList[postIndex];
			try
			{
				bool isAboutTopic = await IsPostAboutTopicAsync(post, topic);
				if (isAboutTopic)
				{
					Console.WriteLine($"Found a new post about {topic}!");
					selectedPosts.Add(post);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("Exception when analyzing post:");
				Console.WriteLine(e.Message);
				Console.WriteLine("Post content:");
				Console.WriteLine(post);
			}

			Console.WriteLine($"Processed {postIndex + 1} / {postList.Count} posts.");
		}

		return selectedPosts;
	}
	
	public async Task<string> SummarizeAsync(IEnumerable<string> posts)
	{
		var chat = openAiApi.Chat.CreateConversation(new ChatRequest()
		{
			MaxTokens = 1000,
		});

		string prompt = "You're an assistant helping me to browse my social media feed, " +
		                "writing summaries of posts from a given time period. " +
		                "I'll paste some posts in the following format:\n\n" +
		                "Author: <a string of signs identifying the author of a given post>\n" +
		                "Date: <date>" + 
		                "PostId: <a unique identifier of a given post>\n" +
		                "Content:\n <the post's content (plain text)>\n\n" +
		                "In response, I want you to write a short summary of everything you've learned from those posts.\n" +
		                "Skip any posts that don't provide any new information and just express attitudes like excitement, " +
		                "condemnation. When you mention a piece of info, try to provide a PostId from which it is sourced.";

		string initialResponse = "I understand. I will write short summaries based on the posts you provide, trying to stick to informative posts only.";
		string request = string.Join("\n\n", posts);
		
		chat.AppendUserInput(prompt);
		chat.AppendExampleChatbotOutput(initialResponse);
		chat.AppendUserInput(request);
		return await chat.GetResponseFromChatbotAsync();
	}
}