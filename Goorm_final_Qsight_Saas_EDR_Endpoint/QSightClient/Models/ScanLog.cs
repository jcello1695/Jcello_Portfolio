using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QSightClient.Models
{
    public class ScanLog
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ScanId { get; set; } = "";
        public string StaticResult { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public DateTime ScanTime { get; set; }
        public string Result { get; set; } = "";

        public string Sha256 { get; set; } = "";
    }
}