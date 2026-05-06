using DeepSeekCLI;
using Spectre.Console;
using System.Text.RegularExpressions;

var client = new OllamaClient();
var history = new List<ChatMessage>();

var systemPrompt = """
You are a coding assistant. Use these tool tags:
- [TOOL:LIST path]
- [TOOL:READ path]
- [TOOL:WRITE path] followed by a ```code block```

Rules:
1. Use EXACTLY the tags above.
2. Provide COMPLETE code in WRITE blocks.
3. Stop immediately after a tool call.
Current Directory: {0}
""";

var formattedSystemPrompt = string.Format(systemPrompt, Directory.GetCurrentDirectory());
history.Add(new ChatMessage("system", formattedSystemPrompt));

AnsiConsole.Write(new FigletText("DeepSeek CLI").Centered().Color(Color.Cyan1));

List<string> models;
try { models = await client.ListModelsAsync(); }
catch { AnsiConsole.MarkupLine("[red]Error: Could not connect to Ollama.[/]"); return; }

var modelName = models.FirstOrDefault(m => m.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) ?? models[0];
AnsiConsole.MarkupLine($"[grey]Using model:[/] [cyan]{modelName}[/]");
AnsiConsole.WriteLine();

while (true)
{
    var prompt = AnsiConsole.Prompt(new TextPrompt<string>("[bold green]You:[/] ").AllowEmpty());
    if (string.IsNullOrWhiteSpace(prompt)) continue;
    if (prompt == "/clear") { history.Clear(); history.Add(new ChatMessage("system", formattedSystemPrompt)); AnsiConsole.Clear(); continue; }
    if (prompt == "/exit") break;

    history.Add(new ChatMessage("user", prompt));

    bool processingTools = true;
    while (processingTools)
    {
        processingTools = false;
        string fullResponse = "";

        await AnsiConsole.Status().StartAsync("Thinking...", async ctx =>
        {
            AnsiConsole.Markup("[bold cyan]DeepSeek:[/] ");
            await foreach (var chunk in client.ChatStreamAsync(modelName, history))
            {
                // INTERCEPT NATIVE DEEPSEEK TAGS
                if (chunk.Contains("<｜") || chunk.Contains("function")) 
                {
                    // If the model starts using its native format, we just print it in orange for visibility
                    // but we don't let it break our own parsing.
                    AnsiConsole.Write(new Text(chunk, new Style(Color.DarkOrange)));
                }
                else
                {
                    Console.Write(chunk);
                }
                
                fullResponse += chunk;
                
                // Break early if we see a complete custom tool tag to prevent yapping
                if (fullResponse.Contains("]") && (fullResponse.Contains("[TOOL:LIST") || fullResponse.Contains("[TOOL:READ"))) break;
            }
            Console.WriteLine();
        });

        if (string.IsNullOrWhiteSpace(fullResponse)) continue;
        history.Add(new ChatMessage("assistant", fullResponse));

        // 1. NATIVE INTERCEPTOR: If it used native tags like in the last error
        if (fullResponse.Contains("LIST") || fullResponse.Contains("READ") || fullResponse.Contains("WRITE"))
        {
            // Try to extract path even from the "broken" native format
            var nativeMatch = Regex.Match(fullResponse, @"(?:LIST|READ|WRITE)\s+([a-zA-Z0-9\.\-\\_:\s]+)", RegexOptions.IgnoreCase);
            if (nativeMatch.Success && !fullResponse.Contains("[TOOL:"))
            {
                var cmd = nativeMatch.Value.Trim().ToUpper();
                if (cmd.StartsWith("LIST")) fullResponse = $"[TOOL:LIST {nativeMatch.Groups[1].Value.Trim()}]";
                else if (cmd.StartsWith("READ")) fullResponse = $"[TOOL:READ {nativeMatch.Groups[1].Value.Trim()}]";
                else if (cmd.StartsWith("WRITE")) fullResponse = $"[TOOL:WRITE {nativeMatch.Groups[1].Value.Trim()}]";
            }
        }

        // 2. STANDARD PARSING
        var listMatch = Regex.Match(fullResponse, @"\[TOOL:LIST\s+(.+?)\]", RegexOptions.IgnoreCase);
        var readMatch = Regex.Match(fullResponse, @"\[TOOL:READ\s+(.+?)\]", RegexOptions.IgnoreCase);
        var writeMatch = Regex.Match(fullResponse, @"\[TOOL:WRITE\s+(.+?)\]\s*```[\s\S]*?\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (listMatch.Success)
        {
            var path = listMatch.Groups[1].Value.Trim().Replace("\"", "");
            try
            {
                var dir = string.IsNullOrWhiteSpace(path) ? "." : path;
                var result = string.Join("\n", Directory.GetFileSystemEntries(dir).Select(Path.GetFileName));
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Listed {dir}[/]");
                history.Add(new ChatMessage("system", $"Files:\n{result}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error LIST: {ex.Message}")); processingTools = true; }
        }
        else if (readMatch.Success)
        {
            var path = readMatch.Groups[1].Value.Trim().Replace("\"", "");
            try
            {
                var content = await File.ReadAllTextAsync(path);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Read {path}[/]");
                history.Add(new ChatMessage("system", $"Content:\n{content}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error READ: {ex.Message}")); processingTools = true; }
        }
        else if (writeMatch.Success)
        {
            var path = writeMatch.Groups[1].Value.Trim().Replace("\"", "");
            var content = writeMatch.Groups[2].Value;
            try
            {
                await File.WriteAllTextAsync(path, content);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Wrote {path}[/]");
                history.Add(new ChatMessage("system", $"Wrote {path} successfully."));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error WRITE: {ex.Message}")); processingTools = true; }
        }
        
        AnsiConsole.WriteLine();
    }
}
