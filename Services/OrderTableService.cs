using Microsoft.Extensions.Configuration;
using retailMvcDemo.Models;

namespace retailMvcDemo.Services
{
    public class OrderTableService : TableRepositoryBase<OrderEntity>
    {
        public OrderTableService(IConfiguration cfg)
            : base(cfg.GetConnectionString("AzureStorage"), "Orders") { }
    }
}
