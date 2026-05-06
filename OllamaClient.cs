using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DeepSeekCLI;

public class OllamaClient(string baseUrl = "http://localhost:11434")
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };

    public async Task<List<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<ModelList>("/api/tags", cancellationToken);
        return response?.Models?.Select(m => m.Name).ToList() ?? new List<string>();
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(string model, List<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(model, messages, Stream: true)
        {
            Options = new Dictionary<string, object>
            {
                { "temperature", 0 },
                { "num_ctx", 8192 }, // Increased context window
                { "top_p", 0.9 },
                { "stop", new[] { "[TOOL:", "<|", "SYSTEM:", "<｜begin▁of▁sentence｜>", "User:", "You:" } }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? content = null;
            bool isDone = false;
            try 
            {
                var chunk = JsonSerializer.Deserialize<StreamChatResponse>(line);
                content = chunk?.Message?.Content;
                isDone = chunk?.Done ?? false;
            }
            catch (JsonException) { }

            if (content != null) yield return content;
            if (isDone) break;
        }
    }
}
