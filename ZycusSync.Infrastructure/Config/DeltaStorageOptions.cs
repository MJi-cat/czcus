using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ZycusSync.Infrastructure.Config
{
    public sealed class DeltaStorageOptions
    {
        public string? ConnectionString { get; set; }
        public string Container { get; set; } = "zycus-delta";
        public string Prefix { get; set; } = "dev/";
    }
}

