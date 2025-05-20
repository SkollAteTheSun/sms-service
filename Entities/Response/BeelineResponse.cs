using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class BeelineResponse
{
    [JsonProperty("actions")]
    public List<BeelineAction>? Actions { get; set; }

    [JsonProperty("agt_id")]
    public string? AgentId { get; set; }

    [JsonProperty("error")]
    public BeelineError? Error { get; set; }

    [JsonProperty("date_report")]
    public string ReportDate { get; set; }
}
