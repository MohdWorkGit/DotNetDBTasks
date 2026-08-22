using System.Security.Cryptography;
using System.Text;

namespace Bayan.Infrastructure.Caching;

/// <summary>
/// Encrypts the rows of a disk-spilled cached result so the files under
/// <c>ResultCache:SpillDirectory</c> are unreadable to anything with filesystem access — the spill
/// directory lives inside the deployed app folder and inherits its ACLs, so without this a full
/// result set could be read straight out of the NDJSON file.
///
/// <para>Each row is sealed on its own with AES-256-GCM and Base64-encoded, rather than the file
/// being encrypted as a whole: <see cref="DiskCachedResult"/> seeks to a row by byte offset, so a
/// single stream over the file would break every paged read. Base64 also keeps the one-row-per-line
/// framing intact, since its alphabet cannot produce a newline.</para>
///
/// <para>The key is generated per process and never leaves memory. That is deliberate rather than
/// reusing <c>Encryption:Key</c>: the threat here is someone reading the spill files off the server,
/// and such a reader could equally read the key out of appsettings.json. Nothing is lost by letting
/// the key die with the process, because <see cref="BackgroundJobs.InMemoryQueryJobStore"/> already
/// wipes the whole spill directory at both startup and shutdown — spill files are never meant to
/// outlive the process that wrote them.</para>
/// </summary>
internal sealed class ResultCacheCipher
{
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;   // AES-GCM authentication tag

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Opens a scope holding one <see cref="AesGcm"/> for a whole read or write pass. Callers keep a
    /// session for the duration of a scan instead of per row: importing the key allocates a native
    /// key handle, which would otherwise dominate the cost of an AES-NI-accelerated operation.
    /// <see cref="AesGcm"/> is not documented thread-safe, so a session must not be shared.
    /// </summary>
    public Session Open() => new(_key);

    internal sealed class Session : IDisposable
    {
        private readonly AesGcm _aes;

        internal Session(byte[] key) => _aes = new AesGcm(key, TagSize);

        /// <summary>Seals one encoded row as Base64 of [nonce][tag][ciphertext].</summary>
        public string Protect(string plainText)
        {
            var plain = Encoding.UTF8.GetBytes(plainText);
            var envelope = new byte[NonceSize + TagSize + plain.Length];

            // A fresh nonce per row, stored inline, so a line decrypts from itself alone — the
            // readers below hand out byte offsets, not row ordinals, so there is no counter to key.
            var nonce = envelope.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);

            _aes.Encrypt(
                nonce,
                plain,
                envelope.AsSpan(NonceSize + TagSize, plain.Length),
                envelope.AsSpan(NonceSize, TagSize));

            return Convert.ToBase64String(envelope);
        }

        /// <summary>
        /// Opens a line written by <see cref="Protect"/>. Throws <see cref="CryptographicException"/>
        /// if the file was tampered with or was written by a different process's key.
        /// </summary>
        public string Unprotect(string cipherLine)
        {
            var envelope = Convert.FromBase64String(cipherLine);
            if (envelope.Length < NonceSize + TagSize)
                throw new CryptographicException("Cached result row is too short to be a sealed row.");

            var plain = new byte[envelope.Length - NonceSize - TagSize];
            _aes.Decrypt(
                envelope.AsSpan(0, NonceSize),
                envelope.AsSpan(NonceSize + TagSize),
                envelope.AsSpan(NonceSize, TagSize),
                plain);

            return Encoding.UTF8.GetString(plain);
        }

        public void Dispose() => _aes.Dispose();
    }
}
