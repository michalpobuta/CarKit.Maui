using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarKit.Maui;

/// <summary>What a <see cref="CarMessageRequest"/> asks the app to do.</summary>
public enum CarMessageRequestKind
{
    /// <summary>Send <see cref="CarMessageRequest.Text"/> to the conversation.</summary>
    Reply,

    /// <summary>Mark the conversation as read.</summary>
    MarkAsRead,
}

/// <summary>A request from the Intents Extension (Siri) to the app, queued in a <see cref="CarConversationStore"/>.</summary>
/// <param name="Kind">The request kind.</param>
/// <param name="ConversationId">The conversation identifier (<see cref="CarElement.Id"/> of the <see cref="CarConversationItem"/>).</param>
public sealed record CarMessageRequest(CarMessageRequestKind Kind, string ConversationId)
{
    /// <summary>Gets the reply text for <see cref="CarMessageRequestKind.Reply"/>.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// Conversations and requests shared by the app and its Intents Extension through a directory in the App Group
/// container: the app saves the conversations Siri may read, and the extension queues replies the app sends.
/// The type has no MAUI dependency, so the extension can reference <c>CarKit.Maui.Core</c> alone. Writes are atomic,
/// and each request is its own file, so both processes can use the directory at the same time.
/// </summary>
public sealed class CarConversationStore
{
    private const string ConversationsFile = "conversations.json";
    private const string RequestsDirectory = "requests";
    private const string RequestExtension = ".json";

    /// <summary>Creates the store.</summary>
    /// <param name="directory">The directory; created on the first write.</param>
    public CarConversationStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>Gets the store directory.</summary>
    public string Directory { get; }

    /// <summary>Creates the store the app and its Intents Extension share in an App Group container.</summary>
    /// <param name="containerPath">The App Group container path (<c>NSFileManager.GetContainerUrl(appGroup).Path</c>).</param>
    /// <returns>The store in the <c>CarKit</c> directory of the container.</returns>
    public static CarConversationStore ForAppGroupContainer(string containerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        return new CarConversationStore(Path.Combine(containerPath, "CarKit"));
    }

    /// <summary>
    /// Gets the Darwin notification the Intents Extension posts after <see cref="EnqueueAsync"/>, so a running app takes
    /// the request at once (<c>CFNotificationCenter.Darwin</c>).
    /// </summary>
    /// <param name="appGroup">The App Group identifier.</param>
    /// <returns>The notification name.</returns>
    public static string GetRequestsNotificationName(string appGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appGroup);
        return appGroup + ".carkit.requests";
    }

    private string Requests => Path.Combine(Directory, RequestsDirectory);

    /// <summary>Replaces the saved conversations. Images and handlers are not saved.</summary>
    /// <param name="conversations">The conversations.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public async Task SaveConversationsAsync(IEnumerable<CarConversationItem> conversations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        var document = new StoredConversations { Conversations = [.. conversations.Select(StoredConversation.From)] };
        await WriteAtomicallyAsync(Path.Combine(Directory, ConversationsFile), document, StoreJsonContext.Default.StoredConversations, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the saved conversations; a missing or unreadable file gives an empty list.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, without images and handlers.</returns>
    public async Task<IReadOnlyList<CarConversationItem>> LoadConversationsAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(Path.Combine(Directory, ConversationsFile), StoreJsonContext.Default.StoredConversations, cancellationToken)
            .ConfigureAwait(false);
        return document?.Conversations is { } stored ? [.. stored.Select(c => c.ToItem()).OfType<CarConversationItem>()] : [];
    }

    /// <summary>Queues a request for the app.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the request is written.</returns>
    public Task EnqueueAsync(CarMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId, nameof(request));
        // A name sortable by write time: requests are received in order
        var name = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + RequestExtension;
        var stored = new StoredRequest { Kind = request.Kind, ConversationId = request.ConversationId, Text = request.Text };
        return WriteAtomicallyAsync(Path.Combine(Requests, name), stored, StoreJsonContext.Default.StoredRequest, cancellationToken);
    }

    /// <summary>Takes all queued requests, oldest first; each request is returned once, also to concurrent callers.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The requests; unreadable files are removed and skipped.</returns>
    public async Task<IReadOnlyList<CarMessageRequest>> DequeueAllAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(Requests))
        {
            return [];
        }

        var result = new List<CarMessageRequest>();
        foreach (var file in System.IO.Directory.GetFiles(Requests, "*" + RequestExtension).Order(StringComparer.Ordinal))
        {
            // Taking over a file by renaming it: a second receiver (Darwin notification and app start at once) will not
            // get it
            var claimed = file + ".claimed-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Move(file, claimed);
            }
            catch (IOException)
            {
                continue;
            }

            try
            {
                if (await ReadAsync(claimed, StoreJsonContext.Default.StoredRequest, cancellationToken).ConfigureAwait(false) is { ConversationId: { Length: > 0 } id } stored)
                {
                    result.Add(new CarMessageRequest(stored.Kind, id) { Text = stored.Text });
                }
            }
            finally
            {
                File.Delete(claimed);
            }
        }

        return result;
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        System.IO.Directory.CreateDirectory(directory);
        // A temporary file without the .json extension: the request receiver will not see it before the rename
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var stream = File.Create(temporary);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, value, type, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<T?> ReadAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }
}

// Store file format (a contract between app and extension versions; changes only additive)
internal sealed class StoredConversations
{
    public List<StoredConversation>? Conversations { get; set; }
}

internal sealed class StoredPerson
{
    public string? Key { get; set; }

    public string? Name { get; set; }

    public string? PhoneOrEmail { get; set; }

    public static StoredPerson From(CarPerson person) => new() { Key = person.Key, Name = person.Name, PhoneOrEmail = person.PhoneOrEmail };

    public CarPerson? ToPerson() =>
        string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Name) ? null : new CarPerson(Key, Name) { PhoneOrEmail = PhoneOrEmail };
}

internal sealed class StoredMessage
{
    public string? Id { get; set; }

    public StoredPerson? Sender { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public bool IsRead { get; set; }
}

internal sealed class StoredConversation
{
    public string? Id { get; set; }

    public List<string>? Title { get; set; }

    public StoredPerson? Self { get; set; }

    public bool IsGroupConversation { get; set; }

    public List<StoredMessage>? Messages { get; set; }

    public static StoredConversation From(CarConversationItem item) => new()
    {
        Id = item.Id,
        Title = [.. item.Title.Variants],
        Self = StoredPerson.From(item.Self),
        IsGroupConversation = item.IsGroupConversation,
        Messages = [.. item.Messages.Select(m => new StoredMessage
        {
            Id = m.Id,
            Sender = m.Sender is null ? null : StoredPerson.From(m.Sender),
            Text = m.Text,
            Timestamp = m.Timestamp,
            IsRead = m.IsRead,
        })],
    };

    // An incomplete entry (e.g. from a newer version with a different shape) is skipped instead of aborting the read
    public CarConversationItem? ToItem()
    {
        if (string.IsNullOrWhiteSpace(Id) || Title is not { Count: > 0 } title || Self?.ToPerson() is not { } self)
        {
            return null;
        }

        var messages = (Messages ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id) && m.Text is not null)
            .Select(m => new CarConversationMessage(m.Id!, m.Sender?.ToPerson(), m.Text!, m.Timestamp) { IsRead = m.IsRead });
        return new CarConversationItem(Id, new CarText(title), self, messages) { IsGroupConversation = IsGroupConversation };
    }
}

internal sealed class StoredRequest
{
    public CarMessageRequestKind Kind { get; set; }

    public string? ConversationId { get; set; }

    public string? Text { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoredConversations))]
[JsonSerializable(typeof(StoredRequest))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;
