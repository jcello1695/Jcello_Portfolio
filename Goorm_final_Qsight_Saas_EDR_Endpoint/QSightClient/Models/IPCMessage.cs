using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QSightClient.Models
{
    public class IPCMessage
    {
        public required string Command { get; set; }
        public required string Path { get; set; }
    }
}
