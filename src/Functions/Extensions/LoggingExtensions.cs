using System.Web;

namespace CallRecordInsights.Extensions
{
    public static class LoggingExtensions
    {
        /// <summary>
        /// Sanitize the string to log by replacing tabs, new lines and carriage returns with underscores
        /// </summary>
        /// <param name="stringToLog"></param>
        /// <returns></returns>
        public static string Sanitize(this string stringToLog)
        {
            return HttpUtility.HtmlEncode(
                stringToLog?
                    .Replace('\t','_')
                    .Replace('\r','_')
                    .Replace('\n','_')
                ?? string.Empty);
        }
    }
}
