using Azure;
using Azure.Data.Tables;
using System;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class Orders : ITableEntity
    {
        public string? PartitionKey { get; set; } // Link to CustomerId
        public string? RowKey { get; set; } // Unique OrderId
        [Required(ErrorMessage = "CustomerId is required.")]
        public string? CustomerId { get; set; } // Explicit link to customer
        [Required(ErrorMessage = "ProductId is required.")]
        public string? ProductId { get; set; } // Links to Products.RowKey
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0.")]
        public double Price { get; set; }
        public string? TrackingId { get; set; }
        public DateTime OrderDate { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}