using System.Text;

namespace kpu.Models
{
    public class CsvOptions
    {
        public string Delimiter { get; set; } = ",";
        public bool IncludeHeader { get; set; } = true;
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public bool IgnoreNullValues { get; set; } = true;
        public int InitialBufferSize { get; set; } = 4096;
        public bool Append { get; set; } = false;
        public bool CapitalizeExceptRoman { get; set; } = false;
    }
}