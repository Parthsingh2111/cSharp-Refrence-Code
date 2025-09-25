# PayGlocal Server-to-Server Reference (C# / .NET) 

This repository contains minimal reference code to initiate a PayGlocal payment server-to-server.

Important: This is reference code and uses the PayGlocal UAT (testing environment) endpoint. Once testing is complete, the endpoint should be updated for production.

## What this does
- Exposes a minimal API endpoint: `POST /pg/initiate`
- Accepts your JSON payload as-is (unchanged)
- Generates a JWE (encrypted payload) using PayGlocal's public key
- Generates a JWS (signature over the JWE) using your merchant private key
- Sends a server-to-server request to PayGlocal with:
  - Body: raw JWE string (Content-Type `text/plain`)
  - Header: `x-gl-token-external: <JWS>`
- Returns PayGlocal's response body and Content-Type to the caller

## Requirements
- .NET 9 SDK
- PayGlocal test/prod credentials and RSA keys
  - Merchant ID
  - Public Key Id (JWE key id)
  - Private Key Id (JWS key id)
  - Public key (SPKI PEM or certificate PEM)
  - Private key (PKCS#8 or PKCS#1 PEM)

## Setup
1) Install dependencies
```bash
dotnet restore
```

2) Provide credentials and keys (no secrets checked in)
- Option A: Put PEM key files inside `keys/` (preferred for local dev)
  - `keys/payglocal_public_key.pem` → PayGlocal public key (SPKI or CERT)
  - `keys/payglocal_private_key.pem` → Merchant private key (PKCS#8 or PKCS#1)
  - Then set environment variables to these relative paths:
    - `PAYGLOCAL_PUBLIC_KEY=keys/payglocal_public_key.pem`
    - `PAYGLOCAL_PRIVATE_KEY=keys/payglocal_private_key.pem`
- Option B: Use absolute paths for your key files via environment variables

Always set the following environment variables with YOUR values:
- `PAYGLOCAL_MERCHANT_ID`
- `PAYGLOCAL_PUBLIC_KEY_ID` (kid used for JWE)
- `PAYGLOCAL_PRIVATE_KEY_ID` (kid used for JWS)
- `PAYGLOCAL_PUBLIC_KEY` (PEM path)
- `PAYGLOCAL_PRIVATE_KEY` (PEM path)

Note: `appsettings.json` contains a `JoseTokenConfig` section used as a fallback. The effective values come from env + key files if present.

3) Run locally
```bash
dotnet run
```
You should see the app listening on localhost (see `Properties/launchSettings.json`).

## Calling the endpoint
// name the endpoint as per your requirement
Send a JSON body to your backend `POST /pg/initiate` (you can rename this route as needed). The payload must include a payment instrument (card or token) or PayGlocal will reject it (GL-400-001).

Example with token (recommended):
```bash
curl -sS -X POST http://localhost:5270/pg/initiate \
  -H 'Content-Type: application/json' \
  -d '{
    "merchantTxnId": "TXN_123",
    "paymentData": {
      "totalAmount": "500.00",
      "txnCurrency": "INR",
    },
    "merchantCallbackURL": "https://your-domain.com/payment/callback"
  }'
```

Example with card (applicable only if you are PCI compliant): For the paydirect method, the endpoint used will be:
https://api.uat.payglocal.in/gl/v1/payments/initiate

```bash
curl -sS -X POST http://localhost:5270/pg/initiate \
  -H 'Content-Type: application/json' \
  -d '{
    "merchantTxnId": "TXN_123",
    "paymentData": {
      "totalAmount": "500.00",
      "txnCurrency": "INR",
      "paymentMethod": {
        "type": "CARD",
        "card": {
          "number": "5123450000000008",
          "holder": "John Doe",
          "expiryMonth": "12",
          "expiryYear": "30",
          "cvv": "123"
        }
      }
    },
    "merchantCallbackURL": "https://your-domain.com/payment/callback"
  }'
```

Note: This reference targets merchants whose backend is implemented in C#/.NET. Integrate server-to-server by calling this endpoint from your backend. If you do have a browser frontend, it should call your backend route (not PayGlocal directly).

## Redirect handling
Inspect PayGlocal's response for a redirect URL (field name may vary by flow/version, e.g. `redirectUrl` or `paymentPageURL`). Redirect the customer from the frontend:
```javascript
const res = await fetch('/pg/initiate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
const data = await res.json();
const url = data.redirectUrl || data.paymentPageURL || data?.instrumentResponse?.redirectUrl;
if (url) window.location.assign(url);
```

## Direct S2S (without this endpoint)
You can embed the JOSE utility directly in your server instead of calling `/pg/initiate`:
```csharp
var config = new StandaloneJose.JoseTokenConfig {
    MerchantId = "YOUR_MERCHANT_ID",
    PublicKeyId = "YOUR_JWE_KID",
    PrivateKeyId = "YOUR_JWS_KID",
    PayglocalPublicKey = File.ReadAllText("keys/payglocal_public_key.pem"),
    MerchantPrivateKey = File.ReadAllText("keys/payglocal_private_key.pem"),
};
var jwe = StandaloneJose.JoseTokenUtility.GenerateJWE(payload, config);
var jws = StandaloneJose.JoseTokenUtility.GenerateJWS(jwe, config);
// POST jwe as text/plain with header x-gl-token-external: jws
```

## Security and operations
- Do not commit keys. Use environment variables and secure secret stores.
- Add authentication and rate limiting to `/pg/initiate` if exposed beyond your own systems.
- Enable CORS only for allowed origins if called from a browser.
- Keep server clock in sync (iat/exp are time-based).
- UAT vs Production: this reference targets UAT `https://api.uat.payglocal.in`. Change the URL for production.

## Troubleshooting
- "Invalid PEM format": Ensure public is SPKI/CERT and private is PKCS#8 or PKCS#1. Include `BEGIN/END` lines.
- `GL-400-001` (Payment data incorrect): Add a payment instrument (card or token) to your payload.
- Only body returned: The endpoint returns upstream body + Content-Type. If you need upstream status codes/headers forwarded, adjust the endpoint to proxy them explicitly.

## License / Disclaimer
This is reference code intended to demonstrate the minimal server-to-server initiate flow. Review, extend, and harden before production use. 
