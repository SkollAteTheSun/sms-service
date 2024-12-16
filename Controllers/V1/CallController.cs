using Asp.Versioning;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Kp.Ms.Sms.Controllers.V1;
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[SwaggerTag("Контроллер для звонков")]
public class CallController : ControllerBase
{
    private readonly CallService _callService;

    public CallController(CallService callService)
    {
        _callService = callService;
    }

    [HttpPost("call")]
    public async Task<IActionResult> InitiateCall([FromBody] CallRequest request)
    {
        var response = await _callService.InitiateCallAsync(request);

        if (string.IsNullOrEmpty(response.StatusText))
        {
            response.StatusText = "Unexpected error occurred";
        }

        switch (response.Status)
        {
            case "OK":
                return Ok(response);

            case "failure":
                if (response.StatusText.Contains("Invalid phone number or IP address") ||
                    response.StatusText.Contains("Invalid callback URL"))
                {
                    return BadRequest(response);
                }
                return StatusCode(500, response);

            case "queued":
                return Accepted(response);

            default:
                return StatusCode(500, new CallResponse
                {
                    Status = "failure",
                    StatusText = response.StatusText,
                });
        }
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new { queued = _callService.GetCallQueueStatus() });
    }

    [HttpGet("queue-callback-status")]
    public IActionResult GetCallbackQueueStatus()
    {
        return Ok(new { queued = _callService.GetCallbackQueueStatus() });
    }
}