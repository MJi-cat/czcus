using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;

namespace ZycusSync.Application.Groups
{
    public sealed record ProcessGroupDelta : IRequest<Unit>;
}
