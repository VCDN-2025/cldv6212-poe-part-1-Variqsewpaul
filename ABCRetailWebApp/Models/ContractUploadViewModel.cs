using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailWebApp.Models
{
    public class ContractUploadViewModel
    {
        [Required(ErrorMessage = "Please select a file to upload.")]
        public IFormFile? File { get; set; }
    }
}