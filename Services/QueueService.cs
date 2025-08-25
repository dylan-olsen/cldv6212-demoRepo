using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;

namespace retailMvcDemo.Services
{
    public interface IQueueService
    {
        Task EnqueueOrderAsync(object payload);
        Task<IReadOnlyList<string>> PeekAsync(int maxMessages = 16);
    }

    public class QueueService : IQueueService
    {
        private readonly QueueClient _queue;

        public QueueService(IConfiguration cfg)
        {
            // Prefer explicit AzureQueueStorage settings, otherwise fall back to ConnectionStrings:AzureStorage
            var conn = cfg.GetSection("AzureQueueStorage")["ConnectionString"]
                      ?? cfg.GetConnectionString("AzureStorage");
            var name = cfg.GetSection("AzureQueueStorage")["QueueName"] ?? "orders";

            _queue = new QueueClient(conn, name, new QueueClientOptions
            {
                // Base64-encode so JSON is safe to transport
                MessageEncoding = QueueMessageEncoding.Base64
            });

            _queue.CreateIfNotExists();
        }

        public async Task EnqueueOrderAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            await _queue.SendMessageAsync(json);
        }

        public async Task<IReadOnlyList<string>> PeekAsync(int maxMessages = 16)
        {
            var list = new List<string>();
            PeekedMessage[] peeked = await _queue.PeekMessagesAsync(maxMessages);
            foreach (var m in peeked)
            {
                if (m.Body != null)
                    list.Add(m.Body.ToString());
            }
            return list;
        }
    }
}
