using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NNostr.Client;
using NostrSandbox.NostrDb;

namespace TestConsoleApp;

public class NostrClientGroup : IDisposable, IAsyncDisposable
{
	private const string MessageSubscriptionName = "messages";
	private const string ContactListSubscriptionName = "contact-lists";
	private const string UserPublicKey = "7324e05a946c66a7818fd0c0aca80b60fb1e95fde32357ef1a2087303dd0185e";

	private readonly List<NostrClient> clients = new();
	private readonly NostrDbContext dbContext;

	private readonly HashSet<string> processedMessageEvents = new();
	private CountdownEvent? contactListProcessingCountdown;
	private CountdownEvent? messageProcessingCountdown;

	public List<string> Feed { get; } = new();

	public NostrClientGroup()
	{
		Console.WriteLine("Initializing database...");
		string appDataLocalPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string dataDirectory = Path.Combine(appDataLocalPath, "NostrSandbox");
		if (!Directory.Exists(dataDirectory))
		{
			Directory.CreateDirectory(dataDirectory);
		}
		dbContext = new NostrDbContext(dataDirectory);
		dbContext.Database.EnsureCreated();
	}

	public async Task ConnectToRelaysAsync(IEnumerable<string> relayAddresses)
	{
		List<Task> connectTaskList = new();
		foreach (var relayUri in relayAddresses)
		{
			Console.WriteLine($"Connecting to relay {relayUri}...");
			var client = new NostrClient(new Uri(relayUri));
			connectTaskList.Add(Task.Run(async () =>
			{
				try
				{
					await client.ConnectAndWaitUntilConnected();
					clients.Add(client);
					Console.WriteLine($"Connected to relay {relayUri}");
				}
				catch (WebSocketException e)
				{
					Console.WriteLine($"Error from {relayUri}: \"{e.Message}\"");
				}
			}));
		}
		await Task.WhenAll(connectTaskList.ToArray());
		Console.WriteLine($"Finished connecting to relays");
	}

	public async Task SubscribeToContactListEventsAsync(TimeSpan contactListTimespan)
	{
		List<Task> contactListTasks = new();
		contactListProcessingCountdown?.Dispose();
		contactListProcessingCountdown = new CountdownEvent(clients.Count);
		foreach (var client in clients)
		{
			client.EventsReceived += (_, arguments) =>
			{
				if (arguments.subscriptionId != ContactListSubscriptionName)
				{
					return;
				}
				
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
						else
						{
							userData.Contacts.Clear();
						}

						var contactPublicKeys = receivedEvent.Tags
							.Where(tag => tag.TagIdentifier == "p")
							.Select(tag => tag.Data[0])
							.ToList();
						Console.WriteLine($"Received contact list");
						Console.WriteLine(string.Join('\n', contactPublicKeys));
						foreach (var contactPublicKey in contactPublicKeys)
						{
							UserData contactUserData = dbContext.Users
								.FirstOrDefault(user => user.PublicKey == contactPublicKey);
							if (contactUserData == null)
							{
								Console.WriteLine($"Adding user {contactPublicKey}");
								contactUserData = new UserData() { PublicKey = contactPublicKey };
								dbContext.Users.Add(contactUserData);
							}

							userData.Contacts.Add(new ContactListEntry() { Contact = contactUserData });
						}
					}

					dbContext.SaveChanges();
				}
			};

			client.EoseReceived += (sender, subscriptionId) =>
			{
				if (subscriptionId != ContactListSubscriptionName)
				{
					return;
				}
				
				Console.WriteLine($"Received contact list EOSE from {(sender as NostrClient).Relay}");
				contactListProcessingCountdown.Signal();
			};

			contactListTasks.Add(client.CreateSubscription(ContactListSubscriptionName,
				new[]
				{
					new NostrSubscriptionFilter()
					{
						Kinds = new[] { 3 },
						Since = DateTimeOffset.Now - contactListTimespan,
						Authors = new[] { UserPublicKey }
					}
				}));
		}

		await Task.WhenAll(contactListTasks.ToArray());
	}
	
	public async Task SubscribeToMessageEventsAsync(TimeSpan messageListTimespan)
	{
		List<Task> messageListTasks = new();
		messageProcessingCountdown?.Dispose();
		messageProcessingCountdown = new CountdownEvent(clients.Count);
		
		foreach (var client in clients)
		{
			client.EventsReceived += (_, arguments) =>
			{
				if (arguments.subscriptionId != MessageSubscriptionName)
				{
					return;
				}
				
				lock (processedMessageEvents)
				{
					foreach (var receivedEvent in arguments.events)
					{
						if (processedMessageEvents.Contains(receivedEvent.Id))
						{
							continue;
						}

						StringBuilder builder = new();

						builder.AppendLine($"Author: {receivedEvent.PublicKey}");
						builder.AppendLine($"PostId: {receivedEvent.Id}");
						builder.AppendLine("Content:");
						builder.AppendLine(receivedEvent.Content);

						processedMessageEvents.Add(receivedEvent.Id);
						Feed.Add(builder.ToString());
					}
				}
			};

			client.EoseReceived += (sender, subscriptionId) =>
			{
				if (subscriptionId != MessageSubscriptionName)
				{
					return;
				}
				Console.WriteLine($"Received message EOSE from {(sender as NostrClient).Relay}");
				messageProcessingCountdown.Signal();
			};

			var user = dbContext.Users
				.Include(user => user.Contacts)
				.FirstOrDefault(user => user.PublicKey == UserPublicKey);
			if (user == null)
			{
				Console.Error.WriteLine($"No contact list found for user {UserPublicKey}. Aborting.");
				return;
			}
			
			var userContacts = user.Contacts.Select(entry => entry.Contact.PublicKey).ToArray();
			messageListTasks.Add(client.CreateSubscription(MessageSubscriptionName,
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
		
		await Task.WhenAll(messageListTasks.ToArray());
	}

	public async Task WaitForContactListEose()
	{
		if (contactListProcessingCountdown == null)
		{
			return;
		}
		
		await Task.Run(() => contactListProcessingCountdown.Wait());
	}
	
	public async Task WaitForMessageEose()
	{
		if (messageProcessingCountdown == null)
		{
			return;
		}
		
		await Task.Run(() => messageProcessingCountdown.Wait());
	}

	public async Task CloseContactListSubscription()
	{
		await CloseSubscription(ContactListSubscriptionName);
	}
	
	public async Task CloseMessageListSubscription()
	{
		await CloseSubscription(MessageSubscriptionName);
	}

	public void Dispose()
	{
		contactListProcessingCountdown?.Dispose();
		messageProcessingCountdown?.Dispose();
		foreach(var client in clients)
		{
			client.Dispose();
		}
		
		dbContext.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		contactListProcessingCountdown?.Dispose();
		messageProcessingCountdown?.Dispose();
		foreach(var client in clients)
		{
			client.Dispose();
		}
		await dbContext.DisposeAsync();
	}
	
	private async Task CloseSubscription(string subscription)
	{
		List<Task> closeTasks = new();
		foreach (var client in clients)
		{
			closeTasks.Add(client.CloseSubscription(subscription));
		}
		await Task.WhenAll(closeTasks.ToArray());
	}
}