using Microsoft.AspNetCore.Mvc;
using retailMvcDemo.Models;
using retailMvcDemo.Services;

namespace retailMvcDemo.Controllers
{
    public class ContractsFilesController : Controller
    {
        private readonly IFileShareService _files;
        private readonly IQueueService? _queue; // optional

        public ContractsFilesController(IFileShareService files, IQueueService? queue = null)
        {
            _files = files;
            _queue = queue;
        }

        // GET /ContractsFiles
        public async Task<IActionResult> Index()
        {
            IReadOnlyList<ContractFileViewModel> list = await _files.ListAsync();
            return View(list);
        }

        // GET /ContractsFiles/Upload
        public IActionResult Upload() => View();

        // POST /ContractsFiles/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file, string? displayName)
        {
            if (file == null || file.Length <= 0)
            {
                ModelState.AddModelError("", "Please choose a file.");
                return View();
            }

            var name = await _files.UploadAsync(file, displayName);
            TempData["Msg"] = $"Uploaded: {name}";

            if (_queue != null)
            {
                await _queue.EnqueueOrderAsync(new
                {
                    type = "contract-uploaded",
                    fileName = name,
                    utc = DateTime.UtcNow
                });
            }

            return RedirectToAction(nameof(Index));
        }

        // GET /ContractsFiles/Download?name=...
        public async Task<IActionResult> Download(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest();
            var stream = await _files.DownloadAsync(name);
            const string contentType = "application/octet-stream";
            return File(stream, contentType, fileDownloadName: name);
        }

        // POST /ContractsFiles/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest();
            await _files.DeleteAsync(name);

            if (_queue != null)
            {
                await _queue.EnqueueOrderAsync(new
                {
                    type = "contract-deleted",
                    fileName = name,
                    utc = DateTime.UtcNow
                });
            }

            TempData["Msg"] = "Deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
