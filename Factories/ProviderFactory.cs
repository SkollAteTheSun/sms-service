using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Interfaces;
using Kp.Ms.Sms.Services;

namespace Kp.Ms.Sms.Factories;

public class ProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IProvider GetProvider(SmsProvider provider)
    {
        return provider switch
        {
            SmsProvider.SmsRu => _serviceProvider.GetRequiredService<SmsRuProvider>(),
            SmsProvider.SmsRu2 => _serviceProvider.GetRequiredService<SmsRu2Provider>(),
            _ => throw new NotSupportedException($"Provider: {provider} is not supported")
        };
    }
}