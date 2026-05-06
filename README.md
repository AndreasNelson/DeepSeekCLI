# DeepSeek CLI (.NET)

A powerful, interactive command-line interface for DeepSeek models running locally via Ollama. Built with .NET 10 and Spectre.Console.

## Features

- **Local-First:** Communicates directly with your local Ollama instance.
- **Streaming Responses:** Real-time, character-by-character output.
- **File System Access:** The AI can read, list, and write files in the workspace to assist with coding tasks.
- **Chat History:** Maintains context throughout the session.
- **Interactive UI:** Rich terminal interface with progress indicators and syntax highlighting support via Spectre.Console.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Ollama](https://ollama.com/)
- DeepSeek models pulled in Ollama (e.g., `ollama pull deepseek-coder-v2:16b-lite-instruct-q4_K_M`)

## Getting Started

1. **Clone the repository:**
   ```bash
   git clone <your-repo-url>
   cd DeepSeekCLI
   ```

2. **Run the application:**
   ```bash
   dotnet run
   ```

3. **Usage:**
   - Simply type your request to the AI.
   - Use `/clear` to reset the conversation history.
   - Use `/exit` or `exit` to quit.

## File System Tools

The AI is programmed to use specific tags to interact with your files:
- `[TOOL:LIST path]` - Lists files in a directory.
- `[TOOL:READ path]` - Reads a file's content.
- `[TOOL:WRITE path]` - Creates or overwrites a file.

## Project Structure

- `Program.cs`: CLI loop, UI logic, and tool parsing.
- `OllamaClient.cs`: API communication with Ollama.
- `Models.cs`: DTOs for JSON serialization.
