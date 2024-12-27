namespace Kp.Ms.Sms.Entities.Entity;

public class CallbackItem
{
    public string CallbackUrl { get;}
    public object CallbackData { get;}
    public int Attempt { get; set; }
    public CallbackItem(string callbackUrl, object callbackData, int attempt)
    {
        CallbackUrl = callbackUrl;
        CallbackData = callbackData;
        Attempt = attempt;
    }
}
