using Kp.Ms.Sms.Entities.Enums;

namespace Kp.Ms.Sms.Services;
public class ProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<ServiceType, SmsProvider> _providerStates = new();

    public ProviderManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    public SmsProvider GetActiveProvider(ServiceType serviceType)
    {
        if (_providerStates.TryGetValue(serviceType, out var provider))
        {
            return provider;
        }
        return SmsProvider.SmsRu;
    }

    public bool SetActiveProvider(ServiceType serviceType, SmsProvider provider)
    {
        _providerStates[serviceType] = provider;
        return true;
    }
}