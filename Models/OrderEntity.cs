using System;
using Azure;
using Azure.Data.Tables;

namespace retailMvcDemo.Models
{
    public class OrderEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;   // "CUSTOMER-{CustomerId}"
        public string RowKey { get; set; } = default!;         // OrderId (GUID)
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string CustomerId { get; set; } = default!;
        public string ItemsJson { get; set; } = "[]";         // e.g., [{"sku":"MILK","qty":2,"price":24.99}]
        public double Total { get; set; }
        public string Status { get; set; } = "Pending";        // Pending/Processing/Completed/Cancelled
    }
}
