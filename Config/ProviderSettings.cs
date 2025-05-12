namespace Kp.Ms.Sms.Config;

public class ProviderSettings
{
    public Providers.ProviderNames Id { get; set; }
    public string Url { get; set; }
    public string FromNumber { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }

    public string Name => Id.ToString();
}
