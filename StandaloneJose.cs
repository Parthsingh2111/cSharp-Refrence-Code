using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Jose;
using Newtonsoft.Json;

namespace StandaloneJose
{
    /// <summary>
    /// Minimal configuration for token generation.
    /// </summary>
    public class JoseTokenConfig
    {
        public string MerchantId { get; set; } = string.Empty;
        public string PublicKeyId { get; set; } = string.Empty;       // kid for JWE
        public string PrivateKeyId { get; set; } = string.Empty;      // kid for JWS
        public string PayglocalPublicKey { get; set; } = string.Empty; // PEM (SPKI) or CERT
        public string MerchantPrivateKey { get; set; } = string.Empty; // PEM (PKCS#8 or PKCS#1)
            
    }

    /// <summary>
    /// Result container for generated tokens.
    /// </summary>
    public class TokenResult
    {
        public string Jwe { get; set; } = string.Empty;
        public string Jws { get; set; } = string.Empty;
    }

    /// <summary>
    /// Standalone JOSE token utility: GenerateJWE, GenerateJWS, GenerateTokens.
    /// </summary>
    public static class JoseTokenUtility
    {
        /// <summary>
        /// Import PEM key into RSA. Supports:
        /// - Private: PKCS#8 (BEGIN PRIVATE KEY), PKCS#1 (BEGIN RSA PRIVATE KEY)
        /// - Public: SPKI (BEGIN PUBLIC KEY), X.509 certificate (BEGIN CERTIFICATE)
        /// </summary>
        public static RSA PemToKey(string pem, bool isPrivate = false)
        {
            if (string.IsNullOrWhiteSpace(pem))
            {
                throw new ArgumentException("PEM must be a non-empty string");
            }

            try
            {
                // Prefer .NET's native PEM parser which supports multiple formats
                var rsa = RSA.Create();

                // If it's a certificate and we want a public key, extract from certificate
                if (!isPrivate && pem.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
                {
                    var cert = X509Certificate2.CreateFromPem(pem);
                    RSA? pub = cert.GetRSAPublicKey();
                    if (pub == null)
                    {
                        throw new ArgumentException("Certificate does not contain an RSA public key");
                    }
                    return pub;
                }

                // Otherwise, let ImportFromPem handle PKCS#1/PKCS#8/SPKI
                rsa.ImportFromPem(pem);
                return rsa;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Crypto error: Invalid PEM format: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generate a JWE over the payload using RSA-OAEP-256 and A128CBC-HS256.
        /// Header claims: iat (string), exp (string), kid, issued-by.
        /// </summary>
        public static string GenerateJWE(object payload, JoseTokenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.MerchantId)) throw new ArgumentException("MerchantId is required");
            if (string.IsNullOrWhiteSpace(config.PublicKeyId)) throw new ArgumentException("PublicKeyId is required");
            if (string.IsNullOrWhiteSpace(config.PayglocalPublicKey)) throw new ArgumentException("PayglocalPublicKey is required");

            long iat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long exp = iat + 30000; // ms (default 5 minutes)

            RSA publicKey = PemToKey(config.PayglocalPublicKey, isPrivate: false);

            string payloadJson = JsonConvert.SerializeObject(payload);

            var jweHeaders = new Dictionary<string, object>
            {
                { "iat", iat.ToString() },
                { "exp", exp.ToString() },
                { "kid", config.PublicKeyId },
                { "issued-by", config.MerchantId }
            };

            string jwe = JWT.Encode(
                payloadJson,
                publicKey,
                JweAlgorithm.RSA_OAEP_256,
                JweEncryption.A128CBC_HS256,
                extraHeaders: jweHeaders
            );

            return jwe;
        }

        /// <summary>
        /// Generate a JWS over SHA-256 digest of input string.
        /// Payload claims: digest, digestAlgorithm, exp (long), iat (string).
        /// Header claims: alg=RS256, issued-by, kid, x-gl-merchantId, x-gl-enc=true, is-digested=true.
        /// </summary>
        public static string GenerateJWS(string toDigest, JoseTokenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.MerchantId)) throw new ArgumentException("MerchantId is required");
            if (string.IsNullOrWhiteSpace(config.PrivateKeyId)) throw new ArgumentException("PrivateKeyId is required");
            if (string.IsNullOrWhiteSpace(config.MerchantPrivateKey)) throw new ArgumentException("MerchantPrivateKey is required");
            if (toDigest == null) throw new ArgumentNullException(nameof(toDigest));

            long iat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long exp = iat +  300000;  // ms (default 5 minutes);

            using var sha256 = SHA256.Create();
            byte[] digestBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(toDigest));
            string digestBase64 = Convert.ToBase64String(digestBytes);

            var payload = new
            {
                digest = digestBase64,
                digestAlgorithm = "SHA-256",
                exp = exp,
                iat = iat.ToString()
            };

            string payloadJson = JsonConvert.SerializeObject(payload);

            var headers = new Dictionary<string, object>
            {
                { "alg", "RS256" },
                { "issued-by", config.MerchantId },
                { "kid", config.PrivateKeyId },
                { "x-gl-merchantId", config.MerchantId },
                { "x-gl-enc", "true" },
                { "is-digested", "true" }
            };

            RSA privateKey = PemToKey(config.MerchantPrivateKey, isPrivate: true);

            string jws = JWT.Encode(
                payloadJson,
                privateKey,
                JwsAlgorithm.RS256,
                extraHeaders: headers
            );

            return jws;
        }

        /// <summary>
        /// Convenience method to generate both JWE and JWS (JWS over the JWE).
        /// </summary>
        public static TokenResult GenerateTokens(object payload, JoseTokenConfig config)
        {
            string jwe = GenerateJWE(payload, config);
            string jws = GenerateJWS(jwe, config);
            return new TokenResult { Jwe = jwe, Jws = jws };
        }
    }
} 