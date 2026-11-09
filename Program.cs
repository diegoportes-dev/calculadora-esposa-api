using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using CalculadoraMercado.Api;
using Microsoft.AspNetCore.RateLimiting;

const long maxRequestBodySize = 8 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySize;
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Gemini", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddHttpClient<GeminiVisionClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));

var app = builder.Build();

app.UseCors("DevelopmentClient");
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/produtos/identificar", async (
    IdentificarProdutoRequest? request,
    GeminiVisionClient gemini,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.ImageBase64))
    {
        return Results.BadRequest(new { error = "Envie a imagem do produto em base64." });
    }

    if (request.ImageBase64.Length > GeminiVisionClient.MaxBase64ImageLength)
    {
        return Results.BadRequest(new { error = "A imagem excede o tamanho máximo permitido de 5 MB." });
    }

    try
    {
        var product = await gemini.IdentifyProductAsync(request, cancellationToken);
        return Results.Ok(product);
    }
    catch (InvalidImageException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (GeminiConfigurationException exception)
    {
        logger.LogError("{Message}", exception.Message);
        return Results.Problem(
            title: "Serviço de identificação indisponível.",
            detail: "Configure a chave Gemini no backend.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (GeminiUpstreamException exception)
    {
        logger.LogWarning("A chamada ao Gemini falhou com status {StatusCode}.", exception.StatusCode);
        return Results.Problem(
            title: "Não foi possível identificar o produto.",
            detail: exception.Message,
            statusCode: exception.StatusCode == HttpStatusCode.TooManyRequests
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "O serviço de identificação excedeu o tempo limite.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "Não foi possível conectar ao Gemini.");
        return Results.Problem(
            title: "Não foi possível conectar ao serviço de identificação.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "A resposta do Gemini não contém um produto válido.");
        return Results.Problem(
            title: "Resposta inválida do serviço de identificação.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (JsonException exception)
    {
        logger.LogError(exception, "O Gemini retornou JSON inválido.");
        return Results.Problem(
            title: "Resposta inválida do serviço de identificação.",
            statusCode: StatusCodes.Status502BadGateway);
    }
})
.RequireRateLimiting("Gemini");

app.Run();
