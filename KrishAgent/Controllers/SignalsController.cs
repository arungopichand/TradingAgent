using Microsoft.AspNetCore.Mvc;
using KrishAgent.Services;

namespace KrishAgent.Controllers
{
    [ApiController]
    [Route("api/signals")]
    public sealed class SignalsController : ControllerBase
    {
        private readonly SignalEngine _signalEngine;

        public SignalsController(SignalEngine signalEngine)
        {
            _signalEngine = signalEngine;
        }

        [HttpGet]
        public async Task<IActionResult> GetSignals(CancellationToken cancellationToken)
        {
            var board = await _signalEngine.GetBoardAsync(cancellationToken);
            return Ok(board);
        }
    }
}
