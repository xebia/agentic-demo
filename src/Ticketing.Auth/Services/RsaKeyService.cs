using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Ticketing.Auth.Services;

/// <summary>
/// Manages RSA key pair for RS256 JWT signing.
/// In production, keys should be loaded from secure storage (Key Vault, HSM, etc.)
/// </summary>
public class RsaKeyService
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _privateKey;
    private readonly RsaSecurityKey _publicKey;
    private readonly string _keyId;

    public RsaKeyService()
    {
        // Generate a new RSA key pair on startup
        // In production, this should be loaded from secure storage
        _rsa = RSA.Create(2048);
        _keyId = Guid.NewGuid().ToString("N")[..8];

        // Create security keys
        _privateKey = new RsaSecurityKey(_rsa)
        {
            KeyId = _keyId
        };

        // Export only public key for validation
        var publicRsa = RSA.Create();
        publicRsa.ImportRSAPublicKey(_rsa.ExportRSAPublicKey(), out _);
        _publicKey = new RsaSecurityKey(publicRsa)
        {
            KeyId = _keyId
        };
    }

    /// <summary>
    /// Gets the signing credentials for token generation (uses private key).
    /// </summary>
    public SigningCredentials GetSigningCredentials()
    {
        return new SigningCredentials(_privateKey, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Gets the key ID for the current key.
    /// </summary>
    public string KeyId => _keyId;

    /// <summary>
    /// Gets the JWKS (JSON Web Key Set) containing only the public key.
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_publicKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        jwk.Kid = _keyId;

        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(jwk);

        return jwks;
    }
}
