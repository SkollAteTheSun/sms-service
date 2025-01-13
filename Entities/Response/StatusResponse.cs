using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Response;

public class StatusResponse
{
    [Required]
    public required string Status { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}