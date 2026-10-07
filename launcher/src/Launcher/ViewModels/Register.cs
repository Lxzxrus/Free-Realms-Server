using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using Launcher.Helpers;

using NLog;

namespace Launcher.ViewModels;

public partial class Register : Popup
{
    private readonly Server _server;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private string? warning;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters long.")]
    [RegularExpression(@"^[a-zA-Z0-9_.]+$", ErrorMessage = "Username can only contain letters, numbers, underscores, and dots.")]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Password is required.")]
    [NotifyPropertyChangedFor(nameof(ConfirmPassword))]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters long.")]
    [RegularExpression(@"^[\x00-\x7F]+$", ErrorMessage = "Password can only contain ASCII characters.")]
    private string password = string.Empty;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(Register), nameof(ValidateConfirmPassword))]
    private string confirmPassword = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public Register(Server server)
    {
        _server = server;

        AddSecureWarning();

        View = new Views.Register
        {
            DataContext = this
        };
    }

    public static ValidationResult? ValidateConfirmPassword(string confirmPassword, ValidationContext context)
    {
        if (context.ObjectInstance is not Register register)
            throw new InvalidOperationException();

        if (!register.Password.Equals(confirmPassword))
            return new ValidationResult(App.GetText("Text.Register.ConfirmPassword.Error"));

        return ValidationResult.Success;
    }

    public override async Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Register.Loading");

        using var httpClient = HttpHelper.CreateHttpClient();

        var result = await WebApiClient.RegisterAsync(httpClient, _server.Info.WebApiUrl, Username, Password);

        if (result.Status != WebApiStatus.Ok)
        {
            _logger.Warn("Registration failed for server '{Name}': {Status} (HTTP {HttpStatus}).", _server.Info.Name, result.Status, (int?)result.HttpStatus);

            App.AddNotification(WebApiMessages.Describe(result, isLogin: false), true);

            if (result.Status == WebApiStatus.NameTaken)
                Username = string.Empty;

            return false;
        }

        Password = string.Empty;
        ConfirmPassword = string.Empty;

        App.AddNotification(App.GetText("Text.Register.Success"));

        return true;
    }

    private void AddSecureWarning()
    {
        if (!TransportPolicy.IsAllowed(_server.Info.WebApiUrl))
            Warning = App.GetText("Text.WebApi.Insecure");
        else if (Uri.TryCreate(_server.Info.WebApiUrl, UriKind.Absolute, out var webApiUrl) && webApiUrl.Scheme != Uri.UriSchemeHttps)
            Warning = App.GetText("Text.Server.LocalHttpWarning");
    }
}