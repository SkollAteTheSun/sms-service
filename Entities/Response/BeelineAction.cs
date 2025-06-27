using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class BeelineAction
{
    [JsonProperty("sms_group_id")]
    public string SmsGroupId { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("sms_type")]
    public string SmsType { get; set; }

    [JsonProperty("phone")]
    public string Phone { get; set; }

    [JsonProperty("sms_res_count")]
    public string SmsResCount { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }
}
