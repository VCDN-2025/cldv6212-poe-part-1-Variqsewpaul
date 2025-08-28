using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using ABCRetailWebApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ABCRetailWebApp.Controllers
{
    public class HomeController(ILogger<HomeController> logger, IConfiguration configuration) : Controller
    {
        private readonly string? _connectionString = configuration.GetConnectionString("AzureStorageConnectionString");
        private readonly ILogger<HomeController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly string _customerQueueName = "customerqueue";

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> CreateCustomerProfile()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Create customer profile page accessed at {Time}", DateTime.Now);
            var customerProfiles = new List<CustomerProfile>();
            await foreach (var profile in tableClient.QueryAsync<CustomerProfile>()) { customerProfiles.Add(profile); }
            var newProfile = new CustomerProfile();
            ViewData["NewProfile"] = newProfile;
            return View(customerProfiles);
        }

        public async Task<IActionResult> ManageCustomerProfiles()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Manage customer profiles page accessed at {Time}", DateTime.Now);
            var customerProfiles = new List<CustomerProfile>();
            await foreach (var profile in tableClient.QueryAsync<CustomerProfile>()) { customerProfiles.Add(profile); }
            return View(customerProfiles);
        }

        [HttpGet]
        public async Task<IActionResult> EditProfile(string partitionKey, string rowKey)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Edit profile page accessed at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
            var response = await tableClient.GetEntityAsync<CustomerProfile>(partitionKey, rowKey);
            if (response.Value == null)
            {
                return NotFound();
            }
            return View(response.Value);
        }

        [HttpPost]
        public async Task<IActionResult> EditProfile(string partitionKey, string rowKey, CustomerProfile profile)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            if (!ModelState.IsValid)
            {
                return View(profile);
            }
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            profile.PartitionKey = partitionKey;
            profile.RowKey = rowKey;
            profile.RegistrationDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
            await tableClient.UpsertEntityAsync(profile, TableUpdateMode.Replace);
            _logger.LogInformation("Profile updated at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
            return RedirectToAction("ManageCustomerProfiles");
        }

        [HttpGet]
        public async Task<IActionResult> DeleteProfile(string partitionKey, string rowKey)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Delete profile page accessed at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
            try
            {
                var response = await tableClient.GetEntityAsync<CustomerProfile>(partitionKey, rowKey);
                if (response.Value == null)
                {
                    _logger.LogWarning("Profile not found for deletion at {Time} with RowKey: {RowKey}", DateTime.Now, rowKey);
                    return NotFound();
                }
                return View(response.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving profile for deletion at {Time} with RowKey: {RowKey}", DateTime.Now, rowKey);
                return StatusCode(500, "An error occurred while retrieving the profile.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmDeleteProfile(string partitionKey, string rowKey)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            _logger.LogInformation("Profile deletion initiated at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
            try
            {
                await tableClient.DeleteEntityAsync(partitionKey, rowKey);
                _logger.LogInformation("Profile deleted successfully at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting profile at {Time} for RowKey: {RowKey}", DateTime.Now, rowKey);
                return StatusCode(500, "An error occurred while deleting the profile.");
            }
            return RedirectToAction("ManageCustomerProfiles");
        }

        public async Task<IActionResult> SeedData()
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            var sampleProfiles = new[]
            {
                new CustomerProfile
                {
                    PartitionKey = "Customer",
                    RowKey = "001",
                    CustomerId = "C001",
                    Name = "John Doe",
                    Email = "john.doe@example.com",
                    Address = "123 Main St",
                    Phone = "555-0101",
                    RegistrationDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc)
                },
                new CustomerProfile
                {
                    PartitionKey = "Customer",
                    RowKey = "002",
                    CustomerId = "C002",
                    Name = "Jane Smith",
                    Email = "jane.smith@example.com",
                    Address = "456 Oak Ave",
                    Phone = "555-0102",
                    RegistrationDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc)
                }
            };
            foreach (var profile in sampleProfiles)
            {
                await tableClient.UpsertEntityAsync(profile);
            }
            _logger.LogInformation("Sample customer data seeded at {Time}", DateTime.Now);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> CreateProfile(CustomerProfile profile)
        {
            if (_connectionString == null) return new StatusCodeResult(500);
            var tableClient = new TableClient(_connectionString, "CustomerProfiles");
            await tableClient.CreateIfNotExistsAsync();
            profile.PartitionKey = "Customer";
            profile.RowKey = Guid.NewGuid().ToString();
            profile.RegistrationDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
            await tableClient.UpsertEntityAsync(profile);

            // Send queue messages
            var queueClient = new QueueClient(_connectionString, _customerQueueName);
            await queueClient.CreateIfNotExistsAsync();

            // Log customer creation
            await queueClient.SendMessageAsync($"Customer created: {profile.CustomerId} - {profile.Name} at {DateTime.UtcNow}");
            _logger.LogInformation("Customer creation logged in queue for {CustomerId} at {Time}", profile.CustomerId, DateTime.Now);

            // Send welcome notification
            await queueClient.SendMessageAsync($"Send welcome: {profile.CustomerId} - {profile.Email}");
            _logger.LogInformation("Welcome notification queued for {CustomerId} at {Time}", profile.CustomerId, DateTime.Now);

            // Validate customer details
            await queueClient.SendMessageAsync($"Validate customer: {profile.CustomerId} - {profile.Email}");
            _logger.LogInformation("Customer validation queued for {CustomerId} at {Time}", profile.CustomerId, DateTime.Now);

            _logger.LogInformation("New profile created at {Time}: {Name}", DateTime.Now, profile.Name);
            return RedirectToAction("Index");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}