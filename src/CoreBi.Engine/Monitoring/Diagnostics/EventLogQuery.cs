using System.Diagnostics.Eventing.Reader;

namespace CoreBi.Engine.Monitoring.Diagnostics;

internal static class EventLogReaderHelper
{
    public static void ForEach(string logName, string xpath, Action<EventRecord> action, bool reverseDirection = false)
    {
        using var reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = reverseDirection });
        for (EventRecord? record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
        {
            using (record) action(record);
        }
    }

    public static int Count(string logName, string xpath)
    {
        int count = 0;
        ForEach(logName, xpath, _ => count++);
        return count;
    }
}
