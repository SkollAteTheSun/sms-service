using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Kp.Ms.Sms.Providers;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using System.Collections.Concurrent;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly ILogger<SmsService> _logger;
    private readonly ConcurrentQueue<SmsRequest> _smsQueue = [];
    private readonly ConcurrentQueue<CallbackItem> _smsCallbackQueue = [];
    private readonly QueueSettings _queueSettings;
    private readonly IHttpClientWrapper _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private IConfiguration _configuration;
    private readonly ProviderFactory _providerFactory;
    private readonly ValidationService _validationService;

    private Dictionary<string, Organization> _organizations =>
        _configuration.GetSection("Settings:Organizations")?.Get<Dictionary<string, Organization>>();

    public SmsService(
        ILogger<SmsService> logger,
        ProviderFactory providerFactory,
        ValidationService validationService,
        IConfiguration configuration,
        IHttpClientWrapper httpClient,
        OpenSearchClient openSearchClient,
        IOptions<QueueSettings> queueSettings)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _validationService = validationService;
        _providerFactory = providerFactory;
        _queueSettings = queueSettings.Value;
    }

    public async Task<string> SendSmsAsync(SmsRequest request)
    {
        try
        {
            if (!_organizations.TryGetValue(request.OrganizationName, out var organization))
            {
                throw new ArgumentException($"Organization with name \"{request.OrganizationName}\" not exist.");
            }

            request.MessId = GenerateMessageId();
            string cleanedPhoneNumber;

            if (!_validationService.ValidPhoneNumber(request.Phone, out cleanedPhoneNumber)) return ErrorMessages.InvalidPhoneNumber;

            if (!string.IsNullOrEmpty(request.CallbackUrl) && !_validationService.ValidUrl(request.CallbackUrl)) return ErrorMessages.InvalidCallbackUrl;

            var response = await organization.SendSmsAsync(_providerFactory, request.Phone, request.Message, request.Provider);

            // Отправка смс прошла успешна
            if (response.Status == SmsResponseStatus.OK.ToString())
            {
                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    message = request.Message,
                    messId = request.MessId,
                    status = StatusType.Success.ToString(),
                    errorMessage = response.StatusText
                });

                await LogSmsToOpenSearch(request, StatusType.Success.ToString(), response.ProviderName);
                return StatusType.Success.ToString();
            }

            if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
            {
                // Если очередь переплнена, возвращаем ошибку
                if (!EnqueueSms(request))
                {
                    await LogSmsToOpenSearch(request, StatusType.Failure.ToString(), response.ProviderName, ErrorMessages.QueueLimitReached);
                    return ErrorMessages.QueueLimitReached;
                }

                // Есть место в очереди - добавляем в очередеь
                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    message = request.Message,
                    messId = request.MessId,
                    status = StatusType.Queued.ToString(),
                    errorMessage = response.StatusText
                });

                await LogSmsToOpenSearch(request, StatusType.Queued.ToString(), response.ProviderName, ErrorMessages.NoRouteToHost);
                return StatusType.Queued.ToString();
            }

            // Непредвиденные ошибки
            await LogSmsToOpenSearch(request, response.Status, response.ProviderName, response.StatusText);
            return response.Status;
        }
        catch (Exception ex)
        {
            return StatusType.Failure.ToString();
        }
    }

    public async Task ProcessQueue()
    {
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
            if (!_organizations.TryGetValue(request.OrganizationName, out var organization))
            {
                _logger.LogError($"Organization with name \"{request.OrganizationName}\" not exist.");
                continue;
            }
            var response = await organization.SendSmsAsync(_providerFactory, request.Phone, request.Message, request.Provider);

            var status = string.Empty;
            var errorMessage = string.Empty;

            if (response.Status == SmsResponseStatus.OK.ToString())
            {
                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    message = request.Message,
                    messId = request.MessId,
                    status = response.Status,
                    errorMessage = response.StatusText
                });

                status = StatusType.Success.ToString();
                errorMessage = "Successful sending of call from queue";
            }
            else
            {
                if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
                {
                    if (!EnqueueSms(request))
                    {
                        status = StatusType.Failure.ToString();
                        errorMessage = ErrorMessages.QueueLimitReached;
                    }
                }
                else
                {
                    status = StatusType.Failure.ToString();
                    errorMessage = response.StatusText;
                }
            }

            await LogSmsToOpenSearch(request, status, response.ProviderName, errorMessage);

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
                await LogSmsToOpenSearch(
                    null,
                    StatusType.Failure.ToString(),
                    $"Callback to url: {item.CallbackUrl} failed after {item.Attempt} attempts. Removing from queue.");
                return;
            }

            CallbackResponse callbackResponse = await SendCallback(item.CallbackUrl, item.CallbackData, item.Attempt);

            if (!callbackResponse.Status)
            {
                if (_smsCallbackQueue.Count >= _queueSettings.SmsCallbackMaxSize)
                {
                    await LogSmsToOpenSearch(
                        null,
                        StatusType.Failure.ToString(),
                        $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {item.CallbackUrl}");
                    return;
                }
            }
            else
            {
                await LogSmsToOpenSearch(
                    null,
                    StatusType.Success.ToString(),
                    $"Sending callback from queue to {item.CallbackUrl} was successful.");
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
            await LogSmsToOpenSearch(
                null,
                StatusType.Failure.ToString(),
                $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {callbackUrl}");
            return;
        }
        _smsCallbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, 1));
    }

    private async Task<CallbackResponse> SendCallback(string callbackUrl, object callbackData, int attempt)
    {
        try
        {
            await _httpClient.PostAsync<object, object>(callbackUrl, callbackData);
            return new CallbackResponse
            {
                Status = true,
                StatusText = null,
            };
        }
        catch (Exception ex)
        {
            if (_smsCallbackQueue.Count >= _queueSettings.SmsCallbackMaxSize)
            {
                return new CallbackResponse
                {
                    Status = false,
                    StatusText = ErrorMessages.CallbackQueueLimitReached,
                };
            }
            _smsCallbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, 1));
            return new CallbackResponse
            {
                Status = false,
                StatusText = $"Callback failed and added to queue: {ex.Message}",
            };
        }
    }

    private async Task LogSmsToOpenSearch(SmsRequest? request, string status, string provider = null, string errorMessage = null)
    {
        SmsLog log = new SmsLog
        {
            MessId = request?.MessId,
            Phone = request?.Phone,
            TextMessage = request?.Message,
            Date = DateTime.UtcNow,
            Status = status,
            Provider = provider,
            ErrorMessage = errorMessage
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
}