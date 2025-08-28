using Azure;
using Azure.Data.Tables;
using System;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class CustomerProfile : ITableEntity
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public string? CustomerId { get; set; }
        [Required(ErrorMessage = "Name is required.")]
        public string? Name { get; set; }
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address.")]
        public string? Email { get; set; }
        [Required(ErrorMessage = "Address is required.")]
        public string? Address { get; set; }
        [Required(ErrorMessage = "Phone is required.")]
        [Phone(ErrorMessage = "Invalid phone number.")]
        public string? Phone { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

}