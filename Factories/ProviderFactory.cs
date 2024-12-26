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

    public IProvider GetProvider(string methodCode)
    {
        return methodCode.ToLower() switch
        {
            "smsru" => _serviceProvider.GetRequiredService<SmsRuProvider>(),
            "smsru2" => _serviceProvider.GetRequiredService<SmsRu2Provider>(),
            _ => throw new NotSupportedException($"Provider with code {methodCode} is not supported")
        };
    }
}