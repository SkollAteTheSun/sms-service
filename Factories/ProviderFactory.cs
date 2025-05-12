using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Providers;

namespace Kp.Ms.Sms.Factories;

public class ProviderFactory
{
    private readonly IHttpClientWrapper _client;

    public ProviderFactory(IHttpClientWrapper client)
    {
        _client = client;
    }

    public Provider GetProvider(ProviderSettings settings)
    {
        return settings.Id switch
        {
            ProviderNames.SmsRu => new SmsRuProvider(_client, settings),
            ProviderNames.Megafon => new MegafonProvider(_client, settings),
            _ => throw new NotSupportedException($"Provider: {settings.Name} is not supported")
        };
    }
}