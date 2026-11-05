using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CalculadoraMercado.Api;

public sealed class GeminiVisionClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxBase64ImageLength = ((MaxImageBytes + 2) / 3) * 4;

    private static readonly HashSet<string> SupportedMimeTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    private readonly GeminiOptions _options = options.Value;

    public async Task<ProdutoIdentificadoResponse> IdentifyProductAsync(
        IdentificarProdutoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new GeminiConfigurationException();
        }

        if (string.IsNullOrWhiteSpace(request.MimeType)
            || !SupportedMimeTypes.Contains(request.MimeType))
        {
            throw new InvalidImageException("Formato de imagem inválido. Use JPEG, PNG ou WEBP.");
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(request.ImageBase64);
        }
        catch (FormatException)
        {
            throw new InvalidImageException("O conteúdo da imagem não está em base64 válido.");
        }

        if (imageBytes.Length == 0 || imageBytes.Length > MaxImageBytes)
        {
            throw new InvalidImageException("A imagem deve ter no máximo 5 MB.");
        }

        var model = string.IsNullOrWhiteSpace(_options.Model)
            ? "gemini-2.5-flash"
            : _options.Model.Trim();
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey.Trim());
        message.Content = JsonContent.Create(new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = "Você identifica produtos e preços em etiquetas de supermercado. Não invente dados. Se não conseguir ler o nome ou o preço, informe um nome vazio e preço zero."
                    }
                }
            },
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            text = "Leia a etiqueta da imagem. Retorne somente um objeto JSON com as chaves nome (string) e preco (número decimal em reais)."
                        },
                        new
                        {
                            inlineData = new
                            {
                                mimeType = request.MimeType,
                                data = request.ImageBase64
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.1
            }
        });

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            var detail = statusCode == HttpStatusCode.TooManyRequests
                ? "Limite de solicitações do Gemini atingido. Tente novamente em instantes."
                : "O Gemini recusou a solicitação. Verifique a configuração do modelo e da chave.";
            throw new GeminiUpstreamException(statusCode, detail);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("O Gemini retornou uma resposta vazia.");
        }

        var candidateText = parts
            .EnumerateArray()
            .Select(part => part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (candidateText is null)
        {
            throw new InvalidOperationException("O Gemini retornou uma resposta vazia.");
        }

        using var productDocument = JsonDocument.Parse(candidateText);
        var product = productDocument.RootElement;
        var name = product.TryGetProperty("nome", out var nameElement)
            ? nameElement.GetString()?.Trim()
            : null;
        var price = product.TryGetProperty("preco", out var priceElement)
            ? priceElement.GetDouble()
            : 0;
        if (string.IsNullOrWhiteSpace(name) || !double.IsFinite(price) || price <= 0)
        {
            throw new InvalidOperationException("O Gemini não conseguiu ler um nome e preço válidos.");
        }

        return new ProdutoIdentificadoResponse(name, price);
    }
}
