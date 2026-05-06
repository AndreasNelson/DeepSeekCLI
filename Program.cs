using DeepSeekCLI;
using Spectre.Console;
using System.Text.RegularExpressions;

var client = new OllamaClient();
var history = new List<ChatMessage>();

var systemPrompt = """
You are a coding assistant with access to:
- [TOOL:LIST path]
- [TOOL:READ path]
- [TOOL:WRITE path] followed by a ```code block```

Rules:
1. One tool per turn.
2. Stop immediately after a tool call.
3. Be concise.
Current Directory: {0}
""";

var formattedSystemPrompt = string.Format(systemPrompt, Directory.GetCurrentDirectory());
history.Add(new ChatMessage("system", formattedSystemPrompt));

AnsiConsole.Write(new FigletText("DeepSeek CLI").Centered().Color(Color.Cyan1));

List<string> models;
try { models = await client.ListModelsAsync(); }
catch { AnsiConsole.MarkupLine("[red]Error: Could not connect to Ollama.[/]"); return; }

var modelName = models.FirstOrDefault(m => m.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) ?? models[0];
AnsiConsole.MarkupLine($"[grey]Using model:[/] [cyan]{modelName}[/] [grey](Temp: 0)[/]");
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

        // Start the thinking status but write chunks directly to console for speed/reliability
        await AnsiConsole.Status().StartAsync("Thinking...", async ctx =>
        {
            AnsiConsole.Markup("[bold cyan]DeepSeek:[/] ");
            await foreach (var chunk in client.ChatStreamAsync(modelName, history))
            {
                // Write directly to standard output to avoid any Spectre formatting overhead during streaming
                Console.Write(chunk);
                fullResponse += chunk;
            }
            Console.WriteLine();
        });

        if (string.IsNullOrWhiteSpace(fullResponse)) continue;
        history.Add(new ChatMessage("assistant", fullResponse));

        // Tool Parsing
        var listMatch = Regex.Match(fullResponse, @"\[TOOL:LIST\s+(.+?)\]", RegexOptions.IgnoreCase);
        var readMatch = Regex.Match(fullResponse, @"\[TOOL:READ\s+(.+?)\]", RegexOptions.IgnoreCase);
        var writeMatch = Regex.Match(fullResponse, @"\[TOOL:WRITE\s+(.+?)\]\s*```[\s\S]*?\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (listMatch.Success)
        {
            var path = listMatch.Groups[1].Value.Trim().Replace("\"", "");
            try
            {
                var entries = Directory.GetFileSystemEntries(path);
                var result = string.Join("\n", entries.Select(f => Path.GetFileName(f) + (Directory.Exists(f) ? "/" : "")));
                AnsiConsole.MarkupLine($"[yellow]SYSTEM: Listed {path}[/]");
                history.Add(new ChatMessage("system", $"Files in {path}:\n{result}"));
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
                history.Add(new ChatMessage("system", $"Content of {path}:\n{content}"));
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
                history.Add(new ChatMessage("system", $"Successfully wrote to {path}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error WRITE: {ex.Message}")); processingTools = true; }
        }
        
        AnsiConsole.WriteLine();
    }
}
