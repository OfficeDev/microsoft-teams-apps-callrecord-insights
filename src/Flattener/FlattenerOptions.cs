using System.Collections.Generic;

namespace CallRecordInsights.Flattener
{
    public class FlattenerOptions
    {
        public IDictionary<string, string>? ColumnObjectMap { get; set; }
        public IList<string>? ColumnOrder { get; set; }
        public bool CaseInsensitivePropertyNameMatching { get; set; } = false;
    }
}