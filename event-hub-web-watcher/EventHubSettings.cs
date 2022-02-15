namespace event_hub_web_watcher
{
    public class EventHubSettings
    {
        public string ConnectionString { get; set; }
        public string Name { get; set; }
        public string ConsumerGroup { get; set; }
    }
}