using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ABCRetailWebApp.Controllers
{
    public class ProductsController : Controller
    {
        private readonly string? _connectionString;
        private readonly string? _blobConnectionString;
        private readonly ILogger<ProductsController>? _logger;
        private const string ContainerName = "productimages";
        private readonly string _inventoryQueueName = "inventoryqueue";

        public ProductsController(IConfiguration configuration, ILogger<ProductsController> logger)
        {
            _connectionString = configuration.GetConnectionString("AzureStorageConnectionString");
            _blobConnectionString = configuration.GetConnectionString("AzureBlobStorageConnectionString") ?? throw new ArgumentNullException(nameof(_blobConnectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> Index()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Products");
            await tableClient.CreateIfNotExistsAsync();
            _logger?.LogInformation("Products page accessed at {Time}", DateTime.Now);
            var queryResults = tableClient.QueryAsync<Products>();
            var products = new List<Products>();
            await foreach (var product in queryResults) { products.Add(product); }
            return View(products);
        }

        public async Task<IActionResult> ManageProducts()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Products");
            await tableClient.CreateIfNotExistsAsync();
            _logger?.LogInformation("Manage Products page accessed at {Time}", DateTime.Now);
            var queryResults = tableClient.QueryAsync<Products>();
            var products = new List<Products>();
            await foreach (var product in queryResults) { products.Add(product); }
            return View(products);
        }

        [HttpGet]
        [Route("Products/Create")]
        public IActionResult Create()
        {
            return View(new Products());
        }

        [HttpPost]
        [Route("Products/CreateProduct")]
        public async Task<IActionResult> CreateProduct(Products product, IFormFile? imageFile)
        {
            if (_connectionString == null || _blobConnectionString == null) return new StatusCodeResult(500);
            if (!ModelState.IsValid)
            {
                return View("Create", product);
            }
            if (string.IsNullOrEmpty(product.ProductId))
            {
                ModelState.AddModelError("ProductId", "ProductId is required.");
                return View("Create", product);
            }

            var tableClient = new TableClient(_connectionString, "Products");
            await tableClient.CreateIfNotExistsAsync();
            product.PartitionKey = "Products";
            product.RowKey = product.ProductId;

            var queueClient = new QueueClient(_connectionString, _inventoryQueueName);
            await queueClient.CreateIfNotExistsAsync();

            // Send queue message for product creation logging
            await queueClient.SendMessageAsync($"Product created: {product.ProductId} at {DateTime.UtcNow}");
            _logger?.LogInformation("Product creation logged in queue for {ProductId} at {Time}", product.ProductId, DateTime.Now);

            if (imageFile != null && imageFile.Length > 0)
            {
                var blobContainerClient = new BlobContainerClient(_blobConnectionString, ContainerName);
                await blobContainerClient.CreateIfNotExistsAsync();
                var blobName = $"{product.ProductId}/{imageFile.FileName}";
                var blobClient = blobContainerClient.GetBlobClient(blobName);

                using (var stream = imageFile.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = imageFile.ContentType });
                }
                product.ImageUrl = blobClient.Uri.ToString();

                // Send queue message for image upload
                await queueClient.SendMessageAsync($"Uploading image: {blobName}");
                _logger?.LogInformation("Image upload queued for {BlobName} at {Time}", blobName, DateTime.Now);
            }

            // Send queue message for inventory update
            await queueClient.SendMessageAsync($"Inventory update: Added {product.ProductId} with quantity 1");
            _logger?.LogInformation("Inventory update queued for {ProductId} at {Time}", product.ProductId, DateTime.Now);

            // Send queue message for price validation
            await queueClient.SendMessageAsync($"Validate price: {product.ProductId} - {product.Price}");
            _logger?.LogInformation("Price validation queued for {ProductId} at {Time}", product.ProductId, DateTime.Now);

            await tableClient.UpsertEntityAsync(product);
            _logger?.LogInformation("New product created at {Time} with ProductId: {RowKey}", DateTime.Now, product.RowKey);
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> EditProduct(string id)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Products");
            Products? product = null;
            await foreach (var item in tableClient.QueryAsync<Products>(p => p.RowKey == id))
            {
                product = item;
                break; // Take the first match
            }
            if (product == null) return NotFound();
            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> EditProduct(Products product, IFormFile? imageFile)
        {
            if (_connectionString == null || _blobConnectionString == null) return new StatusCodeResult(500);
            if (!ModelState.IsValid)
            {
                return View(product);
            }
            var tableClient = new TableClient(_connectionString, "Products");
            try
            {
                var existingProduct = tableClient.GetEntity<Products>(product.PartitionKey, product.RowKey).Value;
                product.ETag = existingProduct.ETag;

                if (imageFile != null && imageFile.Length > 0)
                {
                    var blobContainerClient = new BlobContainerClient(_blobConnectionString, ContainerName);
                    await blobContainerClient.CreateIfNotExistsAsync();
                    var blobName = $"{product.ProductId}/{imageFile.FileName}";
                    var blobClient = blobContainerClient.GetBlobClient(blobName);

                    using (var stream = imageFile.OpenReadStream())
                    {
                        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = imageFile.ContentType });
                    }
                    product.ImageUrl = blobClient.Uri.ToString();

                    // Send queue message for image upload
                    var queueClient = new QueueClient(_connectionString, _inventoryQueueName);
                    await queueClient.CreateIfNotExistsAsync();
                    await queueClient.SendMessageAsync($"Uploading image: {blobName}");
                    _logger?.LogInformation("Image upload queued for {BlobName} at {Time}", blobName, DateTime.Now);
                }

                await tableClient.UpdateEntityAsync(product, product.ETag);
                _logger?.LogInformation("Product updated at {Time} with ProductId: {RowKey}", DateTime.Now, product.RowKey);
            }
            catch (Exception)
            {
                return NotFound();
            }
            return RedirectToAction("ManageProducts");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProduct(string id)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Products");
            Products? product = null;
            await foreach (var item in tableClient.QueryAsync<Products>(p => p.RowKey == id))
            {
                product = item;
                break; // Take the first match
            }
            if (product != null)
            {
                try
                {
                    await tableClient.DeleteEntityAsync(product.PartitionKey, product.RowKey);
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        var blobContainerClient = new BlobContainerClient(_blobConnectionString, ContainerName);
                        var blobClient = blobContainerClient.GetBlobClient(product.ImageUrl.Split('/').Last());
                        await blobClient.DeleteIfExistsAsync();
                    }
                    _logger?.LogInformation("Product deleted at {Time} with ProductId: {RowKey}", DateTime.Now, id);
                }
                catch (Exception)
                {
                    return NotFound();
                }
            }
            return RedirectToAction("ManageProducts");
        }
    }
}