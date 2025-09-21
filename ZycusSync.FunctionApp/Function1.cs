using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ZycusSync.Application.Users;
using ZycusSync.Application.Groups;

namespace ZycusSync.FunctionApp
{
    public sealed class Function1
    {
        private readonly ILogger<Function1> _log;
        private readonly IMediator _mediator;

        public Function1(ILogger<Function1> log, IMediator mediator)
            => (_log, _mediator) = (log, mediator);

        // Every 5 minutes
        [Function("ProcessUserDelta")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
        {
            _log.LogInformation("Tick at: {time}", DateTimeOffset.UtcNow);

            // Run both flows
            await _mediator.Send(new ProcessUserDelta());
            await _mediator.Send(new ProcessGroupDelta());
        }
    }
}
