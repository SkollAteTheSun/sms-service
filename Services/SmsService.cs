using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly ConcurrentQueue<SmsRequest> _smsQueue;
    private readonly ConcurrentQueue<CallbackItem> _smsCallbackQueue;
    private readonly QueueSettings _queueSettings;
    private readonly HttpClient _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private IConfiguration _configuration;
    private readonly ProviderManager _providerManager;
    private readonly ProviderFactory _providerFactory;
    private SmsProvider _activeProvider;
    private readonly ValidationService _validationService;

    public SmsService(ProviderManager providerManager, ProviderFactory providerFactory, ValidationService validationService, IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient, IOptions<QueueSettings> queueSettings)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _validationService = validationService;
        _providerManager = providerManager;
        _providerFactory = providerFactory;
        _activeProvider = _providerManager.GetActiveProvider(ServiceType.Sms);
        _queueSettings = queueSettings.Value;
        _smsQueue = new ConcurrentQueue<SmsRequest>();
        _smsCallbackQueue = new ConcurrentQueue<CallbackItem>();
    }

    public async Task<string> SendSmsAsync(SmsRequest request)
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        request.MessId = GenerateMessageId();
        request.Phone = _validationService.CleanPhoneNumber(request.Phone);

        if (!_validationService.ValidPhoneNumber(request.Phone)) return "Error: Invalid phone nubmer";

        if (!string.IsNullOrEmpty(request.CallbackUrl) && !_validationService.ValidUrl(request.CallbackUrl)) return "Error: Invalid callback URL";

        var response = await provider.SendSmsAsync(request.Phone, request.Message);

        // Отправка смс прошла успешна
        if (response.Status == "OK")
        {
            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                message = request.Message,
                messId = request.MessId,
                status = response.Status,
                errorMessage = response.StatusText
            });

            await LogSmsToOpenSearch(new SmsLogRequest(request.Phone, request.Message, response.Status, request.MessId, null));
            return StatusType.Success.ToString();
        }

        if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
        {
            // Если очередь переплнена, возвращаем ошибку
            if (!EnqueueSms(request))
            {
                await LogSmsToOpenSearch(new SmsLogRequest(request.Phone, request.Message, StatusType.Failure.ToString(), request.MessId, "500: Queue limit reached"));
                return "500: Queue limit reached";
            }

            // Есть место в очереди - добавляем в очередеь

            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                message = request.Message,
                messId = request.MessId,
                status = response.Status,
                errorMessage = response.StatusText
            });

            await LogSmsToOpenSearch(new SmsLogRequest(request.Phone, request.Message, StatusType.Queued.ToString(), request.MessId, "No route to host"));
            return StatusType.Queued.ToString();
        }

        // Непредвиденные ошибки
        await LogSmsToOpenSearch(new SmsLogRequest(request.Phone, request.Message, response.Status, request.MessId, response.StatusText));
        return response.Status;
    }

    public async Task ProcessQueue()
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        var batch = new List<SmsRequest>();
        for (int i = 0; i < _queueSettings.SmsBatchSize && _smsQueue.TryDequeue(out SmsRequest request); i++)
        {
            batch.Add(request);
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var request in batch)
        {
            var response = await provider.SendSmsAsync(request.Phone, request.Message);

            var logRequest = new SmsLogRequest(
                phone: request.Phone,
                textMessage: request.Message,
                status : string.Empty,
                messId: request.MessId,
                errorMessage: string.Empty
            );

            if (response.Status == "OK")
            {

                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    message = request.Message,
                    messId = request.MessId,
                    status = response.Status,
                    errorMessage = response.StatusText
                });

                logRequest.Status = StatusType.Success.ToString();
                logRequest.ErrorMessage = "Successful sending of call from queue";
            }
            else
            {
                if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
                {
                    if (!EnqueueSms(request))
                    {
                        logRequest.Status = StatusType.Failure.ToString();
                        logRequest.ErrorMessage = "500: Queue limit reached";
                    }
                }
                else
                {
                    logRequest.Status = StatusType.Failure.ToString();
                    logRequest.ErrorMessage = response.StatusText;
                }
            }

            await Task.Delay(_queueSettings.SmsBatchIntervalMs);
        }
    }

    private bool EnqueueSms(SmsRequest request)
    {
        if (_smsQueue.Count >= _queueSettings.SmsMaxSize)
        {
            return false;
        }
        _smsQueue.Enqueue(request);
        return true;
    }

    public async Task ProcessCallbackQueue()
    {
        var batch = new List<CallbackItem>();

        for (int i = 0; i < _queueSettings.SmsCallbackBatchSize && _smsCallbackQueue.TryDequeue(out var callbackItem); i++)
        {
            batch.Add(callbackItem);
        }

        if (batch.Count == 0)
        {
            return;
        }

        var tasks = batch.Select(async item =>
        {
            if (item.Attempt >= _queueSettings.MaxSmsAttempts)
            {
                await LogSmsToOpenSearch(new SmsLogRequest(null, null, StatusType.Failure.ToString(), null, $"Callback to url: {item.CallbackUrl} failed after {item.Attempt} attempts. Removing from queue."));
                return;
            }

            var (success, statusText) = await SendCallback(item.CallbackUrl, item.CallbackData, item.Attempt);

            if (!success)
            {
                if (_smsCallbackQueue.Count >= _queueSettings.SmsCallbackMaxSize)
                {
                    await LogSmsToOpenSearch(new SmsLogRequest(null, null, StatusType.Failure.ToString(), null, $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {item.CallbackUrl}"));
                    return;
                }
            }
            else
            {
                await LogSmsToOpenSearch(new SmsLogRequest(null, null, StatusType.Success.ToString(), null, $"Sending callback from queue to {item.CallbackUrl} was successful."));
            }

            await Task.Delay(_queueSettings.SmsCallbackBatchIntervalMs);
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnqueueCallback(string callbackUrl, object callbackData)
    {
        if (string.IsNullOrEmpty(callbackUrl))
            return;

        if (_smsCallbackQueue.Count >= _queueSettings.SmsCallbackMaxSize)
        {
            await LogSmsToOpenSearch(new SmsLogRequest(null, null, StatusType.Failure.ToString(), null, $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {callbackUrl}"));
            return;
        }
        _smsCallbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, 1));
    }

    private async Task<(bool Success, string? StatusText)> SendCallback(string callbackUrl, object callbackData, int attempt)
    {
        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(callbackUrl, jsonContent);
            response.EnsureSuccessStatusCode();
            return (true, null);
        }
        catch (Exception ex)
        {
            if (_smsCallbackQueue.Count >= _queueSettings.SmsCallbackMaxSize)
            {
                return (false, "Callback queue limit reached");
            }
            _smsCallbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, 1));
            return (false, $"Callback failed and added to queue: {ex.Message}");
        }
    }

    private async Task LogSmsToOpenSearch(SmsLogRequest logRequest)
    {
        SmsLog log = new SmsLog
        {
            MessId = logRequest.MessId,
            Phone = logRequest.Phone,
            TextMessage = logRequest.TextMessage,
            Date = DateTime.UtcNow,
            Status = logRequest.Status,
            Provider = _activeProvider.ToString(),
            ErrorMessage = logRequest.ErrorMessage
        };

        var response = await _openSearchClient.IndexAsync(log, idx => idx.Index(_configuration.GetSmsStorageName()));

        if (!response.IsValid)
        {
            Console.WriteLine("Error logging SMS to OpenSearch: " + response.ServerError.Error.Reason);
        }
    }

    public static string GenerateMessageId()
    {
        DateTime now = DateTime.UtcNow;
        // Получаем таймстамп в миллисекундах 

        long timestamp = (long)(now - new DateTime(1970, 1, 1)).TotalMilliseconds;

        // Получаем миллисекунды текущего времени
        int milliseconds = now.Millisecond;

        // Форматируем идентификатор с учетом 5 знаков миллисекунд
        string messageId = $"{timestamp:D13}{milliseconds:D3}";

        return messageId;
    }

    public int GetQueueStatus() => _smsQueue.IsEmpty ? 0 : _smsQueue.Count;
    public int GetCallbackQueueStatus() => _smsCallbackQueue.IsEmpty ? 0 : _smsCallbackQueue.Count;

    public bool SwitchProvider(SmsProvider provider)
    {
        return _providerManager.SetActiveProvider(ServiceType.Sms, provider);
    }

    public SmsProvider GetActiveProvider()
    {
        return _providerManager.GetActiveProvider(ServiceType.Sms);
    }
}