namespace Kp.Ms.Sms.Services;

public class SmsRu2Provider : SmsProviderBase
{
    public SmsRu2Provider(IConfiguration configuration, HttpClient client)
        : base(configuration, client, "SmsRu2:ApiId", "SmsRu2:From", "SmsRu2:Url")
    { }
}