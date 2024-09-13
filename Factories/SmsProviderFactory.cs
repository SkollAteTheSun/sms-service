using Kp.Ms.Sms.Interfaces;
using Kp.Ms.Sms.Services;

namespace Kp.Ms.Sms.Factories;

public class SmsProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public SmsProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ISmsProvider GetProvider(string methodCode)
    {
        return methodCode switch
        {
            "SmsRu" => _serviceProvider.GetRequiredService<SmsRuProvider>(),
            "SmsRu2" => _serviceProvider.GetRequiredService<SmsRu2Provider>(),
            _ => throw new NotSupportedException($"Provider with code {methodCode} is not supported")
        };
    }
}