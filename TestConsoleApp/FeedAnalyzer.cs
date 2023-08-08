using OpenAI_API;
using OpenAI_API.Chat;

namespace TestConsoleApp;

class FeedAnalyzer
{
	public async Task<string> AnalyzePost(string post, string topic)
	{
		var authenticationStruct = APIAuthentication.LoadFromPath("E:/OpenAI", "OpenAiApiKey.txt");
		OpenAIAPI openAiApi = new(authenticationStruct);
		var chat = openAiApi.Chat.CreateConversation(new ChatRequest()
		{
			MaxTokens = 5,
		});

		string prompt = "You're an assistant helping me to browse my social media feed, " +
		                "providing summaries of all posts about a given topic. " +
		                "I'll paste requests in the following format:\n\n" +
		                "Topic: <topic name goes here>\n" +
		                "Post:\n <here goes a social media post>\n\n" +
		                "Posts will be presented in the following format:\n" +
		                "Author: <a string of signs identifying the author of a given post>\n" +
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
		return await chat.GetResponseFromChatbotAsync();
	}

	public async Task<List<string>> GetPostsAboutATopicAsync(IEnumerable<string> allPosts, string topic)
	{
		List<string> selectedPosts = new();
		foreach (var post in allPosts)
		{
			string result = await AnalyzePost(post, topic);
			if (result.ToLower() == "yes")
			{
				Console.WriteLine($"Found a new post about {topic}!");
				selectedPosts.Add(post);
			}
		}

		return selectedPosts;
	}
}