using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;          // IFormFile
using retailMvcDemo.Models;
using retailMvcDemo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace retailMvcDemo.Controllers
{
    public class ProductsController : Controller
    {
        private readonly ProductTableService _products;
        private readonly IBlobService _blobs;
        private readonly IQueueService _queue;

        public ProductsController(ProductTableService products, IBlobService blob, IQueueService queue)
        {
            _products = products;
            _blobs = blob;
            _queue = queue;
        }

        // GET /Products?category=Grocery
        public async Task<IActionResult> Index(string? category = null)
        {
            var list = new List<ProductEntity>();
            if (string.IsNullOrWhiteSpace(category))
            {
                await foreach (var p in _products.QueryAllAsync()) list.Add(p);
            }
            else
            {
                var pk = $"CATEGORY-{category.ToUpper()}";
                await foreach (var p in _products.QueryByPartitionAsync(pk)) list.Add(p);
            }
            return View(list.OrderByDescending(p => p.Timestamp));
        }

        // GET /Products/Details?pk=...&rk=...
        public async Task<IActionResult> Details(string pk, string rk)
        {
            var row = await _products.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // GET /Products/Create
        public IActionResult Create() => View();

        // POST /Products/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string category, string name, double price, int stockQty, IFormFile? imageFile)
        {
            // basic requireds
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
            {
                // keep form filled + show error
                ViewBag.Category = category;
                ViewBag.Name = name;
                ViewBag.Price = price;
                ViewBag.StockQty = stockQty;

                // IMPORTANT: key must match the input's name attr (lowercase "name")
                ModelState.AddModelError("name", "Category and Name are required.");
                return View();
            }

            // prevent duplicate product name in same category
            var pk = $"CATEGORY-{category.ToUpper()}";
            await foreach (var existing in _products.QueryByPartitionAsync(pk))
            {
                if (existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    // keep form filled + show field-level error
                    ViewBag.Category = category;
                    ViewBag.Name = name;
                    ViewBag.Price = price;
                    ViewBag.StockQty = stockQty;

                    // IMPORTANT: use "name" not "Name" to bind to the free-form input
                    ModelState.AddModelError("name", "A product with this name already exists in this category.");
                    return View();
                }
            }

            string? imageUrl = null;
            if (imageFile != null && imageFile.Length > 0)
            {
                imageUrl = await _blobs.UploadFileAsync(imageFile);
            }

            var entity = _products.Build(category, name, price, stockQty, imageUrl);
            await _products.AddAsync(entity);

            // ───────────── Queue messages ─────────────
            await _queue.EnqueueOrderAsync(new
            {
                type = "product-created",
                productId = entity.RowKey,
                category = entity.PartitionKey,
                name = entity.Name,
                price = entity.Price,
                hasImage = !string.IsNullOrWhiteSpace(entity.ImageBlobUrl),
                utc = DateTime.UtcNow
            });

            if (!string.IsNullOrWhiteSpace(entity.ImageBlobUrl))
            {
                await _queue.EnqueueOrderAsync(new
                {
                    type = "product-image-uploaded",
                    productId = entity.RowKey,
                    url = entity.ImageBlobUrl,
                    utc = DateTime.UtcNow
                });
            }
            // ─────────────────────────────────────────

            return RedirectToAction(nameof(Index), new { category });
        }

        // GET /Products/Edit?pk=...&rk=...
        public async Task<IActionResult> Edit(string pk, string rk)
        {
            var row = await _products.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Products/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            string pk, string rk, string name, double price, int stockQty, IFormFile? newImageFile, string? existingImageUrl)
        {
            var row = await _products.GetAsync(pk, rk);
            if (row == null) return NotFound();

            // prevent renaming to clash with another product in same category
            var pkCheck = row.PartitionKey;
            await foreach (var existing in _products.QueryByPartitionAsync(pkCheck))
            {
                if (existing.RowKey != rk &&
                    existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    // IMPORTANT: key must match the input name in your Edit view
                    ModelState.AddModelError("name", "Another product with this name already exists in this category.");
                    // keep current row so view stays filled
                    return View(row);
                }
            }

            // Update basic fields
            row.Name = name;
            row.Price = price;
            row.StockQty = stockQty;

            // Replace image if a new one was uploaded
            if (newImageFile != null && newImageFile.Length > 0)
            {
                if (!string.IsNullOrWhiteSpace(existingImageUrl))
                    await _blobs.DeleteFileAsync(existingImageUrl);

                var newUrl = await _blobs.UploadFileAsync(newImageFile);
                row.ImageBlobUrl = newUrl;

                await _queue.EnqueueOrderAsync(new
                {
                    type = "product-image-replaced",
                    productId = row.RowKey,
                    newUrl = newUrl,
                    utc = DateTime.UtcNow
                });
            }

            await _products.UpdateAsync(row);

            var categoryHint = row.PartitionKey.StartsWith("CATEGORY-")
                ? row.PartitionKey.Substring("CATEGORY-".Length)
                : null;

            return RedirectToAction(nameof(Index), new { category = categoryHint });
        }

        // GET /Products/Delete?pk=...&rk=...
        public async Task<IActionResult> Delete(string pk, string rk)
        {
            var row = await _products.GetAsync(pk, rk);
            if (row == null) return NotFound();
            return View(row);
        }

        // POST /Products/DeleteConfirmed
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string pk, string rk)
        {
            var row = await _products.GetAsync(pk, rk);

            if (row != null && !string.IsNullOrWhiteSpace(row.ImageBlobUrl))
            {
                await _blobs.DeleteFileAsync(row.ImageBlobUrl);
            }

            await _products.DeleteAsync(pk, rk);

            await _queue.EnqueueOrderAsync(new
            {
                type = "product-deleted",
                productId = rk,
                partitionKey = pk,
                utc = DateTime.UtcNow
            });

            return RedirectToAction(nameof(Index));
        }
    }
}
