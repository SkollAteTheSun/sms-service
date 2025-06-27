using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Response;

namespace Kp.Ms.Sms.Providers;

public abstract class Provider
{
    public string Name => Settings.Name;

    protected readonly IHttpClientWrapper Client;
    protected readonly ProviderSettings Settings;

    protected virtual string DefaultUserIp => "-1";
    protected Dictionary<string, string> DefaultHeaders = [];

    protected Provider(
        IHttpClientWrapper client,
        ProviderSettings settings)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public abstract Task<SmsResponse> SendSmsAsync(string phone, string message);

    public abstract Task<CallResponse> CallApiAsync(string phone, string? userIp);
}