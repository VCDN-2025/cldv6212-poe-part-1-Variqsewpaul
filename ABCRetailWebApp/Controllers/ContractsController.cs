using Azure.Storage.Files.Shares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Azure;

namespace ABCRetailWebApp.Controllers
{
    public class ContractsController(ILogger<ContractsController> logger, IConfiguration configuration) : Controller
    {
        private readonly string? _connectionString = configuration.GetConnectionString("AzureStorageConnectionString");
        private readonly ILogger<ContractsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly string _fileShareName = "contracts"; // Adjust to your file share name

        [HttpGet]
        public IActionResult Upload()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            return View(new ContractUploadViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Upload(ContractUploadViewModel model)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var file = model.File;
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please select a file to upload.");
                return View(model);
            }

            try
            {
                var shareClient = new ShareClient(_connectionString, _fileShareName);
                await shareClient.CreateIfNotExistsAsync();

                var directoryClient = shareClient.GetDirectoryClient("dummycontracts"); // Subdirectory for dummy contracts
                await directoryClient.CreateIfNotExistsAsync();

                var fileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(file.FileName)}";
                var fileClient = directoryClient.GetFileClient(fileName);
                using var stream = file.OpenReadStream();
                await fileClient.CreateAsync(file.Length);
                await fileClient.UploadRangeAsync(new HttpRange(0, file.Length), stream);

                _logger.LogInformation("Contract uploaded at {Time}: {FileName}", DateTime.UtcNow, fileName);
                ViewBag.Message = $"Contract '{fileName}' uploaded successfully!";
                ModelState.Clear(); // Clear errors after success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading contract at {Time}", DateTime.UtcNow);
                ModelState.AddModelError("", "An error occurred while uploading the contract. Please try again.");
            }

            return View(new ContractUploadViewModel());
        }
    }
}