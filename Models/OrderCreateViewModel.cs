using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace retailMvcDemo.Models
{
    public class OrderCreateItemVM
    {
        [Required]
        public string? ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;
    }

    public class OrderCreateVM
    {
        [Required]
        public string? CustomerId { get; set; }

        public string Status { get; set; } = "Pending";

        [MinLength(1, ErrorMessage = "Add at least one product.")]
        public List<OrderCreateItemVM> Items { get; set; } = new() { new OrderCreateItemVM() };
    }
}
