using System.Security.Cryptography;
using System.Text;

namespace obsidian_sync_manager.Web;

/// <summary>
/// Encrypts LiveSync configuration into a setup URI that the Obsidian plugin can decrypt.
///
/// The plugin uses the <c>octagonal-wheels</c> HKDF encryption format (<c>%$</c> prefix):
///   1. UTF-8 encode the passphrase (no hashing).
///   2. Derive a master key via PBKDF2 (310,000 iterations, random 32-byte salt, SHA-256).
///   3. Derive an AES-256-GCM chunk key via HKDF-SHA256 (random 32-byte salt, empty info).
///   4. Encrypt plaintext UTF-8 bytes with AES-256-GCM (12-byte IV, 128-bit tag).
///   5. Output: <c>%$</c> + Base64( pbkdf2Salt(32) | iv(12) | hkdfSalt(32) | ciphertext + tag ).
///
/// A separate "URI passphrase" protects the setup URI itself so that credentials
/// are not exposed when the URI travels through the clipboard. The user types
/// this passphrase in Obsidian to unlock the settings.
/// </summary>
public sealed class SetupUriEncryptionService
{
    private const int Pbkdf2Iterations = 310_000;
    private const int Pbkdf2SaltLength = 32;
    private const int HkdfSaltLength = 32;
    private const int IvLength = 12;
    private const int GcmTagBits = 128;
    private const int AesKeyLength = 32; // 256-bit
    private const string HkdfSaltedPrefix = "%$";

    private static readonly string[] Adjectives =
    [
        "autumn", "hidden", "bitter", "misty", "silent", "empty", "dry", "dark",
        "summer", "icy", "delicate", "quiet", "white", "cool", "spring", "winter",
        "patient", "twilight", "dawn", "crimson", "wispy", "weathered", "blue",
        "billowing", "broken", "cold", "damp", "falling", "frosty", "green",
        "long", "late", "lingering", "bold", "little", "morning", "muddy", "old",
        "red", "rough", "still", "small", "sparkling", "shy", "wandering",
        "withered", "wild", "black", "young", "holy", "solitary", "fragrant",
        "aged", "snowy", "proud", "floral", "restless", "divine", "polished",
        "ancient", "purple", "lively", "nameless"
    ];

    private static readonly string[] Nouns =
    [
        "waterfall", "river", "breeze", "moon", "rain", "wind", "sea", "morning",
        "snow", "lake", "sunset", "pine", "shadow", "leaf", "dawn", "glitter",
        "forest", "hill", "cloud", "meadow", "sun", "glade", "bird", "brook",
        "butterfly", "bush", "dew", "dust", "field", "fire", "flower", "firefly",
        "feather", "grass", "haze", "mountain", "night", "pond", "darkness",
        "snowflake", "silence", "sound", "sky", "shape", "surf", "thunder",
        "violet", "water", "wildflower", "wave", "resonance", "log", "dream",
        "cherry", "tree", "fog", "frost", "voice", "paper", "frog", "smoke", "star"
    ];

    /// <summary>
    /// Encrypts a plaintext settings JSON string using the octagonal-wheels HKDF
    /// ephemeral-salt format (<c>%$</c> prefix) that the Obsidian LiveSync plugin expects.
    /// </summary>
    public static string Encrypt(string plaintext, string passphrase)
    {
        // 1. PBKDF2: raw UTF-8 passphrase → master key material
        byte[] passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        byte[] pbkdf2Salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltLength);
        byte[] masterKeyBytes = Rfc2898DeriveBytes.Pbkdf2(
            passphraseBytes, pbkdf2Salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, AesKeyLength);

        // 2. HKDF: master key → chunk key (AES-256-GCM)
        byte[] hkdfSalt = RandomNumberGenerator.GetBytes(HkdfSaltLength);
        byte[] chunkKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, masterKeyBytes, AesKeyLength, hkdfSalt, info: []);

        // 3. AES-256-GCM encrypt (plain UTF-8 bytes, no length prefix)
        byte[] iv = RandomNumberGenerator.GetBytes(IvLength);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[GcmTagBits / 8]; // 16 bytes

        using var aes = new AesGcm(chunkKey, tag.Length);
        aes.Encrypt(iv, plaintextBytes, ciphertext, tag);

        // 4. Wire format: pbkdf2Salt(32) | iv(12) | hkdfSalt(32) | ciphertext | tag
        //    (WebCrypto AES-GCM appends the tag to the ciphertext buffer)
        int binaryLen = Pbkdf2SaltLength + IvLength + HkdfSaltLength + ciphertext.Length + tag.Length;
        byte[] binary = new byte[binaryLen];
        int offset = 0;

        pbkdf2Salt.CopyTo(binary, offset); offset += Pbkdf2SaltLength;
        iv.CopyTo(binary, offset);         offset += IvLength;
        hkdfSalt.CopyTo(binary, offset);   offset += HkdfSaltLength;
        ciphertext.CopyTo(binary, offset);  offset += ciphertext.Length;
        tag.CopyTo(binary, offset);

        // 5. Output: %$ + base64(binary)
        return $"{HkdfSaltedPrefix}{Convert.ToBase64String(binary)}";
    }

    /// <summary>
    /// Generates a human-friendly passphrase (adjective-noun) that can be typed manually,
    /// matching the format used by the LiveSync <c>generate_setupuri.ts</c> tool.
    /// </summary>
    public static string GenerateFriendlyPassphrase()
    {
        var adj = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        return $"{adj}-{noun}";
    }

    /// <summary>
    /// Builds an encrypted <c>obsidian://setuplivesync</c> URI with auto-generated passphrases.
    /// </summary>
    public static EncryptedSetupUri CreateEncryptedSetupUri(string settingsJson)
    {
        var uriPassphrase = GenerateFriendlyPassphrase();
        var encrypted = Encrypt(settingsJson, uriPassphrase);
        var uri = $"obsidian://setuplivesync?settings={Uri.EscapeDataString(encrypted)}";
        return new EncryptedSetupUri(uri, uriPassphrase, E2eePassphrase: "");
    }

    /// <summary>
    /// Generates a strong random passphrase of <paramref name="wordCount"/> words for E2EE.
    /// Uses the same adjective/noun word lists as the URI passphrase but with more words
    /// for higher entropy.
    /// </summary>
    public static string GenerateE2eePassphrase(int wordCount = 4)
    {
        var allWords = Adjectives.Concat(Nouns).ToArray();
        return string.Join("-", Enumerable.Range(0, wordCount)
            .Select(_ => allWords[RandomNumberGenerator.GetInt32(allWords.Length)]));
    }
}

public record EncryptedSetupUri(string Uri, string UriPassphrase, string E2eePassphrase);
