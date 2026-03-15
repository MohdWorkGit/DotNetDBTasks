namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Provides symmetric encryption/decryption for sensitive data at rest (e.g. database passwords).
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
