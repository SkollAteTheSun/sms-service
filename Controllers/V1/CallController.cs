using Asp.Versioning;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Http;
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

        if (response.Status == "OK")
        {
            return Ok(response);
        }
        return StatusCode(500, response);
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new { queued = _callService.GetQueueStatus() });
    }
}