using System.Text.RegularExpressions;
using GridPocBlazor.Models;

namespace GridPocBlazor.Services;

public static partial class UserInputRules
{
    public static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    public static string DigitsOnly(string? value, int maxLength = int.MaxValue)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length > maxLength ? digits[..maxLength] : digits;
    }

    public static string LettersAndSeparatorsOnly(string? value, int maxLength = 100)
    {
        var filtered = new string((value ?? string.Empty)
            .Where(c => char.IsLetter(c) || c is ' ' or '\'' or '-' or '.')
            .ToArray());

        filtered = Regex.Replace(filtered, @"\s+", " ").Trim();
        return filtered.Length > maxLength ? filtered[..maxLength] : filtered;
    }

    public static string TrimText(string? value, int maxLength = 200) =>
        (value ?? string.Empty).Trim() is var trimmed && trimmed.Length > maxLength
            ? trimmed[..maxLength]
            : trimmed;

    public static string? ValidateName(string? value, string fieldName)
    {
        var normalized = LettersAndSeparatorsOnly(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"{fieldName} is required.";
        }

        return !NameRegex().IsMatch(normalized)
            ? $"{fieldName} should contain letters only."
            : null;
    }

    public static string? ValidateEmail(string? email)
    {
        var normalized = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Email is required.";
        }

        return !EmailRegex().IsMatch(normalized)
            ? "Enter a valid email address."
            : null;
    }

    public static string? ValidatePhone(string? phone)
    {
        var digits = DigitsOnly(phone, 15);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return "Phone is required.";
        }

        return digits.Length is < 10 or > 15
            ? "Phone number must be 10 to 15 digits."
            : null;
    }

    public static string? ValidateZipCode(string? zipCode)
    {
        var digits = DigitsOnly(zipCode, 10);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        return digits.Length is < 4 or > 10
            ? "Zip code must be 4 to 10 digits."
            : null;
    }

    public static string? ValidatePassword(string? password, bool required)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return required ? "Password is required." : null;
        }

        return password.Trim().Length < 6
            ? "Password must be at least 6 characters."
            : null;
    }

    public static string? ValidateConfirmPassword(string? password, string? confirmPassword, bool required)
    {
        if (string.IsNullOrWhiteSpace(password) && !required)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            return "Please confirm the password.";
        }

        return password?.Trim() != confirmPassword.Trim()
            ? "Password and confirm password must match."
            : null;
    }

    public static string? ValidateUser(User user, bool isNew)
    {
        return ValidateName(user.FirstName, "First name")
            ?? ValidateName(user.LastName, "Last name")
            ?? ValidateEmail(user.Email)
            ?? ValidatePhone(user.Phone)
            ?? ValidateZipCode(user.ZipCode)
            ?? ValidatePassword(user.Password, isNew)
            ?? ValidateConfirmPassword(user.Password, user.ConfirmPassword, isNew);
    }

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z\s'\-\.]*$")]
    private static partial Regex NameRegex();
}
