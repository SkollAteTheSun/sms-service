using Common.HttpClientWrapper.Attributes;

namespace Kp.Ms.Sms.Entities.Request;

public class SmsRuRequest
{
    [FormProperty("from")]
    public string From { get; set; }

    [FormProperty("api_id")]
    public string ApiId { get; set; }

    [FormProperty("to")]
    public string To { get; set; }

    [FormProperty("json")]
    public string Json { get; set; }

    [FormProperty("msg")]
    public string Message { get; set; }
}
