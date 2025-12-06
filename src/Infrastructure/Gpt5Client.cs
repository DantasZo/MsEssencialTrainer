using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MsFundamentals.Trainer.Infrastructure;

public sealed class Gpt5Client
{
    private readonly HttpClient _http;
    private readonly ILogger<Gpt5Client> _logger;
    private readonly IConfiguration _cfg;

    private readonly int _maxTokens;
    private readonly int _maxPromptLength;
    private readonly bool _useAzure;
    private readonly string? _azureEndpoint;
    private readonly string? _azureDeployment;
    private readonly string? _azureApiVersion;
    private readonly string? _azureApiKey;

    public Gpt5Client(IConfiguration cfg, ILogger<Gpt5Client> logger, IHttpClientFactory factory)
    {
        _cfg = cfg;
        _logger = logger;
        _http = factory.CreateClient(nameof(Gpt5Client));
        _http.Timeout = TimeSpan.FromSeconds(60);

        _maxTokens = int.TryParse(_cfg["OpenAI:MaxTokens"], out var mt) ? mt : 1500;
        _maxPromptLength = int.TryParse(_cfg["OpenAI:MaxPromptLength"], out var mpl) ? mpl : 18000;

        // Azure config
        _useAzure = bool.TryParse(_cfg["AzureOpenAI:UseAzure"], out var ua) ? ua : false;
        _azureEndpoint = _cfg["AzureOpenAI:Endpoint"];
        _azureDeployment = _cfg["AzureOpenAI:Deployment"];
        _azureApiVersion = _cfg["AzureOpenAI:ApiVersion"];
        _azureApiKey = _cfg["AzureOpenAI:ApiKey"]; // expected from UserSecrets in dev

        // Headers
        if (_useAzure)
        {
            if (string.IsNullOrWhiteSpace(_azureApiKey))
            {
                _logger.LogWarning("AzureOpenAI:ApiKey não definido (use User Secrets).");
            }
            _http.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrWhiteSpace(_azureApiKey))
            {
                _http.DefaultRequestHeaders.Remove("api-key");
                _http.DefaultRequestHeaders.Add("api-key", _azureApiKey);
            }
        }
        else
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _cfg["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("OPENAI_API_KEY não definido. Chamadas OpenAI falharão sem chave.");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private string BuildAzureUrl()
    {
        if (!string.IsNullOrWhiteSpace(_azureEndpoint) &&
            _azureEndpoint.Contains("/deployments/", StringComparison.OrdinalIgnoreCase) &&
            _azureEndpoint.Contains("api-version=" , StringComparison.OrdinalIgnoreCase))
        {
            return _azureEndpoint; 
        }

        if (string.IsNullOrWhiteSpace(_azureEndpoint) || string.IsNullOrWhiteSpace(_azureDeployment) || string.IsNullOrWhiteSpace(_azureApiVersion))
            throw new InvalidOperationException("Configuração AzureOpenAI incompleta (Endpoint/Deployment/ApiVersion).");

        var baseUrl = _azureEndpoint.TrimEnd('/');
        return $"{baseUrl}/openai/deployments/{_azureDeployment}/chat/completions?api-version={_azureApiVersion}";
    }

    public async Task<(string content, int tokensIn, int tokensOut)> ChatJsonAsync(string systemPrompt, string userPrompt, int? maxTokens = null, CancellationToken ct = default)
    {
        object payload;

        if (_useAzure)
        {
            payload = new
            {
                response_format = ToonCodec.AzureQuestionSchema,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = maxTokens ?? _maxTokens,
                temperature = 0.2
            };
        }
        else
        {
            var model = _cfg["OpenAI:Model"] ?? "gpt-5";
            payload = new
            {
                model = model,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = maxTokens ?? _maxTokens,
                temperature = 0.2
            };
        }

        var json = JsonSerializer.Serialize(payload);
        if (json.Length > _maxPromptLength)
        {
            // Truncate userPrompt na emergência (MVP)
            var over = json.Length - _maxPromptLength;
            var trimmedUser = userPrompt[..Math.Max(0, userPrompt.Length - over - 100)];
            if (_useAzure)
            {
                payload = new
                {
                    response_format = ToonCodec.AzureQuestionSchema,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = trimmedUser }
                    },
                    max_tokens = maxTokens ?? _maxTokens,
                    temperature = 0.2
                };
            }
            else
            {
                var model = _cfg["OpenAI:Model"] ?? "gpt-5";
                payload = new
                {
                    model = model,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = trimmedUser }
                    },
                    max_tokens = maxTokens ?? _maxTokens,
                    temperature = 0.2
                };
            }
            json = JsonSerializer.Serialize(payload);
        }

        var url = _useAzure
            ? BuildAzureUrl()
            : $"{(_cfg["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/')}/chat/completions";

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _logger.LogError("LLM error: {Status} {Body}", res.StatusCode, body);
            throw new InvalidOperationException($"LLM API error: {res.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";

        if (_useAzure)
        {
            // Azure: converter formato toon (chaves curtas) para JSON padrão esperado pelos serviços.
            content = ToonCodec.NormalizeQuestions(content);
        }

        // usage tokens (Azure/OpenAI compatible)
        int inTok = 0, outTok = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inTok = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ctok)) outTok = ctok.GetInt32();
        }

        return (content, inTok, outTok);
    }
}
