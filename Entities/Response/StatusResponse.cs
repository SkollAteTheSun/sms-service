using Kp.Ms.Sms.Entities.Enums;
using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Response;

public class StatusResponse : Versioning
{
    public StatusType Status { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}