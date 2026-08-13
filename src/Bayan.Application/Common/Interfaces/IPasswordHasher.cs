namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Abstraction for secure password hashing and verification.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}
