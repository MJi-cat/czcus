using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;

namespace ZycusSync.Application.Users;

public sealed record ProcessUserDelta : IRequest<Unit>;


