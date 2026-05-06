using System.Text.Json.Serialization;

namespace DeepSeekCLI;

public record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream = false
);

public record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

public record ChatResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("message")] ChatMessage Message,
    [property: JsonPropertyName("done")] bool Done
);

public record StreamChatResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("message")] ChatMessage Message,
    [property: JsonPropertyName("done")] bool Done
);

public record ModelList(
    [property: JsonPropertyName("models")] List<ModelInfo> Models
);

public record ModelInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("model")] string Model
);
