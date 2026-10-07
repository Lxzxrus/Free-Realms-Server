using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Security;

/// <summary>
/// Temporary lockouts after failed logins, kept in memory.
/// <list type="bullet">
/// <item>Per account and address: after <see cref="LoginLockoutOptions.FailuresPerAddress"/> failures that address
/// is locked out of that account, for a time that doubles with each further failure up to a cap. An attacker
/// guessing from one address only ever locks out themselves.</item>
/// <item>Per account: many failures from all addresses together lock the account for a fixed window, which stops
/// guessing spread over many addresses. Addresses that have logged into the account before are exempt, so an
/// attacker can't keep the owner out of their own account from their usual connection.</item>
/// </list>
/// Usernames that don't exist are tracked the same way, so a lockout says nothing about whether an account exists.
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxTrustedAddressesPerAccount = 16;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly LoginLockoutOptions _options;

    private readonly Dictionary<(string Account, string Address), AddressState> _addresses = [];
    private readonly Dictionary<string, AccountState> _accounts = [];

    private DateTimeOffset _nextSweep;

    public LoginThrottle(TimeProvider timeProvider, ILogger<LoginThrottle> logger, IOptions<WebAPIOptions> options)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value.LoginLockout;
    }

    /// <summary>How long this address must wait before it may try this account again, or null if it may now.</summary>
    public TimeSpan? GetRetryAfter(string username, string address)
    {
        var account = Normalize(username);
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            Sweep(now);

            TimeSpan? retryAfter = null;

            if (_addresses.TryGetValue((account, address), out var addressState) && addressState.LockedUntil > now)
                retryAfter = addressState.LockedUntil - now;

            if (_accounts.TryGetValue(account, out var accountState)
                && accountState.LockedUntil > now
                && !accountState.IsTrusted(address, now)
                && (retryAfter is null || accountState.LockedUntil - now > retryAfter))
            {
                retryAfter = accountState.LockedUntil - now;
            }

            return retryAfter;
        }
    }

    public void RecordFailure(string username, string address)
    {
        var account = Normalize(username);
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            Sweep(now);

            if (TryGetOrAdd(_addresses, (account, address), out var addressState))
            {
                if (now - addressState.LastFailure >= _options.ForgetFailuresAfter)
                    addressState.Failures = 0;

                addressState.Failures++;
                addressState.LastFailure = now;

                if (addressState.Failures >= _options.FailuresPerAddress)
                {
                    var doublings = Math.Min(addressState.Failures - _options.FailuresPerAddress, 20);
                    var lockout = _options.BaseLockout * (1L << doublings);

                    if (lockout > _options.MaxLockout)
                        lockout = _options.MaxLockout;

                    addressState.LockedUntil = now + lockout;

                    _logger.LogWarning("Login locked for {Lockout} after {Failures} failures. ( Username: {Username}, Address: {Address} )",
                        lockout, addressState.Failures, username, address);
                }
            }

            if (TryGetOrAdd(_accounts, account, out var accountState))
            {
                if (now - accountState.WindowStart >= _options.AccountWindow)
                {
                    accountState.WindowStart = now;
                    accountState.Failures = 0;
                }

                accountState.Failures++;

                if (accountState.Failures == _options.FailuresPerAccount)
                {
                    accountState.LockedUntil = now + _options.AccountWindow;

                    _logger.LogWarning("Account locked for {Lockout} after {Failures} failures from all addresses, except for addresses it has logged in from. ( Username: {Username} )",
                        _options.AccountWindow, accountState.Failures, username);
                }
            }
        }
    }

    public void RecordSuccess(string username, string address)
    {
        var account = Normalize(username);
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            _addresses.Remove((account, address));

            if (!TryGetOrAdd(_accounts, account, out var accountState))
                return;

            accountState.TrustedUntil[address] = now + _options.TrustedAddressLifetime;

            if (accountState.TrustedUntil.Count > MaxTrustedAddressesPerAccount)
                accountState.TrustedUntil.Remove(accountState.TrustedUntil.MinBy(x => x.Value).Key);
        }
    }

    private bool TryGetOrAdd<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        where TKey : notnull
        where TValue : new()
    {
        if (dictionary.TryGetValue(key, out value!))
            return true;

        // Full: stop tracking new entries rather than refuse every login. The per-address rate limit still applies.
        if (dictionary.Count >= _options.MaxTrackedEntries)
        {
            _logger.LogWarning("Login lockout tracking is full ({Count} entries); not tracking new ones until entries expire.", dictionary.Count);
            return false;
        }

        value = new TValue();
        dictionary.Add(key, value);

        return true;
    }

    private void Sweep(DateTimeOffset now)
    {
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        foreach (var (key, state) in _addresses)
        {
            if (state.LockedUntil <= now && now - state.LastFailure >= _options.ForgetFailuresAfter)
                _addresses.Remove(key);
        }

        foreach (var (key, state) in _accounts)
        {
            foreach (var (address, trustedUntil) in state.TrustedUntil)
            {
                if (trustedUntil <= now)
                    state.TrustedUntil.Remove(address);
            }

            if (state.LockedUntil <= now && now - state.WindowStart >= _options.AccountWindow && state.TrustedUntil.Count == 0)
                _accounts.Remove(key);
        }
    }

    private static string Normalize(string username) => username.ToLowerInvariant();

    private sealed class AddressState
    {
        public int Failures { get; set; }
        public DateTimeOffset LastFailure { get; set; }
        public DateTimeOffset LockedUntil { get; set; }
    }

    private sealed class AccountState
    {
        public DateTimeOffset WindowStart { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset LockedUntil { get; set; }
        public Dictionary<string, DateTimeOffset> TrustedUntil { get; } = [];

        public bool IsTrusted(string address, DateTimeOffset now)
        {
            return TrustedUntil.TryGetValue(address, out var trustedUntil) && trustedUntil > now;
        }
    }
}
