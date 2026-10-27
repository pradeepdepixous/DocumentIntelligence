using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Persistence;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Services.Accounts;

/// <summary>
/// Creates/authenticates customer portal accounts via Google sign-in and/or email+password.
/// Admin status is recomputed from the configured allowlist on every successful sign-in rather
/// than stored permanently, so removing an email from the allowlist takes effect immediately.
/// </summary>
public class CustomerAccountService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // PBKDF2 with a per-password random salt and a high iteration count (ASP.NET Core Identity's
    // default hasher) — not a hand-rolled scheme, and not reusing KeyHasher's raw SHA-256 (that's
    // fine for a high-entropy random API key, but never appropriate for a human-chosen password).
    private static readonly PasswordHasher<CustomerAccountEntity> PasswordHasher = new();

    private readonly DocumentIntelligenceDbContext _db;
    private readonly PortalAuthOptions _options;

    public CustomerAccountService(DocumentIntelligenceDbContext db, PortalAuthOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<CustomerAccountEntity> UpsertFromGoogleAsync(
        string googleSubject,
        string email,
        string name,
        string? pictureUrl,
        CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        bool isAdmin = IsAllowlistedAdmin(email);

        // A Google sign-in using an email that already registered with a password links the
        // Google identity onto that same account instead of creating a duplicate/conflicting row
        // (Email has a unique index, so a blind insert here would otherwise throw).
        var existing = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.GoogleSubject == googleSubject, cancellationToken)
            ?? await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);

        if (existing == null)
        {
            existing = new CustomerAccountEntity
            {
                GoogleSubject = googleSubject,
                Email = email,
                Name = name,
                PictureUrl = pictureUrl,
                IsAdmin = isAdmin,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                CreditLimit = _options.DefaultAccountCreditLimit,
                BillingCycle = CurrentBillingCycle()
            };
            _db.CustomerAccounts.Add(existing);
        }
        else
        {
            existing.GoogleSubject = googleSubject;
            existing.Email = email;
            existing.Name = name;
            existing.PictureUrl = pictureUrl;
            existing.IsAdmin = isAdmin;
            existing.LastLoginAt = DateTime.UtcNow;
            existing.FailedLoginAttempts = 0;
            existing.LockedOutUntil = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<(bool Success, CustomerAccountEntity? Account, string? Error)> RegisterWithPasswordAsync(
        string email, string password, string name, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        if (!IsValidEmail(email))
        {
            return (false, null, "Enter a valid email address.");
        }
        if (!IsPasswordStrongEnough(password))
        {
            return (false, null, "Password must be at least 8 characters and include a letter and a number.");
        }

        var existing = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (existing != null)
        {
            return (false, null, existing.PasswordHash == null
                ? "This email is already registered via Google Sign-In — use that instead."
                : "This email is already registered. Try signing in.");
        }

        var account = new CustomerAccountEntity
        {
            Email = email,
            Name = string.IsNullOrWhiteSpace(name) ? email : name.Trim(),
            IsAdmin = IsAllowlistedAdmin(email),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            CreditLimit = _options.DefaultAccountCreditLimit,
            BillingCycle = CurrentBillingCycle()
        };
        account.PasswordHash = PasswordHasher.HashPassword(account, password);

        _db.CustomerAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        return (true, account, null);
    }

    public async Task<(bool Success, CustomerAccountEntity? Account, string? Error)> AuthenticateWithPasswordAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        // Same generic message whether the email doesn't exist, has no password set, or the
        // password is simply wrong — never reveal which case it was (avoids account enumeration).
        const string genericError = "Invalid email or password.";

        email = NormalizeEmail(email);
        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (account == null || account.PasswordHash == null)
        {
            return (false, null, genericError);
        }

        if (account.LockedOutUntil.HasValue)
        {
            if (account.LockedOutUntil.Value > DateTime.UtcNow)
            {
                int minutesLeft = (int)Math.Ceiling((account.LockedOutUntil.Value - DateTime.UtcNow).TotalMinutes);
                return (false, null, $"Too many failed attempts. Try again in {minutesLeft} minute(s).");
            }

            // Lockout window has passed; clear it before evaluating this attempt.
            account.LockedOutUntil = null;
            account.FailedLoginAttempts = 0;
        }

        var verifyResult = PasswordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                account.LockedOutUntil = DateTime.UtcNow.Add(LockoutDuration);
                account.FailedLoginAttempts = 0;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return (false, null, genericError);
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = PasswordHasher.HashPassword(account, password);
        }

        account.FailedLoginAttempts = 0;
        account.LockedOutUntil = null;
        account.LastLoginAt = DateTime.UtcNow;
        account.IsAdmin = IsAllowlistedAdmin(account.Email);

        await _db.SaveChangesAsync(cancellationToken);
        return (true, account, null);
    }

    public async Task<CustomerAccountEntity?> GetByIdAsync(string accountId, CancellationToken cancellationToken = default) =>
        await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);

    public async Task<List<CustomerAccountEntity>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await _db.CustomerAccounts.OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);

    /// <summary>
    /// Checks whether an account has enough remaining credit for an incoming page count — the
    /// account-level equivalent of ICredentialRegistry.CheckQuotaAsync, shared by every self-serve
    /// key the account owns plus direct Playground testing via the portal session.
    /// </summary>
    public async Task<(bool Allowed, int Remaining, string Message)> CheckCreditAsync(
        string accountId, int incomingPages, CancellationToken cancellationToken = default)
    {
        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account == null)
        {
            return (false, 0, $"Account '{accountId}' not found.");
        }

        RolloverBillingCycleIfNeeded(account);
        await _db.SaveChangesAsync(cancellationToken);

        int remaining = Math.Max(0, account.CreditLimit - account.CreditsUsed);
        if (account.CreditsUsed + incomingPages > account.CreditLimit)
        {
            return (false, remaining,
                $"Monthly credit limit exceeded. Limit: {account.CreditLimit:N0}, used: {account.CreditsUsed:N0}, requested: {incomingPages:N0}.");
        }

        return (true, remaining, "Credit available.");
    }

    /// <summary>
    /// Records credit consumption against the account's shared pool — called instead of (for
    /// self-serve keys, in addition to) ICredentialRegistry.RecordUsageAsync's per-key counters.
    /// </summary>
    public async Task RecordCreditUsageAsync(string accountId, int pages, CancellationToken cancellationToken = default)
    {
        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account == null)
        {
            return;
        }

        RolloverBillingCycleIfNeeded(account);
        account.CreditsUsed += pages;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Admin action: grants or adjusts an account's shared credit limit.</summary>
    public async Task<CustomerAccountEntity?> SetCreditLimitAsync(string accountId, int newLimit, CancellationToken cancellationToken = default)
    {
        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId, cancellationToken);
        if (account == null)
        {
            return null;
        }

        account.CreditLimit = newLimit;
        await _db.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static void RolloverBillingCycleIfNeeded(CustomerAccountEntity account)
    {
        string currentCycle = CurrentBillingCycle();
        if (!string.Equals(account.BillingCycle, currentCycle, StringComparison.Ordinal))
        {
            account.BillingCycle = currentCycle;
            account.CreditsUsed = 0;
        }
    }

    private static string CurrentBillingCycle() => DateTime.UtcNow.ToString("yyyy-MM");

    private bool IsAllowlistedAdmin(string email) =>
        _options.AdminEmails.Any(a => string.Equals(NormalizeEmail(a), email, StringComparison.Ordinal));

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsPasswordStrongEnough(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return false;
        }
        return password.Any(char.IsLetter) && password.Any(char.IsDigit);
    }
}
