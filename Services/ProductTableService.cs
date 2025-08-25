using Microsoft.Extensions.Configuration;
using retailMvcDemo.Models;
using System;

namespace retailMvcDemo.Services
{
    public class ProductTableService : TableRepositoryBase<ProductEntity>
    {
        public ProductTableService(IConfiguration cfg)
            : base(cfg.GetConnectionString("AzureStorage"), "Products") { }

        // No SKU: RowKey is a GUID
        public ProductEntity Build(string category, string name, double price, int stockQty, string? imgUrl = null)
        {
            return new ProductEntity
            {
                PartitionKey = $"CATEGORY-{category.ToUpperInvariant()}",
                RowKey = Guid.NewGuid().ToString("N"),
                Name = name,
                Price = price,
                StockQty = stockQty,
                ImageBlobUrl = imgUrl
            };
        }
    }
}


