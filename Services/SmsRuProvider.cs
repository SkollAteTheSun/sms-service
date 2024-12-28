namespace Kp.Ms.Sms.Services;

public class SmsRuProvider : SmsProviderBase
{
    public SmsRuProvider(IConfiguration configuration, HttpClient client)
        : base(configuration, client, "SmsRu:ApiId", "SmsRu:From", "SmsRu:Url")
    { }
}