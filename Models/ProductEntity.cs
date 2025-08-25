using System;
using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Data.Tables;

namespace retailMvcDemo.Models
{
    public class ProductEntity : ITableEntity
    {
        // Azure Tables keys
        public string PartitionKey { get; set; } = default!;   // e.g., CATEGORY-GROCERY
        public string RowKey { get; set; } = default!;         // SKU or GUID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Domain fields
        [Required, StringLength(80)]
        public string Name { get; set; } = default!;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        [Range(0.01, 100000, ErrorMessage = "Price must be greater than 0.")]
        public double Price { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative.")]
        public int StockQty { get; set; }

        public string? ImageBlobUrl { get; set; }
    }
}
