using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.EventHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace event_hub_web_watcher
{
    /// <summary>
    ///     API endpoint to manage orders
    /// </summary>
    [ApiController]
    [Route("api/v1/metrics")]
    public class MetricsController : ControllerBase
    {
        static int i = 0;
        private readonly EventHubClient _eventhubClient;
        private readonly BlobContainerClient _checkpointContainerClient;
        protected IConfiguration Configuration { get; }

        protected readonly ILogger<MetricsController> Logger;

        private readonly string _connectionString;
        private readonly string _eventhubName;
        private readonly string _consumerGroup;
        private readonly string _fullyQualifiedNamespace;
        private readonly string _checkpointConnectionString;
        private readonly string _checkpointContainerName;

        private string Prefix() => $"{_fullyQualifiedNamespace.ToLowerInvariant()}/{_eventhubName.ToLowerInvariant()}/{_consumerGroup.ToLowerInvariant()}";
        private string OwnershipPrefix() => $"{Prefix()}/ownership/";
        private string CheckpointPrefix() => $"{Prefix()}/checkpoint/";
        private string CheckpointBlobName(string partitionId) => $"{CheckpointPrefix()}{partitionId}";

        private const string OwnerIdentifierMetadataKey = "ownerid";
        private const string SequenceNumberMetadataKey = "sequenceNumber";
        private const string OffesetMetadataKey = "offset";
        private const string ServiceBusHostName = ".servicebus.windows.net";



        public MetricsController(IConfiguration configuration, ILogger<MetricsController> logger)
        {
            Configuration = configuration;
            Logger = logger;
            _connectionString = Configuration.GetValue<string>("EventHub:ConnectionString");
            _eventhubName = Configuration.GetValue<string>("EventHub:Name");
            _consumerGroup = Configuration.GetValue<string>("EventHub:ConsumerGroup");

            int index = _connectionString.IndexOf(ServiceBusHostName);
            int start = "Endpoint=sb://".Length;
            _fullyQualifiedNamespace = _connectionString.Substring(start, index - start + ServiceBusHostName.Length);

            string fullConnectionString = _connectionString + ";EntityPath=" + _eventhubName;
            _eventhubClient = EventHubClient.CreateFromConnectionString(fullConnectionString);

            _checkpointConnectionString = Configuration.GetValue<string>("CheckPoint:ConnectionString");
            _checkpointContainerName = Configuration.GetValue<string>("CheckPoint:Container");
            _checkpointContainerClient = new BlobContainerClient(_checkpointConnectionString, _checkpointContainerName);
        }

        [HttpGet]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<EventHubStatus> Get()
        {
            EventHubStatus retVal = new EventHubStatus()
            {
                MessageCount = 0,
                ConsumerGroup = _consumerGroup,
                HostName = _fullyQualifiedNamespace,
                EventHubName = _eventhubName
            };
            
            var checkpointBlobsPrefix = CheckpointPrefix();

            var ehInfo = await _eventhubClient.GetRuntimeInformationAsync();
            foreach (string partitionId in ehInfo.PartitionIds)
            {               
                retVal.MessageCount += await UnprocessedMessageInPartition(partitionId);
            }

            //var ss = _checkpointContainerClient.GetBlobs();

            //foreach (BlobItem item in ss)
            /*await foreach (BlobItem item in _checkpointContainerClient.GetBlobsAsync(
                traits: BlobTraits.Metadata, prefix: checkpointBlobsPrefix, cancellationToken: defaultToken).ConfigureAwait(false))
            {           
                string strSeqNum, strOffset;
                if ((item.Metadata.TryGetValue(SequenceNumberMetadataKey, out strSeqNum)) && (item.Metadata.TryGetValue(OffesetMetadataKey, out strOffset)))
                {
                    long seqNum, offset;
                    if ((long.TryParse(strSeqNum, out seqNum)) && (long.TryParse(strOffset, out offset)))
                    {
                        Logger.LogInformation("Got metadata " + item.Name + " seq=" + seqNum, " offset=" + offset);
                    }                        
                }
            }*/

            return retVal;
        }

        private async Task<long> UnprocessedMessageInPartition(string partitionId)
        {
            long retVal = 0;
            try
            {
                var partitionInfo = await _eventhubClient.GetPartitionRuntimeInformationAsync(partitionId);
                CancellationToken defaultToken = default;

                // if partitionInfo.LastEnqueuedOffset = -1, that means event hub partition is empty
                if ((partitionInfo != null) && (partitionInfo.LastEnqueuedOffset == "-1"))
                {
                    return retVal;
                }

                string checkpointName = CheckpointBlobName(partitionId);

                try
                {
                    BlobProperties properties = await _checkpointContainerClient
                       .GetBlobClient(checkpointName)
                       .GetPropertiesAsync(conditions: null, cancellationToken: defaultToken)
                       .ConfigureAwait(false);

                    string strSeqNum, strOffset;
                    if ((properties.Metadata.TryGetValue(SequenceNumberMetadataKey, out strSeqNum)) && (properties.Metadata.TryGetValue(OffesetMetadataKey, out strOffset)))
                    {
                        long seqNum;
                        if (long.TryParse(strSeqNum, out seqNum))
                        {
                            Logger.LogInformation("Got metadata " + checkpointName + " seq=" + seqNum + " offset=" + strOffset);

                            // If checkpoint.Offset is empty that means no messages has been processed from an event hub partition
                            // And since partitionInfo.LastSequenceNumber = 0 for the very first message hence
                            // total unprocessed message will be partitionInfo.LastSequenceNumber + 1
                            if (string.IsNullOrEmpty(strOffset) == true)
                            {
                                retVal = partitionInfo.LastEnqueuedSequenceNumber + 1;
                                return retVal;
                            }

                            if (partitionInfo.LastEnqueuedSequenceNumber >= seqNum)
                            {
                                retVal = partitionInfo.LastEnqueuedSequenceNumber - seqNum;
                                return retVal;
                            }

                            // Partition is a circular buffer, so it is possible that
                            // partitionInfo.LastSequenceNumber < blob checkpoint's SequenceNumber
                            retVal = (long.MaxValue - partitionInfo.LastEnqueuedSequenceNumber) + seqNum;

                            // Checkpointing may or may not be always behind partition's LastSequenceNumber.
                            // The partition information read could be stale compared to checkpoint,
                            // especially when load is very small and checkpointing is happening often.
                            // e.g., (9223372036854775807 - 10) + 11 = -9223372036854775808
                            // If unprocessedEventCountInPartition is negative that means there are 0 unprocessed messages in the partition
                            if (retVal < 0)
                                retVal = 0;
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogError("Checkpoint container not exist " + ex.Message);
                    retVal = partitionInfo.LastEnqueuedSequenceNumber + 1;
                }
            }
            catch(Exception ex)
            {
                Logger.LogError(ex.ToString());
            }
            return retVal;
        }        
    }
}