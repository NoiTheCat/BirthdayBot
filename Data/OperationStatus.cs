using System;
using System.Collections.Generic;
using System.Text;

namespace BirthdayBot.Data
{
    /// <summary>
    /// Holds information regarding the previous updating information done on a guild including success/error information.
    /// </summary>
    class OperationStatus
    {
        private readonly Dictionary<OperationType, string> _log = new Dictionary<OperationType, string>();

        public DateTimeOffset Timestamp { get; }

        public OperationStatus (params (OperationType, string)[] statuses)
        {
            Timestamp = DateTimeOffset.UtcNow;
            foreach (var status in statuses)
            {
                _log[status.Item1] = status.Item2;
            }
        }

        /// <summary>
        /// Prepares known information in a displayable format.
        /// </summary>
        public string GetDiagStrings()
        {
            var report = new StringBuilder();
            foreach (OperationType otype in Enum.GetValues(typeof(OperationType)))
            {
                var prefix = $"`{Enum.GetName(typeof(OperationType), otype)}`: ";

                string info = null;

                if (!_log.TryGetValue(otype, out info))
                {
                    report.AppendLine(prefix + "No data");
                    continue;
                }

                if (info == null)
                {
                    report.AppendLine(prefix + "Success");
                }
                else
                {
                    report.AppendLine(prefix + info);
                }
            }
            return report.ToString();
        }

        /// <summary>
        /// Specifies the type of operation logged. These enum values are publicly displayed in the specified order.
        /// </summary>
        public enum OperationType
        {
            UpdateBirthdayRoleMembership,
            SendBirthdayAnnouncementMessage
        }
    }
}
