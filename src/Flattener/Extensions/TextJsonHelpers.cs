using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallRecordInsights.Extensions
{
    public static class TextJsonHelpers
    {
        /// <summary>
        /// Gets the value of a JsonNode as a TimeSpan if it is a string in ISO 8601 duration or TimeSpan standard format.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static TimeSpan? GetValueAsTimeSpan(this JsonNode node) => 
            node is JsonValue 
            && (TryParseISO8601TimeSpan(node.GetValue<string>(), out var tsv) 
                || TimeSpan.TryParse(node.GetValue<string>(), out tsv)) 
            ? tsv 
            : null;

        /// <summary>
        /// Gets the value of a JsonNode as a byte array if it is a base64 encoded string.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static byte[]? GetValueAsByteArray(this JsonNode node) => 
            node is JsonValue 
            && node.GetValue<JsonElement>().TryGetBytesFromBase64(out var value) == true 
            ? value 
            : default;

        /// <summary>
        /// Parses a string in ISO 8601 duration format to a TimeSpan.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static bool TryParseISO8601TimeSpan(string? input, out TimeSpan time)
        {
            time = default;

            // Validate input
            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('P') || input.Length == 1)
                return false;

            // Initializing variables
            int days = 0, hours = 0, minutes = 0;
            double seconds = 0;

            // To avoid parsing the same part multiple times and to ensure the order of the parts
            bool hasDays = false, hasTimePart = false, hasHours = false, hasMinutes = false, hasSeconds = false;

            int index = 1; // Start after 'P'
            int timeIndex = -1; // Start of time part
            while (index < input.Length)
            {
                char unit = input[index];
                switch (unit)
                {
                    case 'D':
                        if (hasDays || hasTimePart || !int.TryParse(input.AsSpan(1, index - 1), out days))
                            return false;
                        hasDays = true;
                        break;
                    case 'T':
                        if (hasTimePart)
                            return false;
                        hasTimePart = true;
                        timeIndex = index;
                        break;
                    case 'H':
                        if (!hasTimePart || hasHours || hasMinutes || hasSeconds || !int.TryParse(input.AsSpan(timeIndex + 1, index - timeIndex - 1), out hours) || hours > 24)
                            return false;
                        hasHours = true;
                        timeIndex = index;
                        break;
                    case 'M':
                        if (!hasTimePart || hasMinutes || hasSeconds || !int.TryParse(input.AsSpan(timeIndex + 1, index - timeIndex - 1), out minutes) || minutes > 60)
                            return false;
                        hasMinutes = true;
                        timeIndex = index;
                        break;
                    case 'S':
                        if (!hasTimePart || hasSeconds || !double.TryParse(input.AsSpan(timeIndex + 1, index - timeIndex - 1), out seconds) || seconds > 60d)
                            return false;
                        hasSeconds = true;
                        timeIndex = index;
                        break;
                    default:
                        if (!char.IsDigit(unit) && (!hasTimePart || hasSeconds || unit != '.'))
                            return false;
                        break;
                }

                index++;
            }

            // Assemble TimeSpan
            time = new TimeSpan(days, hours, minutes, (int)seconds, (int)((seconds - (int)seconds) * 1000));
            return true;
        }
    }
}
