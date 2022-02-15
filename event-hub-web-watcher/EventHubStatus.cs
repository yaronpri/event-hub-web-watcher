using Newtonsoft.Json;

namespace event_hub_web_watcher
{
    public class EventHubStatus
    {
        [JsonProperty]
        public long MessageCount { get; set; }
    }
}
