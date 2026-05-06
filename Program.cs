using DeepSeekCLI;
using Spectre.Console;
using System.Text.RegularExpressions;

var client = new OllamaClient();
var history = new List<ChatMessage>();

var systemPrompt = """
You are an AI Software Engineer. You MUST use tools to interact with the system.

COMMANDS:
- LIST: [TOOL:LIST path]
- READ: [TOOL:READ path]
- WRITE: [TOOL:WRITE path] followed by a ```code block```

PROTOCOL:
1. State your plan.
2. Call a tool.
3. If you use [TOOL:WRITE], follow it IMMEDIATELY with the code block.
4. Stop after the tool call.

Example:
I will create a logger.
[TOOL:WRITE logger.py]
```python
print("logging")
```

Current Directory: {0}
""";

var formattedSystemPrompt = string.Format(systemPrompt, Directory.GetCurrentDirectory());
history.Add(new ChatMessage("system", formattedSystemPrompt));

AnsiConsole.Write(new FigletText("DeepSeek CLI").Centered().Color(Color.Cyan1));

List<string> models;
try { models = await client.ListModelsAsync(); }
catch { AnsiConsole.MarkupLine("[red]Error: Connection failed.[/]"); return; }

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
                Console.Write(chunk);
                fullResponse += chunk;
                
                // Break after a code block for WRITE tool
                if (fullResponse.Contains("```") && fullResponse.LastIndexOf("```") > fullResponse.IndexOf("```") + 3 && fullResponse.EndsWith("\n")) break;
            }
            Console.WriteLine();
        });

        if (string.IsNullOrWhiteSpace(fullResponse)) continue;
        history.Add(new ChatMessage("assistant", fullResponse));

        // 1. REGEX PARSING
        var listMatch = Regex.Match(fullResponse, @"\[TOOL:LIST\s+(.+?)\]", RegexOptions.IgnoreCase);
        var readMatch = Regex.Match(fullResponse, @"\[TOOL:READ\s+(.+?)\]", RegexOptions.IgnoreCase);
        var writeMatch = Regex.Match(fullResponse, @"\[TOOL:WRITE\s+(.+?)\]\s*```[\s\S]*?\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // 2. HEURISTIC FALLBACK (If model misses the [TOOL:...] tag but gives a filename and code)
        if (!writeMatch.Success)
        {
            var codeBlockMatch = Regex.Match(fullResponse, @"```[\s\S]*?\n([\s\S]*?)```");
            if (codeBlockMatch.Success)
            {
                // Try to find a filename mentioned in the text
                var fileMentionMatch = Regex.Match(fullResponse, @"(?:file|write to|save to|create)\s+[`""]?([\w\.\-/]+)[`""]?", RegexOptions.IgnoreCase);
                if (fileMentionMatch.Success)
                {
                    var path = fileMentionMatch.Groups[1].Value.Trim().Replace("\"", "");
                    var content = codeBlockMatch.Groups[1].Value;
                    try
                    {
                        await File.WriteAllTextAsync(path, content);
                        AnsiConsole.MarkupLine($"[yellow]SYSTEM: Heuristic match - Wrote {path}[/]");
                        history.Add(new ChatMessage("system", $"File {path} has been written via heuristic."));
                        processingTools = true;
                    }
                    catch { }
                }
            }
        }

        if (listMatch.Success)
        {
            var path = listMatch.Groups[1].Value.Trim().Replace("\"", "").Replace("[", "").Replace("]", "");
            try
            {
                var dir = string.IsNullOrWhiteSpace(path) || path == "path" ? "." : path;
                var result = string.Join("\n", Directory.GetFileSystemEntries(dir).Select(Path.GetFileName));
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Listed {dir}[/]");
                history.Add(new ChatMessage("system", $"RESULT of LIST {dir}:\n{result}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error LIST: {ex.Message}")); processingTools = true; }
        }
        else if (readMatch.Success)
        {
            var path = readMatch.Groups[1].Value.Trim().Replace("\"", "").Replace("[", "").Replace("]", "");
            try
            {
                var content = await File.ReadAllTextAsync(path);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Read {path}[/]");
                history.Add(new ChatMessage("system", $"RESULT of READ {path}:\n{content}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error READ: {ex.Message}")); processingTools = true; }
        }
        else if (writeMatch.Success)
        {
            var path = writeMatch.Groups[1].Value.Trim().Replace("\"", "").Replace("[", "").Replace("]", "");
            var content = writeMatch.Groups[2].Value;
            try
            {
                await File.WriteAllTextAsync(path, content);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Wrote {path}[/]");
                history.Add(new ChatMessage("system", $"SUCCESS: File {path} has been written."));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error WRITE: {ex.Message}")); processingTools = true; }
        }
        
        AnsiConsole.WriteLine();
    }
}
