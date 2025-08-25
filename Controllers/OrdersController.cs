using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using retailMvcDemo.Models;
using retailMvcDemo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace retailMvcDemo.Controllers
{
    public class OrdersController : Controller
    {
        private readonly OrderTableService _orders;
        private readonly CustomerTableService _customers;
        private readonly ProductTableService _products;
        private readonly IQueueService _queue;

        public OrdersController(
            OrderTableService orders,
            CustomerTableService customers,
            ProductTableService products,
            IQueueService queue)
        {
            _orders = orders;
            _customers = customers;
            _products = products;
            _queue = queue;
        }

        // GET /Orders?customerId=...
        public async Task<IActionResult> Index(string? customerId = null)
        {
            var list = new List<OrderEntity>();
            if (string.IsNullOrWhiteSpace(customerId))
            {
                await foreach (var o in _orders.QueryAllAsync()) list.Add(o);
            }
            else
            {
                var pk = $"CUSTOMER-{customerId}";
                await foreach (var o in _orders.QueryByPartitionAsync(pk)) list.Add(o);
            }
            return View(list.OrderByDescending(o => o.Timestamp));
        }

        // GET /Orders/Details?pk=...&rk=...
        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // ---------- Create (multi-product) ----------

        // Populate dropdowns for Create GET and failed POST
        private async Task PopulateDropdownsAsync()
        {
            var customers = new List<CustomerEntity>();
            await foreach (var c in _customers.QueryAllAsync()) customers.Add(c);

            ViewBag.Customers = customers
                .OrderBy(c => c.LastName)
                .Select(c => new SelectListItem($"{c.FirstName} {c.LastName}", c.RowKey))
                .ToList();

            var products = new List<ProductEntity>();
            await foreach (var p in _products.QueryAllAsync()) products.Add(p);

            ViewBag.Products = products
                .OrderBy(p => p.Name)
                .Select(p => new SelectListItem($"{p.Name} (R {p.Price:0.00})", p.RowKey))
                .ToList();

            ViewBag.Statuses = new List<SelectListItem>
            {
                new("Pending", "Pending"),
                new("Processing", "Processing"),
                new("Completed", "Completed"),
                new("Cancelled", "Cancelled")
            };
        }

        // GET /Orders/Create
        public async Task<IActionResult> Create()
        {
            await PopulateDropdownsAsync();
            var vm = new OrderCreateVM
            {
                Status = "Pending",
                Items = new List<OrderCreateItemVM> { new OrderCreateItemVM { Quantity = 1 } }
            };
            return View(vm);
        }

        // POST /Orders/Create (multi-product; preserves selections on error)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrderCreateVM vm)
        {
            if (vm.Items == null || vm.Items.Count == 0)
                ModelState.AddModelError("", "Add at least one product.");

            // Validate customer
            CustomerEntity? customer = null;
            await foreach (var c in _customers.QueryAllAsync())
                if (c.RowKey == vm.CustomerId) { customer = c; break; }

            if (customer == null)
                ModelState.AddModelError("", "Selected customer not found.");

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(vm); // preserves user input
            }

            // Materialize products and compute total from authoritative prices
            var allProducts = new List<ProductEntity>();
            await foreach (var p in _products.QueryAllAsync()) allProducts.Add(p);

            var lines = new List<object>();
            double total = 0;

            foreach (var item in vm.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId) && i.Quantity > 0))
            {
                var prod = allProducts.FirstOrDefault(p => p.RowKey == item.ProductId);
                if (prod == null)
                {
                    ModelState.AddModelError("", "One or more selected products no longer exist.");
                    await PopulateDropdownsAsync();
                    return View(vm);
                }

                lines.Add(new { sku = prod.RowKey, name = prod.Name, qty = item.Quantity, price = prod.Price });
                total += item.Quantity * prod.Price;
            }

            if (lines.Count == 0)
            {
                ModelState.AddModelError("", "Add at least one valid product with quantity > 0.");
                await PopulateDropdownsAsync();
                return View(vm);
            }

            var itemsJson = JsonSerializer.Serialize(lines);

            var order = new OrderEntity
            {
                PartitionKey = $"CUSTOMER-{vm.CustomerId}",
                RowKey = Guid.NewGuid().ToString("N"),
                CustomerId = vm.CustomerId!,
                ItemsJson = itemsJson,
                Total = total,
                Status = string.IsNullOrWhiteSpace(vm.Status) ? "Pending" : vm.Status
            };

            await _orders.AddAsync(order);

            // Queue: order-created
            await _queue.EnqueueOrderAsync(new
            {
                type = "order-created",
                orderId = order.RowKey,
                customerId = order.CustomerId,
                total = order.Total,
                utc = DateTime.UtcNow
            });

            // Queue: inventory-reserve per line (optional but useful)
            foreach (var item in vm.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId) && i.Quantity > 0))
            {
                await _queue.EnqueueOrderAsync(new
                {
                    type = "inventory-reserve",
                    productId = item.ProductId!,
                    qty = item.Quantity,
                    orderId = order.RowKey,
                    utc = DateTime.UtcNow
                });
            }

            return RedirectToAction(nameof(Index), new { customerId = vm.CustomerId });
        }

        // ---------- Edit / Delete (unchanged behavior) ----------

        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string pk, string rk, string status)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();

            row.Status = string.IsNullOrWhiteSpace(status) ? row.Status : status;
            await _orders.UpdateAsync(row);

            string? cid = row.PartitionKey.StartsWith("CUSTOMER-") ? row.PartitionKey["CUSTOMER-".Length..] : null;
            return RedirectToAction(nameof(Index), new { customerId = cid });
        }

        // GET /Orders/Delete?pk=...&rk=...
        public async Task<IActionResult> Delete(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            string? cid = null;
            if (row != null)
                cid = row.PartitionKey.StartsWith("CUSTOMER-") ? row.PartitionKey["CUSTOMER-".Length..] : null;

            await _orders.DeleteAsync(pk, rk);
            return RedirectToAction(nameof(Index), new { customerId = cid });
        }
    }

}