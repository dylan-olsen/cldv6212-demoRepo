using Microsoft.AspNetCore.Mvc;
using retailMvcDemo.Models;
using retailMvcDemo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace retailMvcDemo.Controllers
{
    public class CustomersController : Controller
    {
        private readonly CustomerTableService _customers;
        private readonly IQueueService _queue;

        public CustomersController(CustomerTableService customers, IQueueService queue)
        {
            _customers = customers;
            _queue = queue;
        }

        // GET /Customers?city=Durban
        public async Task<IActionResult> Index(string? city = null)
        {
            var list = new List<CustomerEntity>();
            if (string.IsNullOrWhiteSpace(city))
            {
                await foreach (var c in _customers.QueryAllAsync()) list.Add(c);
            }
            else
            {
                var pk = $"CITY-{city.ToUpper()}";
                await foreach (var c in _customers.QueryByPartitionAsync(pk)) list.Add(c);
            }
            return View(list.OrderByDescending(c => c.Timestamp));
        }

        // GET /Customers/Details?pk=...&rk=...
        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // GET /Customers/Create
        public IActionResult Create() => View(new CustomerEntity());

        // POST /Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomerEntity model)
        {
            // PartitionKey/RowKey are non-nullable, so MVC treats them as required.
            // We generate them server-side, so remove them from ModelState.
            ModelState.Remove(nameof(CustomerEntity.PartitionKey));
            ModelState.Remove(nameof(CustomerEntity.RowKey));

            if (string.IsNullOrWhiteSpace(model.City))
                ModelState.AddModelError(nameof(CustomerEntity.City), "City is required.");

            // Uniqueness: email (global)
            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                await foreach (var c in _customers.QueryAllAsync())
                {
                    if (string.Equals(c.Email, model.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(CustomerEntity.Email), "That email is already in use.");
                        break;
                    }
                }
            }

            // Uniqueness: phone (global) – optional field, only check when provided
            if (!string.IsNullOrWhiteSpace(model.Phone))
            {
                await foreach (var c in _customers.QueryAllAsync())
                {
                    if (string.Equals(c.Phone, model.Phone, StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError(nameof(CustomerEntity.Phone), "That phone number is already in use.");
                        break;
                    }
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            // Keys
            model.PartitionKey = $"CITY-{model.City!.Trim().ToUpper()}";
            model.RowKey = Guid.NewGuid().ToString("N");

            await _customers.AddAsync(model);

            await _queue.EnqueueOrderAsync(new
            {
                type = "customer-created",
                customerId = model.RowKey,
                cityPartition = model.PartitionKey,
                name = $"{model.FirstName} {model.LastName}",
                utc = DateTime.UtcNow
            });

            return RedirectToAction(nameof(Index), new { city = model.City });
        }

        // GET /Customers/Edit?pk=...&rk=...
        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();
            if (string.IsNullOrWhiteSpace(row.City) && pk.StartsWith("CITY-"))
                row.City = pk.Substring("CITY-".Length);
            return View(row);
        }

        // POST /Customers/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CustomerEntity model)
        {
            // we expect PK/RK to come back via hidden fields
            if (string.IsNullOrWhiteSpace(model.City))
                ModelState.AddModelError(nameof(CustomerEntity.City), "City is required.");

            // Uniqueness checks (exclude the current customer by RowKey)
            await foreach (var c in _customers.QueryAllAsync())
            {
                if (c.RowKey == model.RowKey) continue;

                if (!string.IsNullOrWhiteSpace(model.Email) &&
                    string.Equals(c.Email, model.Email, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(CustomerEntity.Email), "That email is already in use.");
                    break;
                }
                if (!string.IsNullOrWhiteSpace(model.Phone) &&
                    string.Equals(c.Phone, model.Phone, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(CustomerEntity.Phone), "That phone number is already in use.");
                    break;
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            var existing = await _customers.GetAsync(model.PartitionKey, model.RowKey);
            if (existing == null) return NotFound();

            existing.FirstName = model.FirstName;
            existing.LastName = model.LastName;
            existing.Email = model.Email;
            existing.Phone = model.Phone;
            existing.City = model.City;

            // Handle city change (partition move)
            var newPk = $"CITY-{model.City!.Trim().ToUpper()}";
            if (!string.Equals(existing.PartitionKey, newPk, StringComparison.OrdinalIgnoreCase))
            {
                var rk = existing.RowKey;
                await _customers.DeleteAsync(existing.PartitionKey, existing.RowKey);
                existing.PartitionKey = newPk;
                existing.RowKey = rk;
                await _customers.AddAsync(existing);
            }
            else
            {
                await _customers.UpdateAsync(existing);
            }

            return RedirectToAction(nameof(Index), new { city = model.City });
        }

        // GET /Customers/Delete?pk=...&rk=...
        public async Task<IActionResult> Delete(string pk, string rk)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Customers/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string pk, string rk)
        {
            await _customers.DeleteAsync(pk, rk);

            await _queue.EnqueueOrderAsync(new
            {
                type = "customer-deleted",
                customerId = rk,
                partitionKey = pk,
                utc = DateTime.UtcNow
            });

            return RedirectToAction(nameof(Index));
        }
    }
}
