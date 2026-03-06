using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class DiagnosedFault
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("faultId")]
    [JsonProperty("faultId")]
    public string FaultId { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("symptoms")]
    [JsonProperty("symptoms")]
    public List<string> Symptoms { get; set; } = [];

    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("confidence")]
    [JsonProperty("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("diagnosedAt")]
    [JsonProperty("diagnosedAt")]
    public DateTimeOffset DiagnosedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("recommendedAction")]
    [JsonProperty("recommendedAction")]
    public string? RecommendedAction { get; set; }
}
