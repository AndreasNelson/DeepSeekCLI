using DeepSeekCLI;
using Spectre.Console;
using System.Text.RegularExpressions;

var client = new OllamaClient();
var history = new List<ChatMessage>();

var systemPromptTemplate = """
You are a helpful AI coding assistant with access to the local file system.
Current Directory: {0}

You can use the following tools:

1. LIST: [TOOL:LIST path]
2. READ: [TOOL:READ path]
3. WRITE: [TOOL:WRITE path]
   ```
   content
   ```

CRITICAL: You MUST use the [TOOL:...] format. 
DO NOT use <|tool_calls|> or any other internal tags.
When you use a tool, STOP your response immediately.
""";

var systemPrompt = string.Format(systemPromptTemplate, Directory.GetCurrentDirectory());
history.Add(new ChatMessage("system", systemPrompt));

AnsiConsole.Write(new FigletText("DeepSeek CLI").Centered().Color(Color.Cyan1));

List<string> models;
try { models = await client.ListModelsAsync(); }
catch (Exception ex)
{
    AnsiConsole.MarkupLine("[red]Error:[/] Could not connect to Ollama.");
    AnsiConsole.WriteException(ex);
    return;
}

if (models.Count == 0) { AnsiConsole.MarkupLine("[yellow]Warning:[/] No models found."); return; }
var modelName = models.FirstOrDefault(m => m.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) ?? models[0];

AnsiConsole.MarkupLine("[grey]Connected to Ollama[/]");
AnsiConsole.MarkupLine($"[grey]Using model:[/] [cyan]{modelName}[/]");
AnsiConsole.WriteLine();

while (true)
{
    var prompt = AnsiConsole.Prompt(new TextPrompt<string>("[bold green]You:[/] ").AllowEmpty());
    if (string.IsNullOrWhiteSpace(prompt)) continue;
    if (prompt.Equals("/exit", StringComparison.OrdinalIgnoreCase)) break;
    if (prompt.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        history.Clear();
        history.Add(new ChatMessage("system", systemPrompt));
        AnsiConsole.Clear();
        continue;
    }

    history.Add(new ChatMessage("user", prompt));

    bool processingTools = true;
    while (processingTools)
    {
        processingTools = false; 
        string fullResponse = "";

        await AnsiConsole.Status().StartAsync("Thinking...", async ctx =>
        {
            AnsiConsole.Markup("[bold cyan]DeepSeek:[/] ");
            try 
            {
                await foreach (var chunk in client.ChatStreamAsync(modelName, history))
                {
                    AnsiConsole.Write(new Text(chunk));
                    fullResponse += chunk;
                }
            }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}"); }
            history.Add(new ChatMessage("assistant", fullResponse));
            AnsiConsole.WriteLine();
        });

        // Regex for the tools
        var listRegex = new Regex(@"\[TOOL:LIST\s+(.+?)\]", RegexOptions.IgnoreCase);
        var readRegex = new Regex(@"\[TOOL:READ\s+(.+?)\]", RegexOptions.IgnoreCase);
        var writeRegex = new Regex(@"\[TOOL:WRITE\s+(.+?)\]\s*```[\s\S]*?\n([\s\S]*?)```", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Native DeepSeek tag detection (to steer it back)
        if (fullResponse.Contains("<|tool_calls|>") || fullResponse.Contains("tool_calls"))
        {
            // Try to extract if it used native tags anyway
            var nativeMatch = Regex.Match(fullResponse, @"""name"":\s*""(.+?)""[\s\S]*?""path"":\s*""(.+?)""", RegexOptions.IgnoreCase);
            if (nativeMatch.Success)
            {
                var tool = nativeMatch.Groups[1].Value.ToLower();
                var path = nativeMatch.Groups[2].Value;
                if (tool == "list") fullResponse = $"[TOOL:LIST {path}]";
                else if (tool == "read") fullResponse = $"[TOOL:READ {path}]";
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]SYSTEM:[/] Model used invalid tags. Correcting...");
                history.Add(new ChatMessage("system", "Error: Please use the [TOOL:...] format only."));
                processingTools = true;
                continue;
            }
        }

        var listMatch = listRegex.Match(fullResponse);
        var readMatch = readRegex.Match(fullResponse);
        var writeMatch = writeRegex.Match(fullResponse);

        if (listMatch.Success)
        {
            var path = listMatch.Groups[1].Value.Trim();
            try 
            {
                var files = Directory.GetFileSystemEntries(path);
                var result = string.Join("\n", files.Select(f => Path.GetFileName(f) + (Directory.Exists(f) ? "/" : "")));
                AnsiConsole.MarkupLine($"[yellow]SYSTEM:[/] Listed [blue]{path}[/]");
                history.Add(new ChatMessage("system", $"Files in {path}:\n{result}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error LIST: {ex.Message}")); processingTools = true; }
        }
        else if (readMatch.Success)
        {
            var path = readMatch.Groups[1].Value.Trim();
            try 
            {
                var content = await File.ReadAllTextAsync(path);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM:[/] Read [blue]{path}[/]");
                history.Add(new ChatMessage("system", $"Content of {path}:\n{content}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error READ: {ex.Message}")); processingTools = true; }
        }
        else if (writeMatch.Success)
        {
            var path = writeMatch.Groups[1].Value.Trim();
            var content = writeMatch.Groups[2].Value;
            try 
            {
                await File.WriteAllTextAsync(path, content);
                AnsiConsole.MarkupLine($"[yellow]SYSTEM:[/] Wrote [blue]{path}[/]");
                history.Add(new ChatMessage("system", $"Success: Wrote {path}"));
                processingTools = true;
            }
            catch (Exception ex) { history.Add(new ChatMessage("system", $"Error WRITE: {ex.Message}")); processingTools = true; }
        }
        
        AnsiConsole.WriteLine();
    }
}
