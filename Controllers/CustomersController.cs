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
        public IActionResult Create() => View();

        // POST /Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string firstName, string lastName, string email, string phone, string city)
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(city))
            {
                ModelState.AddModelError("", "First name, last name, and city are required.");
                return View();
            }

            var entity = _customers.Build(firstName, lastName, email, phone, city);
            await _customers.AddAsync(entity);

            // queue: customer-created
            await _queue.EnqueueOrderAsync(new
            {
                type = "customer-created",
                customerId = entity.RowKey,
                cityPartition = entity.PartitionKey,  // e.g., CITY-DURBAN
                name = $"{entity.FirstName} {entity.LastName}",
                utc = DateTime.UtcNow
            });

            return RedirectToAction(nameof(Index), new { city });
        }

        // GET /Customers/Edit?pk=...&rk=...
        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Customers/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string pk, string rk, string firstName, string lastName, string email, string phone, string city)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();

            row.FirstName = firstName;
            row.LastName = lastName;
            row.Email = email;
            row.Phone = phone;

            // If city changed, we should move partitions (Table Storage requires re-insert)
            var newPk = $"CITY-{city.ToUpper()}";
            if (!string.Equals(row.PartitionKey, newPk, StringComparison.OrdinalIgnoreCase))
            {
                // delete old row and create a new one with same RowKey in new partition
                var preservedRk = row.RowKey;

                await _customers.DeleteAsync(row.PartitionKey, row.RowKey);
                row.PartitionKey = newPk;
                row.RowKey = preservedRk; // keep same ID
                await _customers.AddAsync(row);
            }
            else
            {
                await _customers.UpdateAsync(row);
            }

            return RedirectToAction(nameof(Index), new { city });
        }

        // GET /Customers/Delete?pk=...&rk=...
        public async Task<IActionResult> Delete(string pk, string rk)
        {
            var row = await _customers.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Customers/DeleteConfirmed
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string pk, string rk)
        {
            await _customers.DeleteAsync(pk, rk);

            // queue: customer-deleted
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
