using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Factories;
using Kp.Ms.Sms.Providers;

namespace Kp.Ms.Sms.Config;

public class Organization
{
    public string Name { get; set; }
    public bool AutoSwitchProvider { get; set; } = false;
    public Dictionary<ProviderNames, ProviderSettings> ConfigureProviders { get; set; } = [];
    public ProviderNames DefaultProvider { get; set; }

    public ProviderSettings GetDefaultProviderSettings() => ConfigureProviders[DefaultProvider];

    public async Task<SmsResponse> SendSmsAsync(ProviderFactory factory, string phone, string message)
    {
        try
        {
            var defaultProvider = factory.GetProvider(ConfigureProviders[DefaultProvider]);
            var response = await defaultProvider.SendSmsAsync(phone, message);
            if (response.Status != SmsResponseStatus.OK.ToString() && AutoSwitchProvider)
            {
                throw new Exception();
            }
            return response;
        }
        catch (Exception ex)
        {
            var providerSettings = ConfigureProviders.Values
                .Where(settings => settings.Id != DefaultProvider)
                .ToList();

            if (!providerSettings.Any())
            {
                return new SmsResponse()
                {
                    Status = SmsResponseStatus.ERROR.ToString(),
                };
            }
            else
            {
                foreach (var settings in providerSettings)
                {
                    var provider = factory.GetProvider(settings);
                    var response = await provider.SendSmsAsync(phone, message);
                    if (response.Status == SmsResponseStatus.OK.ToString())
                    {
                        return response;
                    }
                    else
                    {
                        continue;
                    }
                }

                return new SmsResponse()
                {
                    Status = SmsResponseStatus.ERROR.ToString(),
                    StatusText = $"All attempts to send SMS failed"
                };
            }
        }
    }

    public async Task<CallResponse> CallApiAsync(ProviderFactory factory, string phone, string userIp)
    {
        try
        {
            var defaultProvider = factory.GetProvider(ConfigureProviders[DefaultProvider]);
            var response = await defaultProvider.CallApiAsync(phone, userIp);
            if (response.Status != SmsResponseStatus.OK.ToString())
            {
                throw new Exception();
            }
            return response;
        }
        catch (Exception ex)
        {
            var providerSettings = ConfigureProviders.Values
                .Where(settings => settings.Id != DefaultProvider)
                .ToList();

            if (!providerSettings.Any())
            {
                return new CallResponse()
                {
                    Status = SmsResponseStatus.ERROR.ToString(),
                };
            }
            else
            {
                foreach (var settings in providerSettings)
                {
                    var provider = factory.GetProvider(settings);
                    var response = await provider.CallApiAsync(phone, userIp);
                    if (response.Status == SmsResponseStatus.OK.ToString())
                    {
                        return response;
                    }
                    else
                    {
                        continue;
                    }
                }

                return new CallResponse()
                {
                    Status = SmsResponseStatus.ERROR.ToString(),
                    StatusText = $"All attempts to send SMS failed"
                };
            }
        }
    }
}
