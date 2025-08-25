using System;
using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Data.Tables;

namespace retailMvcDemo.Models
{
    public class CustomerEntity : ITableEntity
    {
        // Azure Tables keys
        public string PartitionKey { get; set; } = default!;   // e.g., CITY-DURBAN
        public string RowKey { get; set; } = default!;         // GUID string
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Domain fields
        [Required, StringLength(50)]
        public string FirstName { get; set; } = default!;

        [Required, StringLength(50)]
        public string LastName { get; set; } = default!

        ;

        [Required, EmailAddress, StringLength(120)]
        public string Email { get; set; } = default!;

        [Phone, StringLength(30)]
        public string? Phone { get; set; }

        // Optional – PartitionKey already encodes the city as CITY-*
        [StringLength(60)]
        public string? City { get; set; }
    }
}
