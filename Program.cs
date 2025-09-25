using System.Net.Http;
using System.Text;
using System.IO;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using StandaloneJose;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();



// name the endpoint as per your requirement
app.MapPost("/pg/initiate", async (HttpRequest httpRequest) =>
{
	var baseConfig = builder.Configuration.GetSection("JoseTokenConfig").Get<JoseTokenConfig>();
	if (baseConfig == null)
	{
		return Results.Problem("JoseTokenConfig is missing in appsettings.");
	}

	string body;
	using (var reader = new StreamReader(httpRequest.Body))
	{
		body = await reader.ReadToEndAsync();
	}
	if (string.IsNullOrWhiteSpace(body))
	{
		body = "{}";
	}

	var payload = JObject.Parse(body);

	// Load overrides from environment variables
	var envMerchantId = Environment.GetEnvironmentVariable("PAYGLOCAL_MERCHANT_ID");
	var envPublicKeyId = Environment.GetEnvironmentVariable("PAYGLOCAL_PUBLIC_KEY_ID");
	var envPrivateKeyId = Environment.GetEnvironmentVariable("PAYGLOCAL_PRIVATE_KEY_ID");
	var envPublicKeyPath = Environment.GetEnvironmentVariable("PAYGLOCAL_PUBLIC_KEY");
	var envPrivateKeyPath = Environment.GetEnvironmentVariable("PAYGLOCAL_PRIVATE_KEY");

	string defaultPublicPath = Path.Combine(AppContext.BaseDirectory, "keys", "payglocal_public_key.pem");
	string defaultPrivatePath = Path.Combine(AppContext.BaseDirectory, "keys", "payglocal_private_key.pem");

	string ResolveKey(string? envPath, string fallbackPath)
	{
		var path = !string.IsNullOrWhiteSpace(envPath) ? envPath! : fallbackPath;
		return File.ReadAllText(path);
	}

	string publicPem = ResolveKey(envPublicKeyPath, defaultPublicPath);
	string privatePem = ResolveKey(envPrivateKeyPath, defaultPrivatePath);

	var effectiveConfig = new JoseTokenConfig
	{
		MerchantId = envMerchantId ?? baseConfig.MerchantId,
		PublicKeyId = envPublicKeyId ?? baseConfig.PublicKeyId,
		PrivateKeyId = envPrivateKeyId ?? baseConfig.PrivateKeyId,
		PayglocalPublicKey = publicPem,
		MerchantPrivateKey = privatePem,
	};

	var tokens = JoseTokenUtility.GenerateTokens(payload, effectiveConfig);

	using var client = new HttpClient();
	using var content = new StringContent(tokens.Jwe, Encoding.UTF8, "text/plain");
	using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.uat.payglocal.in/gl/v1/payments/initiate/paycollect")
	{
		Content = content
	};
	request.Headers.Add("x-gl-token-external", tokens.Jws);

	var response = await client.SendAsync(request);
	var responseBody = await response.Content.ReadAsStringAsync();
	var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

	return Results.Content(responseBody, contentType);
});

app.Run();
