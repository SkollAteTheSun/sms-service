using Kp.Ms.Sms.Entities.Entity;
using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Response;

public class SmsStatusResponse : Versioning
{
    public List<SmsLog> Statuses { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Error { get; set; }
}
