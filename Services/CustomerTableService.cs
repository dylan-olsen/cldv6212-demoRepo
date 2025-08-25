using Microsoft.Extensions.Configuration;
using retailMvcDemo.Models;
using System;

namespace retailMvcDemo.Services
{
    public class CustomerTableService : TableRepositoryBase<CustomerEntity>
    {
        public CustomerTableService(IConfiguration cfg)
            : base(cfg.GetConnectionString("AzureStorage"), "Customers") { }

        // tiny helper so controllers don’t forget keys
        public CustomerEntity Build(string first, string last, string email, string phone, string city) =>
            new CustomerEntity
            {
                PartitionKey = $"CITY-{city.ToUpperInvariant()}",
                RowKey = Guid.NewGuid().ToString("N"),
                FirstName = first,
                LastName = last,
                Email = email,
                Phone = phone,
                City = city
            };
    }
}
