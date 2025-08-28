using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;

namespace ABCRetailWebApp.Controllers
{
    public class OrdersController(ILogger<OrdersController> logger, IConfiguration configuration) : Controller
    {
        private readonly string? _connectionString = configuration.GetConnectionString("AzureStorageConnectionString");
        private readonly ILogger<OrdersController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly string _orderQueueName = "orderqueue";

        private async Task<(List<CustomerProfile> Customers, List<Products> Products)> GetLookupData()
        {
            if (_connectionString == null) throw new InvalidOperationException("Connection string is null.");
            var customerTableClient = new TableClient(_connectionString, "CustomerProfiles");
            var productTableClient = new TableClient(_connectionString, "Products");
            await customerTableClient.CreateIfNotExistsAsync();
            await productTableClient.CreateIfNotExistsAsync();
            var customers = new List<CustomerProfile>();
            var products = new List<Products>();
            await foreach (var profile in customerTableClient.QueryAsync<CustomerProfile>()) { customers.Add(profile); }
            await foreach (var product in productTableClient.QueryAsync<Products>()) { products.Add(product); }
            return (customers, products);
        }

        public async Task<IActionResult> Index()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Orders");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Orders page accessed at {Time}", DateTime.Now);
            var queryResults = tableClient.QueryAsync<Orders>();
            var orders = new List<Orders>();
            await foreach (var order in queryResults) { orders.Add(order); }
            return View(orders);
        }

        public async Task<IActionResult> ManageOrders()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Orders");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Manage Orders page accessed at {Time}", DateTime.Now);
            var queryResults = tableClient.QueryAsync<Orders>();
            var orders = new List<Orders>();
            await foreach (var order in queryResults) { orders.Add(order); }
            return View(orders);
        }

        [HttpGet]
        [Route("Orders/Create")]
        public async Task<IActionResult> Create()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var (customers, products) = await GetLookupData();
            ViewBag.Customers = customers;
            ViewBag.Products = products;
            var order = new Orders
            {
                PartitionKey = "Orders",
                RowKey = Guid.NewGuid().ToString(),
                OrderDate = DateTime.UtcNow,
                ETag = new ETag(),
                Status = "Pending"
            };
            return View(order);
        }

        [HttpPost]
        [Route("Orders/CreateOrder")]
        public async Task<IActionResult> CreateOrder(Orders order)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            if (!ModelState.IsValid)
            {
                var (customers, products) = await GetLookupData();
                ViewBag.Customers = customers;
                ViewBag.Products = products;
                _logger.LogWarning("Invalid model state for order creation at {Time}: {ModelState}", DateTime.Now, ModelState);
                return View("Create", order);
            }

            if (order.RowKey == null)
            {
                order.RowKey = Guid.NewGuid().ToString();
            }
            if (order.PartitionKey == null)
            {
                order.PartitionKey = "Orders";
            }
            if (order.Price == 0)
            {
                try
                {
                    var productTableClient = new TableClient(_connectionString, "Products");
                    await productTableClient.CreateIfNotExistsAsync();
                    var product = await productTableClient.GetEntityAsync<Products>(order.PartitionKey, order.ProductId);
                    if (product.Value != null)
                    {
                        order.Price = (double)product.Value.Price;
                    }
                    else
                    {
                        ModelState.AddModelError("ProductId", "Invalid product selected.");
                        var (customers, products) = await GetLookupData();
                        ViewBag.Customers = customers;
                        ViewBag.Products = products;
                        return View("Create", order);
                    }
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Error fetching product price at {Time} for ProductId: {ProductId}", DateTime.Now, order.ProductId);
                    ModelState.AddModelError("ProductId", "Error retrieving product price.");
                    var (customers, products) = await GetLookupData();
                    ViewBag.Customers = customers;
                    ViewBag.Products = products;
                    return View("Create", order);
                }
            }

            order.OrderDate = DateTime.UtcNow;
            order.ETag = new ETag();
            order.TrackingId += $"TRK-{order.RowKey[..8]}"; // Simplified Substring and compound assignment
            order.Status = "Pending";

            try
            {
                var tableClient = new TableClient(_connectionString, "Orders");
                await tableClient.CreateIfNotExistsAsync();
                var queueClient = new QueueClient(_connectionString, _orderQueueName);
                await queueClient.CreateIfNotExistsAsync();

                await queueClient.SendMessageAsync($"Order created: {order.RowKey} at {DateTime.UtcNow}");
                _logger.LogInformation("Order creation logged in queue for {OrderId} at {Time}", order.RowKey, DateTime.Now);

                await tableClient.UpsertEntityAsync(order);
                _logger.LogInformation("New order created at {Time} with OrderId: {RowKey}", DateTime.Now, order.RowKey);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error saving order at {Time} with OrderId: {RowKey}", DateTime.Now, order.RowKey);
                return StatusCode(500, "An error occurred while saving the order.");
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteOrder(string id)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Orders");
            Orders? order = null;
            await foreach (var item in tableClient.QueryAsync<Orders>(o => o.RowKey == id))
            {
                order = item;
                break;
            }
            if (order != null)
            {
                try
                {
                    await tableClient.DeleteEntityAsync(order.PartitionKey, order.RowKey);
                    _logger.LogInformation("Order deleted at {Time} with OrderId: {RowKey}", DateTime.Now, id);
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Error deleting order at {Time} with OrderId: {RowKey}", DateTime.Now, id);
                    return StatusCode(500, "An error occurred while deleting the order.");
                }
            }
            return RedirectToAction("ManageOrders");
        }

        [HttpGet]
        public async Task<IActionResult> EditOrder(string id)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "Orders");
            await tableClient.CreateIfNotExistsAsync();
            var order = await tableClient.GetEntityAsync<Orders>("Orders", id);
            if (order.Value == null) return NotFound();
            return View(order.Value);
        }

        [HttpPost]
        public async Task<IActionResult> EditOrder(Orders order)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            if (!this.ModelState.IsValid) // Explicitly use 'this' to access ModelState
            {
                return View(order);
            }

            try
            {
                var tableClient = new TableClient(_connectionString, "Orders");
                await tableClient.UpsertEntityAsync(order, TableUpdateMode.Replace); // Correct update mode
                _logger.LogInformation("Order updated at {Time} with OrderId: {RowKey}", DateTime.Now, order.RowKey);
                return RedirectToAction("ManageOrders");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error updating order at {Time} with OrderId: {RowKey}", DateTime.Now, order.RowKey);
                this.ModelState.AddModelError("", "An error occurred while updating the order."); // Use 'this' for clarity
                return View(order);
            }
        }
    }
}