using System;
using Azure;
using Azure.Data.Tables;

namespace retailMvcDemo.Models

{
    public class CustomerEntity : ITableEntity
    {
        // Required by Azure Tables
        public string PartitionKey { get; set; } = default!;   // e.g., "CITY-DURBAN"
        public string RowKey { get; set; } = default!;         // CustomerId (GUID as string)
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Domain fields 
        public string FirstName { get; set; } = default!;
        public string LastName { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Phone { get; set; } = default!;
        public string? City { get; set; }
       // public string? ImageBlobUrl { get; set; }              // URL to blob image
    }
}
