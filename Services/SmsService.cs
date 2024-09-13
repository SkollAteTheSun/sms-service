using Kp.Ms.Sms.Factories;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly SmsProviderFactory _smsProviderFactory;
    private string _activeProvider;

    public SmsService(SmsProviderFactory smsProviderFactory, IConfiguration configuration)
    {
        _smsProviderFactory = smsProviderFactory;
        _activeProvider = configuration["ActiveSmsProvider"] ?? "SMSRU";
    }

    public async Task<string> SendSmsAsync(string phone, string message)
    {
        var provider = _smsProviderFactory.GetProvider(_activeProvider);
        var response = await provider.SendSmsAsync(phone, message);

        if (response.Status == "OK")
            return "success";
        return response.StatusText ?? "failure";
    }

    public bool SwitchProvider(string methodCode)
    {
        // Допустимые провайдеры
        var allowedProviders = new[] { "SMSRU", "SMSRU2" };
        if (!allowedProviders.Contains(methodCode))
            return false;

        _activeProvider = methodCode;
        return true;
    }

    public string GetActiveProvider()
    {
        return _activeProvider;
    }
}