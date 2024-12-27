using Kp.Ms.Sms.Services;
using Kp.Ms.Sms.Entities.Entity;
using Microsoft.Extensions.Options;

public class TimerService : IHostedService, IDisposable
{
    private readonly System.Timers.Timer _CallQueueTimer;
    private readonly System.Timers.Timer _CallCallbackQueueTimer;
    private readonly System.Timers.Timer _SmsQueueTimer;
    private readonly System.Timers.Timer _SmsCallbackQueueTimer;
    private readonly CallService _callService;
    private readonly SmsService _smsService;

    public TimerService(CallService callService, SmsService smsService, IOptions<QueueSettings> queueSettings)
    {
        _callService = callService;
        _smsService = smsService;

        _CallQueueTimer = new System.Timers.Timer(queueSettings.Value.CallQueueIntervalMs);
        _CallCallbackQueueTimer = new System.Timers.Timer(queueSettings.Value.CallCallbackQueueIntervalMs);
        _SmsQueueTimer = new System.Timers.Timer(queueSettings.Value.SmsQueueIntervalMs);
        _SmsCallbackQueueTimer = new System.Timers.Timer(queueSettings.Value.SmsCallbackQueueIntervalMs);

        _CallQueueTimer.Elapsed += async (sender, e) => await HandleCallQueue();
        _CallCallbackQueueTimer.Elapsed += async (sender, e) => await HandleCallCallbackQueue();
        _SmsQueueTimer.Elapsed += async (sender, e) => await HandleSmsQueue();
        _SmsCallbackQueueTimer.Elapsed += async (sender, e) => await HandleSmsCallbackQueue();

        _CallQueueTimer.Start();
        _CallCallbackQueueTimer.Start();
        _SmsQueueTimer.Start();
        _SmsCallbackQueueTimer.Start();
    }

    private async Task HandleCallQueue()
    {
        await _callService.ProcessQueue();
    }

    private async Task HandleCallCallbackQueue()
    {
        await _callService.ProcessCallbackQueue();
    }

    private async Task HandleSmsQueue()
    {
        await _smsService.ProcessQueue();
    }

    private async Task HandleSmsCallbackQueue()
    {
        await _smsService.ProcessCallbackQueue();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _CallQueueTimer.Stop();
        _CallCallbackQueueTimer.Stop();
        _SmsQueueTimer.Stop();
        _SmsCallbackQueueTimer.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _CallQueueTimer?.Dispose();
        _CallCallbackQueueTimer?.Dispose();
        _SmsQueueTimer?.Dispose();
        _SmsCallbackQueueTimer?.Dispose();
    }
}