using Asp.Versioning;
using Kp.Ms.Sms.Attributes;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Reflection;
using System;

namespace Kp.Ms.Sms.Controllers.V1;
[ApiController]
[TokenAuthorization]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[SwaggerTag("Контроллер для звонков")]
public class CallController : ControllerBase
{
    private readonly CallService _callService;
    private readonly string _version;

    public CallController(CallService callService)
    {
        _callService = callService;
        _version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    [HttpPost("send")]
    public async Task<IActionResult> InitiateCall([FromBody] CallRequest request)
    {
        var response = await _callService.InitiateCallAsync(request);

        response.Version = _version;

        switch (response.Status)
        {
            case nameof(StatusType.Success):
                return Ok(response);

            case nameof(StatusType.Failure):
                if (response.StatusText.Contains("Invalid phone number or IP address") ||
                    response.StatusText.Contains("Invalid callback URL"))
                {
                    return BadRequest(response);
                }
                return StatusCode(500, response);

            case nameof(StatusType.Queued):
                return Accepted(response);

            default:
                return StatusCode(500, new CallResponse
                {
                    Version = _version,
                    Status = StatusType.Failure.ToString(),
                    StatusText = response.StatusText,
                });
        }
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new QueueStatusResponse
        {
            Version = _version,
            Queued = _callService.GetCallQueueStatus()
        });
    }

    [HttpGet("queue-callback-status")]
    public IActionResult GetCallbackQueueStatus()
    {
        return Ok(new QueueStatusResponse
        {
            Version = _version,
            Queued = _callService.GetCallbackQueueStatus()
        });
    }
}