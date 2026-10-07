using System;
using System.Linq;

using Launcher.Helpers;

namespace Launcher.ViewModels;

/// <summary>
/// What to tell the player for each WebAPI answer (docs/webapi.md, "Contract for the launcher").
/// </summary>
public static class WebApiMessages
{
    public static string Describe(WebApiResult result, bool isLogin) => result.Status switch
    {
        WebApiStatus.Invalid when result.Errors.Count > 0
            => App.GetText("Text.WebApi.Invalid", string.Join(Environment.NewLine, result.Errors.Select(x => $"• {x}"))),
        WebApiStatus.Invalid => App.GetText("Text.WebApi.InvalidUnknown"),
        WebApiStatus.WrongCredentials => App.GetText("Text.Login.Unauthorized"),
        WebApiStatus.Banned => App.GetText("Text.Login.Banned"),
        WebApiStatus.NameTaken => App.GetText("Text.Register.Conflict"),
        WebApiStatus.TooManyAttempts => App.GetText(
            isLogin ? "Text.Login.TooManyAttempts" : "Text.Register.TooManyAttempts",
            WebApiClient.FormatWait(result.RetryAfter)),
        WebApiStatus.ServerDown => App.GetText("Text.WebApi.ServerDown"),
        WebApiStatus.Insecure => App.GetText("Text.WebApi.Insecure"),
        _ => App.GetText("Text.WebApi.Unexpected", result.HttpStatus is { } status ? (int)status : "no answer")
    };
}
