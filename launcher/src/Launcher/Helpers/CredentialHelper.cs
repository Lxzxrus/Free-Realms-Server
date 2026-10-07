using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Launcher.Models;

using Microsoft.AspNetCore.DataProtection;

using NLog;

namespace Launcher.Helpers;

public static class CredentialHelper
{
    private const string KeysDirectory = "keys";
    private const string PasswordFile = "passwords.dat";
    private const string ProtectorPurpose = "OSFRLauncher.Passwords.v1";

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly object _sync = new();
    private static readonly string _storePath = Path.Combine(Constants.SavePath, PasswordFile);
    private static readonly IDataProtector? _protector = CreateProtector();

    private static IDataProtector? CreateProtector()
    {
        try
        {
            var keyDirectory = new DirectoryInfo(Path.Combine(Constants.SavePath, KeysDirectory));
            keyDirectory.Create();

            return DataProtectionProvider
                .Create(keyDirectory)
                .CreateProtector(ProtectorPurpose);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Password protection unavailable.");

            return null;
        }
    }

    public static string? GetPassword(ServerInfo server)
    {
        if (_protector is null)
            return null;

        try
        {
            lock (_sync)
            {
                return Load().TryGetValue(server.Url, out var protectedPassword)
                    ? _protector.Unprotect(protectedPassword)
                    : null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to read the stored password.");

            return null;
        }
    }

    public static void SavePassword(ServerInfo server, string password)
    {
        if (_protector is null)
            return;

        try
        {
            lock (_sync)
            {
                var store = Load();
                store[server.Url] = _protector.Protect(password);
                Save(store);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to write the password to protected storage.");
        }
    }

    public static void Clear(ServerInfo server)
    {
        if (_protector is null)
            return;

        try
        {
            lock (_sync)
            {
                var store = Load();

                if (store.Remove(server.Url))
                    Save(store);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove the password from protected storage.");
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_storePath))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            var json = File.ReadAllText(_storePath);

            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load protected storage.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void Save(Dictionary<string, string> store)
    {
        Directory.CreateDirectory(Constants.SavePath);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(store));
    }
}
