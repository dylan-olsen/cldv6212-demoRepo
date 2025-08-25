using Microsoft.AspNetCore.Mvc;
using retailMvcDemo.Models;
using retailMvcDemo.Services;

namespace retailMvcDemo.Controllers
{
    public class CustomersController : Controller
    {
        private readonly CustomerTableService _svc;

        public CustomersController(CustomerTableService svc)
        {
            _svc = svc;
        }

        // GET /Customers?city=Durban
        public async Task<IActionResult> Index(string? city = null)
        {
            var items = new List<CustomerEntity>();

            if (string.IsNullOrWhiteSpace(city))
            {
                await foreach (var c in _svc.QueryAllAsync())
                    items.Add(c);
            }
            else
            {
                var pk = $"CITY-{city.ToUpper()}";
                await foreach (var c in _svc.QueryByPartitionAsync(pk))
                    items.Add(c);
            }

            // Show newest first
            return View(items.OrderByDescending(x => x.Timestamp));
        }

        // GET /Customers/Create
        public IActionResult Create() => View();

        // POST /Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string firstName, string lastName, string email, string phone, string city)
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(city))
            {
                ModelState.AddModelError("", "Please fill in First Name, Last Name, Email and City.");
                return View();
            }

            var entity = _svc.Build(firstName, lastName, email, phone, city);
            await _svc.AddAsync(entity);

            return RedirectToAction(nameof(Index), new { city });
        }

        // GET /Customers/Edit?pk=...&rk=...
        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _svc.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Customers/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string pk, string rk, string firstName, string lastName, string email, string phone, string? city)
        {
            var row = await _svc.GetAsync(pk, rk);
            if (row == null) return NotFound();

            row.FirstName = firstName;
            row.LastName = lastName;
            row.Email = email;
            row.Phone = phone;
            row.City = city;

            await _svc.UpdateAsync(row);
            return RedirectToAction(nameof(Index), new { city });
        }

        // GET /Customers/Delete?pk=...&rk=...
        public async Task<IActionResult> Delete(string pk, string rk)
        {
            var row = await _svc.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Customers/DeleteConfirmed
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string pk, string rk, string? city)
        {
            await _svc.DeleteAsync(pk, rk);
            return RedirectToAction(nameof(Index), new { city });
        }

        // Optional: quick details view
        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _svc.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }
    }
}
