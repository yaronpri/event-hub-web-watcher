using Newtonsoft.Json;

namespace event_hub_web_watcher
{
    public class EventHubStatus
    {
        [JsonProperty]
        public long MessageCount { get; set; }

        [JsonProperty]
        public string ConsumerGroup { get; set; }

        [JsonProperty]
        public string EventHubName { get; set; }

        [JsonProperty]
        public string HostName { get; set; }
    }
}
