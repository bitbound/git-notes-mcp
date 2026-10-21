using Bitbound.GitNotesMcp.Configuration;
using Bitbound.GitNotesMcp.Git;
using Bitbound.GitNotesMcp.Notes;
using Bitbound.GitNotesMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

NotesOptions options;
try
{
  options = NotesOptions.FromEnvironment();
}
catch (NotesConfigurationException ex)
{
  Console.Error.WriteLine(ex.Message);
  return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio servers must send only JSON-RPC messages to stdout.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(options.LogLevel);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<GitCommandRunner>();
builder.Services.AddSingleton<GitRepository>();
builder.Services.AddSingleton<NotePathResolver>();
builder.Services.AddSingleton<NoteStore>();
builder.Services.AddSingleton<NoteService>();
builder.Services.AddSingleton<ZvecSearchBackend>();
builder.Services.AddSingleton<NoteSearchService>();

var mcp = builder.Services.AddMcpServer()
  .WithStdioServerTransport()
  .WithTools<NotesReadTools>()
  .WithTools<GitReadTools>();

if (!options.ReadOnly)
{
  mcp.WithTools<NotesWriteTools>()
    .WithTools<GitWriteTools>();
}

var host = builder.Build();
try
{
  await host.Services.GetRequiredService<GitRepository>().InitializeAsync();
}
catch (Exception ex)
{
  Console.Error.WriteLine(ex.Message);
  return 1;
}

await host.RunAsync();
return 0;
