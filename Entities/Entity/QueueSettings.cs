namespace Kp.Ms.Sms.Entities.Entity;

public class QueueSettings
{
    public int SmsMaxSize { get; set; }
    public int SmsBatchSize { get; set; }
    public int SmsQueueIntervalMs { get; set; }
    public int SmsBatchIntervalMs { get; set; }
    public int SmsCallbackMaxSize { get; set; }
    public int SmsCallbackBatchSize { get; set; }
    public int SmsCallbackQueueIntervalMs { get; set; }
    public int SmsCallbackBatchIntervalMs { get; set; }
    public int MaxSmsAttempts { get; set; }
    public int CallMaxSize { get; set; }
    public int CallBatchSize { get; set; }
    public int CallQueueIntervalMs { get; set; }
    public int CallBatchIntervalMs { get; set; }
    public int CallCallbackMaxSize { get; set; }
    public int CallCallbackBatchSize { get; set; }
    public int CallCallbackQueueIntervalMs { get; set; }
    public int CallCallbackBatchIntervalMs { get; set; }
    public int MaxCallAttempts { get; set; }
}