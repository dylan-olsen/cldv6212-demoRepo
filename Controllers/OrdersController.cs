using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using retailMvcDemo.Models;
using retailMvcDemo.Services;
using System.Text.Json;

namespace retailMvcDemo.Controllers
{
    public class OrdersController : Controller
    {
        private readonly OrderTableService _orders;
        private readonly CustomerTableService _customers;
        private readonly ProductTableService _products;

        public OrdersController(OrderTableService orders, CustomerTableService customers, ProductTableService products)
        {
            _orders = orders;
            _customers = customers;
            _products = products;
        }

        // GET /Orders?customerId=<RowKey>
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

        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // CREATE (GET): dropdowns for customer + product
        public async Task<IActionResult> Create()
        {
            var customers = new List<CustomerEntity>();
            await foreach (var c in _customers.QueryAllAsync()) customers.Add(c);

            var products = new List<ProductEntity>();
            await foreach (var p in _products.QueryAllAsync()) products.Add(p);

            ViewBag.Customers = customers
                .OrderBy(c => c.LastName)
                .Select(c => new { Id = c.RowKey, Name = $"{c.FirstName} {c.LastName}" })
                .ToList();

            // Display: "Rk - Name (R price)"
            ViewBag.Products = products
                .OrderBy(p => p.Name)
                .Select(p => new { Rk = p.RowKey, Name = p.Name, Price = p.Price, Display = $"{p.RowKey} - {p.Name} (R {p.Price:0.00})" })
                .ToList();

            // Simple statuses list for the dropdown
            ViewBag.Statuses = new[] { "Pending", "Processing", "Completed", "Cancelled" };

            return View();
        }

        // CREATE (POST): compute price + total on server (secure)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string customerId, string productRowKey, int quantity, string status)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                ModelState.AddModelError("", "Please select a customer.");
            if (string.IsNullOrWhiteSpace(productRowKey))
                ModelState.AddModelError("", "Please select a product.");
            if (quantity <= 0)
                ModelState.AddModelError("", "Quantity must be at least 1.");

            if (!ModelState.IsValid) return await Create();

            // Look up product by RowKey (we don't know the category/PartitionKey, so query by RowKey)
            ProductEntity? product = null;
            await foreach (var p in _products.QueryAsync(x => x.RowKey == productRowKey))
            {
                product = p; break;
            }
            if (product == null)
            {
                ModelState.AddModelError("", "Selected product not found.");
                return await Create();
            }

            // compute totals using authoritative price
            var line = new { sku = product.RowKey, name = product.Name, qty = quantity, price = product.Price };
            var itemsJson = JsonSerializer.Serialize(new[] { line });
            var total = quantity * product.Price;

            var order = new OrderEntity
            {
                PartitionKey = $"CUSTOMER-{customerId}",
                RowKey = Guid.NewGuid().ToString("N"),
                CustomerId = customerId,
                ItemsJson = itemsJson,
                Total = total,
                Status = string.IsNullOrWhiteSpace(status) ? "Pending" : status
            };

            await _orders.AddAsync(order);
            return RedirectToAction(nameof(Index), new { customerId });
        }

        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string pk, string rk, string itemsJson, double total, string status)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();

            try { JsonDocument.Parse(string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson); }
            catch { ModelState.AddModelError("", "Items JSON is invalid."); return View(row); }

            row.ItemsJson = string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson;
            row.Total = total;
            row.Status = string.IsNullOrWhiteSpace(status) ? row.Status : status;

            await _orders.UpdateAsync(row);

            var cid = row.PartitionKey.StartsWith("CUSTOMER-") ? row.PartitionKey["CUSTOMER-".Length..] : null;
            return RedirectToAction(nameof(Index), new { customerId = cid });
        }

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
