using Azure;
using Azure.Data.Tables;
using System;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class Products : ITableEntity
    {
        public string? PartitionKey { get; set; } // e.g., "Products" for all products
        public string? RowKey { get; set; } // Unique ProductId
        [Required(ErrorMessage = "ProductId is required.")]
        public string? ProductId { get; set; } // Explicit ProductId field
        [Required(ErrorMessage = "Name is required.")]
        public string? Name { get; set; }
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0.")]
        public double Price { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; } // URL to the blob-stored image
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}