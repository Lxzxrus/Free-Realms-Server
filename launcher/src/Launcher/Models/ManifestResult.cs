namespace Launcher.Models;

public enum ManifestResult
{
    Success,
    HttpError,
    NotFound,
    InvalidFormat,
    InvalidVersion,
    UnsupportedVersion,
    DeserializeError
}