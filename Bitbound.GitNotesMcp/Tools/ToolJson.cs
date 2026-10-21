using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitbound.GitNotesMcp.Tools;

internal static class ToolJson
{
  private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
