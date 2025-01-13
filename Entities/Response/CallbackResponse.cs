using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Response;

public class CallbackResponse
{
    [Required]
    public required bool Status { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusText { get; set; }
}