using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Entities.Entity;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Microsoft.Extensions.Options;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Providers;
using Common.HttpClientWrapper;

namespace Kp.Ms.Sms.Services;

public class CallService
{
    private readonly ILogger<CallService> _logger;
    private readonly ConcurrentQueue<CallRequest> _callQueue = [];
    private readonly ConcurrentQueue<CallbackItem> _callbackQueue = [];
    private readonly QueueSettings _queueSettings;
    private readonly IHttpClientWrapper _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private readonly IConfiguration _configuration;
    private readonly ProviderFactory _providerFactory;
    private Entities.Enums.SmsProvider _activeProvider;
    private readonly ValidationService _validationService;

    private Dictionary<string, Organization> _organizations =>
        _configuration.GetSection("Settings:Organizations")?.Get<Dictionary<string, Organization>>();

    public CallService(
        ILogger<CallService> logger,
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

    public async Task<CallResponse> InitiateCallAsync(CallRequest request)
    {
        try
        {
            if (!_organizations.TryGetValue(request.OrganizationName, out var organization))
            {
                throw new ArgumentException($"Organization with name \"{request.OrganizationName}\" not exist.");
            }

            var provider = GetProviderForOrganization(request.OrganizationName);
            string cleanedPhoneNumber;

            if (!_validationService.ValidPhoneNumber(request.Phone, out cleanedPhoneNumber))
            {
                return new CallResponse
                {
                    Status = StatusType.Failure.ToString(),
                    StatusText = ErrorMessages.InvalidPhoneNumber
                };
            }

            if (!string.IsNullOrEmpty(request.UserIp) && !_validationService.ValidIp(request.UserIp))
            {
                return new CallResponse
                {
                    Status = StatusType.Failure.ToString(),
                    StatusText = ErrorMessages.InvalidIp
                };
            }

            if (!string.IsNullOrEmpty(request.CallbackUrl) && !_validationService.ValidUrl(request.CallbackUrl))
            {
                return new CallResponse
                {
                    Status = StatusType.Failure.ToString(),
                    StatusText = ErrorMessages.InvalidCallbackUrl
                };
            }

            var response = await organization.CallApiAsync(_providerFactory, request.Phone, request.UserIp);

            // Звонок совершен успешно
            if (response.Status == SmsResponseStatus.OK.ToString())
            {
                response.Status = StatusType.Success.ToString();

                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    callId = response.CallId,
                    status = response.Status,
                    code = response.Code,
                    errorMessage = response.StatusText
                });

                await LogCallToOpenSearch(CreateCallLogRequest(request, response, response.Status));
                return response;
            }

            if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
            {
                // Если очередь переплнена, возвращаем ошибку
                if (!EnqueueCall(request))
                {
                    var queueErrorResponse = new CallResponse
                    {
                        Status = StatusType.Failure.ToString(),
                        StatusText = ErrorMessages.QueueLimitReached
                    };

                    await LogCallToOpenSearch(CreateCallLogRequest(request, response, queueErrorResponse.Status, queueErrorResponse.StatusText));
                    return queueErrorResponse;
                }

                // Есть место в очереди - добавляем в очередеь
                var queuedResponse = new CallResponse
                {
                    Status = StatusType.Queued.ToString(),
                    StatusText = ErrorMessages.NoRouteToHost
                };

                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    callId = response.CallId,
                    status = queuedResponse.Status,
                    code = response.Code,
                    errorMessage = response.StatusText
                });

                await LogCallToOpenSearch(CreateCallLogRequest(request, response, queuedResponse.Status, queuedResponse.StatusText));
                return queuedResponse;
            }

            // Непредвиденные ошибки
            await LogCallToOpenSearch(CreateCallLogRequest(request, response));
            return response;
        }
        catch (Exception ex)
        {
            return new CallResponse
            {
                Status = StatusType.Failure.ToString(),
                StatusText = ex.Message
            };
        }
    }

    private CallLogRequest CreateCallLogRequest(CallRequest request, CallResponse response, string statusOverride = null, string statusTextOverride = null)
    {
        return new CallLogRequest(
            request.Phone,
            statusOverride ?? response.Status,
            response.CallId,
            response.Code,
            statusTextOverride ?? response.StatusText
        );
    }

    public async Task ProcessQueue()
    {
        var batch = new List<CallRequest>();
        for (int i = 0; i < _queueSettings.CallBatchSize && _callQueue.TryDequeue(out var request); i++)
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
            var response = await organization.CallApiAsync(_providerFactory, request.Phone, request.UserIp);

            var logRequest = new CallLogRequest(
               phone: request.Phone,
               status: string.Empty,
               callId: response.CallId,
               code: response.Code,
               errorMessage: string.Empty
           );

            if (response.Status == SmsResponseStatus.OK.ToString())
            {
                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    callId = response.CallId,
                    status = response.Status,
                    code = response.Code,
                    errorMessage = "Successful sending of call from queue"
                });

                logRequest.Status = StatusType.Success.ToString();
                logRequest.ErrorMessage = "Successful sending of call from queue";
            }
            else
            {
                if (response.StatusCode == (int)SmsRuErrorCode.ServiceUnavailable || response.StatusCode == (int)SmsRuErrorCode.InternalServerError)
                {
                    if (!EnqueueCall(request))
                    {
                        logRequest.Status = StatusType.Failure.ToString();
                        logRequest.ErrorMessage = ErrorMessages.QueueLimitReached;
                    }
                }
                else
                {
                    logRequest.Status = StatusType.Failure.ToString();
                    logRequest.ErrorMessage = response.StatusText;
                }
            }

            await LogCallToOpenSearch(logRequest);
            await Task.Delay(_queueSettings.CallBatchIntervalMs);
        }
    }

    private bool EnqueueCall(CallRequest request)
    {
        if (_callQueue.Count >= _queueSettings.CallMaxSize)
        {
            return false;
        }
        _callQueue.Enqueue(request);
        return true;
    }

    public async Task ProcessCallbackQueue()
    {
        var batch = new List<CallbackItem>();

        for (int i = 0; i < _queueSettings.CallCallbackBatchSize && _callbackQueue.TryDequeue(out var callbackItem); i++)
        {
            batch.Add(callbackItem);
        }

        if (batch.Count == 0)
        {
            return;
        }

        var tasks = batch.Select(async item =>
        {
            if (item.Attempt >= _queueSettings.MaxCallAttempts)
            {

                await LogCallToOpenSearch(new CallLogRequest(null, StatusType.Failure.ToString(), null, null, $"Callback to url: {item.CallbackUrl} failed after attempts: {item.Attempt}. Removing from queue."));
                return;
            }

            CallbackResponse callbackResponse = await SendCallback(item.CallbackUrl, item.CallbackData, item.Attempt);

            if (!callbackResponse.Status)
            {

                if (_callbackQueue.Count >= _queueSettings.CallCallbackMaxSize)
                {
                    await LogCallToOpenSearch(new CallLogRequest(null, StatusType.Failure.ToString(), null, null, $"The callback queue is full! Callback queue size: {_callbackQueue.Count}, callback url: {item.CallbackUrl}"));
                    return;
                }
            }
            else
            {
                await LogCallToOpenSearch(new CallLogRequest(null, StatusType.Success.ToString(), null, null, $"Sending callback from queue to {item.CallbackUrl} was successful."));
            }
            await Task.Delay(_queueSettings.CallCallbackBatchIntervalMs);
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnqueueCallback(string callbackUrl, object callbackData)
    {
        if (string.IsNullOrEmpty(callbackUrl))
            return;

        if (_callbackQueue.Count >= _queueSettings.CallCallbackMaxSize)
        {
            await LogCallToOpenSearch(new CallLogRequest(null, StatusType.Failure.ToString(), null, null, $"The callback queue is full! Callback queue size: {_callbackQueue.Count}, callback url: {callbackUrl}"));
            return;
        }
        _callbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, 1));
    }

    private async Task<CallbackResponse> SendCallback(string callbackUrl, object callbackData, int attempt)
    {
        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");

        try
        {
            await _httpClient.PostAsync<object, object>(callbackUrl, jsonContent);
            return new CallbackResponse
            {
                Status = true,
                StatusText = null,
            };
        }
        catch (Exception ex)
        {
            if (_callbackQueue.Count >= _queueSettings.CallCallbackMaxSize)
            {
                return new CallbackResponse
                {
                    Status = false,
                    StatusText =  ErrorMessages.CallbackQueueLimitReached,
                };
            }
            _callbackQueue.Enqueue(new CallbackItem(callbackUrl, callbackData, attempt + 1));
            return new CallbackResponse
            {
                Status = false,
                StatusText = $"Callback failed and added to queue: {ex.Message}",
            };
        }
    }

    private async Task LogCallToOpenSearch(CallLogRequest logRequest)
    {
        CallLog log = new CallLog
        {
            Phone = logRequest.Phone,
            Status = logRequest.Status,
            CallId = logRequest.CallId,
            Code = logRequest.Code,
            ErrorMessage = logRequest.ErrorMessage,
            Date = DateTime.UtcNow,
            Provider = _activeProvider.ToString(),
        };

        var response = await _openSearchClient.IndexAsync(log, idx => idx.Index(_configuration.GetCallStorageName()));

        if (!response.IsValid)
        {
            Console.WriteLine("Error logging call to OpenSearch: " + response.ServerError.Error.Reason);
        }
    }

    private Provider GetProviderForOrganization(string organizationName)
    {
        var isExistOrganization = _organizations.TryGetValue(organizationName, out var organization);
        if (!isExistOrganization)
        {
            throw new ArgumentException($"Organization with name \"{organizationName}\" not exist.");
        }

        return _providerFactory.GetProvider(organization.GetDefaultProviderSettings());
    }

    public int GetCallQueueStatus() => _callQueue.IsEmpty ? 0 : _callQueue.Count;
    public int GetCallbackQueueStatus() => _callbackQueue.IsEmpty ? 0 : _callbackQueue.Count;
}