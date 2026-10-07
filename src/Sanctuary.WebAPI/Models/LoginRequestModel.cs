using System.ComponentModel.DataAnnotations;

namespace Sanctuary.WebAPI.Models;

public class LoginRequestModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(50, ErrorMessage = "Username must be at most 50 characters long.")]
    public required string Username { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, ErrorMessage = "Password must be at most 100 characters long.")]
    public required string Password { get; set; }
}