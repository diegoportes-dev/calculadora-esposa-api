using System.Net;

namespace CalculadoraMercado.Api;

public sealed record IdentificarProdutoRequest(string ImageBase64, string MimeType);

public sealed record ProdutoIdentificadoResponse(string Nome, double Preco);

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";
}

public sealed class InvalidImageException(string message) : Exception(message)
{
}

public sealed class GeminiConfigurationException()
    : Exception("A chave do Gemini não está configurada.")
{
}

public sealed class GeminiUpstreamException(HttpStatusCode statusCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
