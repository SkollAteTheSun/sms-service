using Asp.Versioning;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Attributes;
using System.Reflection;

namespace Kp.Ms.Sms.Controllers.V1;

[ApiController]
[TokenAuthorization]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[SwaggerTag("Тестовый контроллер")]
public class SmsController : ControllerBase
{
    private readonly SmsService _smsService;
    private readonly string _version;

    public SmsController(SmsService smsService)
    {
        _smsService = smsService;
        _version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SmsRequest request)
    {
        var result = (await _smsService.SendSmsAsync(request)) ?? new StatusResponse();

        result.Version = _version;

        switch (result?.Status)
        {
            case StatusType.Success: return Ok(result);

            case StatusType.Queued: return Accepted(result);

            default: return StatusCode(500, result);
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string phone)
    {
        try
        {
            var result = await _smsService.GetLastSmsStatusesAsync(phone);

            result.Version = _version;

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new SmsStatusResponse
            {
                Version = _version,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new SmsStatusResponse
            {
                Version = _version,
                Error = ex.Message
            });
        }
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new QueueStatusResponse
        {
            Version = _version,
            Queued = _smsService.GetQueueStatus()
        });
    }

    [HttpGet("queue-callback-status")]
    public IActionResult GetCallbackQueueStatus()
    {
        return Ok(new QueueStatusResponse
        {
            Version = _version,
            Queued = _smsService.GetCallbackQueueStatus()
        });
    }
}