using System;
using Azure;
using Azure.Data.Tables;

namespace retailMvcDemo.Models
{
    public class ProductEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;   // e.g., "CATEGORY-GROCERY"
        public string RowKey { get; set; } = default!;         // SKU or GUID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public double Price { get; set; }
        public int StockQty { get; set; }
        public string? ImageBlobUrl { get; set; }              // URL to blob image
    }
}
