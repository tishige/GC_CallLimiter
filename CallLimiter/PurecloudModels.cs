using Genesys.Client.Notifications;
using StackExchange.Redis;

namespace ConsoleAppDotNet6
{
    public record AnalyticsQueueAggregatesData(string interval, AnalyticsQueueAggregatesMetric[] metrics);
    public record AnalyticsQueueAggregatesGroup(string queueId, string mediaType);
    public record AnalyticsQueueAggregatesMetric(string metric, NotificationStats stats, string qualifier);
    public record AnalyticsQueueAggregates(AnalyticsQueueAggregatesData[] data, AnalyticsQueueAggregatesGroup group);

    public record NotificationStats(double Count, double Sum, double Min, double Max, double ratio, double numerator, double denominator, double target);

    public class PurecloudAppConfig
    {        

        public string[]? Queues { get; set; }

    }

}
