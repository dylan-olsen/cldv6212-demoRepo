using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using retailMvcDemo.Models;
using retailMvcDemo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace retailMvcDemo.Controllers
{
    // Lightweight VM for the Orders index grid/cards
    public class OrderListVM
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public string CustomerId { get; set; } = default!;
        public string CustomerName { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public double Total { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public int ItemsCount { get; set; }
    }

    // These two VMs match the Create page you’re using
    public class OrderCreateItemVM
    {
        public string? ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class OrderCreateVM
    {
        public string? CustomerId { get; set; }
        public List<OrderCreateItemVM> Items { get; set; } = new();
        public string Status { get; set; } = "Pending";
    }

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
        // Returns a compact list with CustomerName + ItemsCount (no scary GUIDs on the UI)
        public async Task<IActionResult> Index(string? customerId = null)
        {
            // Load orders (optionally filtered)
            var orders = new List<OrderEntity>();
            if (string.IsNullOrWhiteSpace(customerId))
            {
                await foreach (var o in _orders.QueryAllAsync()) orders.Add(o);
            }
            else
            {
                var pk = $"CUSTOMER-{customerId}";
                await foreach (var o in _orders.QueryByPartitionAsync(pk)) orders.Add(o);
            }

            // Build set of customer ids we need to resolve to names
            var wanted = orders
                .Select(o => o.CustomerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Map RowKey -> "First Last"
            var nameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var c in _customers.QueryAllAsync())
            {
                if (wanted.Contains(c.RowKey))
                {
                    nameById[c.RowKey] = $"{c.FirstName} {c.LastName}".Trim();
                    if (nameById.Count == wanted.Count) break;
                }
            }

            // Shape into the list VM + count items per order
            var model = orders
                .OrderByDescending(o => o.Timestamp)
                .Select(o =>
                {
                    int count = 0;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(o.ItemsJson))
                        {
                            using var doc = JsonDocument.Parse(o.ItemsJson);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                                count = doc.RootElement.GetArrayLength();
                        }
                    }
                    catch { /* ignore malformed json */ }

                    return new OrderListVM
                    {
                        PartitionKey = o.PartitionKey,
                        RowKey = o.RowKey,
                        CustomerId = o.CustomerId ?? "",
                        CustomerName = (o.CustomerId != null && nameById.TryGetValue(o.CustomerId, out var nm)) ? nm : (o.CustomerId ?? ""),
                        Status = o.Status ?? "Pending",
                        Total = o.Total,
                        Timestamp = o.Timestamp,
                        ItemsCount = count
                    };
                })
                .ToList();

            return View(model);
        }

        // GET /Orders/Details?pk=...&rk=...
        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _orders.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // ───────────────────────────── Create (GET) ─────────────────────────────
        // Optional params let you preselect a product from the Products page
        public async Task<IActionResult> Create(string? preselectProductId = null, int preselectQty = 1)
        {
            await PopulateDropdownsAsync();

            var vm = new OrderCreateVM
            {
                Status = "Pending",
                Items = new List<OrderCreateItemVM>
                {
                    new OrderCreateItemVM { ProductId = preselectProductId, Quantity = Math.Max(1, preselectQty) }
                }
            };

            return View(vm);
        }

        // ───────────────────────────── Create (POST) ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrderCreateVM vm)
        {
            await PopulateDropdownsAsync();

            if (vm == null)
            {
                ModelState.AddModelError("", "Invalid order.");
                return View(vm);
            }

            if (string.IsNullOrWhiteSpace(vm.CustomerId))
                ModelState.AddModelError(nameof(vm.CustomerId), "Please select a customer.");

            // Normalize items: keep only rows with a product and qty > 0
            vm.Items = vm.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.ProductId) && i.Quantity > 0)
                .ToList();

            if (vm.Items.Count == 0)
                ModelState.AddModelError("", "Add at least one product with quantity.");

            if (!ModelState.IsValid)
                return View(vm);

            // Validate customer exists
            CustomerEntity? customer = null;
            await foreach (var c in _customers.QueryAllAsync())
            {
                if (c.RowKey == vm.CustomerId) { customer = c; break; }
            }
            if (customer == null)
            {
                ModelState.AddModelError(nameof(vm.CustomerId), "Selected customer not found.");
                return View(vm);
            }

            // Build authoritative line items from product table (ensure price from DB)
            var lines = new List<object>();
            double total = 0;

            foreach (var item in vm.Items)
            {
                ProductEntity? product = null;
                await foreach (var p in _products.QueryAllAsync())
                {
                    if (p.RowKey == item.ProductId) { product = p; break; }
                }
                if (product == null)
                {
                    ModelState.AddModelError("", "One or more products no longer exist.");
                    return View(vm);
                }

                lines.Add(new { sku = product.RowKey, name = product.Name, qty = item.Quantity, price = product.Price });
                total += product.Price * item.Quantity;
            }

            var order = new OrderEntity
            {
                PartitionKey = $"CUSTOMER-{vm.CustomerId}",
                RowKey = Guid.NewGuid().ToString("N"),
                CustomerId = vm.CustomerId,
                ItemsJson = JsonSerializer.Serialize(lines),
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

            // Queue: inventory reservations per item
            foreach (var item in vm.Items)
            {
                await _queue.EnqueueOrderAsync(new
                {
                    type = "inventory-reserve",
                    productId = item.ProductId,
                    qty = item.Quantity,
                    orderId = order.RowKey,
                    utc = DateTime.UtcNow
                });
            }

            return RedirectToAction(nameof(Index), new { customerId = vm.CustomerId });
        }

        // ───────────────────────────── Edit Status ──────────────────────────────
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

        // ───────────────────────────── Delete ───────────────────────────────────
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

        // ───────────────────────────── helpers ──────────────────────────────────
        private async Task PopulateDropdownsAsync()
        {
            // Customers dropdown (Id + "First Last")
            var customers = new List<CustomerEntity>();
            await foreach (var c in _customers.QueryAllAsync()) customers.Add(c);
            ViewBag.Customers = customers
                .OrderBy(c => c.LastName)
                .Select(c => new { Id = c.RowKey, Name = $"{c.FirstName} {c.LastName}" })
                .ToList();

            // Products dropdown (RowKey + display)
            var products = new List<ProductEntity>();
            await foreach (var p in _products.QueryAllAsync()) products.Add(p);
            ViewBag.Products = products
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    Rk = p.RowKey,
                    Name = p.Name,
                    Price = p.Price,
                    Display = $"{p.Name} (R {p.Price:0.00})"
                })
                .ToList();

            ViewBag.Statuses = new[] { "Pending", "Processing", "Completed", "Cancelled" };
        }
    }
}
