# Backend da Calculadora de Mercado

API ASP.NET Core 10 que recebe uma imagem de etiqueta, consulta o Gemini no servidor e retorna `{ "nome": "...", "preco": 12.34 }`. A chave do Gemini nunca deve ser colocada no app Expo.

## Configuração local

No PowerShell:

```powershell
cd Backend\CalculadoraMercado.Api
dotnet user-secrets set "Gemini:ApiKey" "SUA_CHAVE_NOVA_DO_GOOGLE_AI_STUDIO"
dotnet run --urls http://0.0.0.0:5270
```

O User Secrets armazena a chave fora do projeto e não a inclui no Git. Não reutilize uma chave que já foi incluída no app ou em um bundle público; revogue-a e crie uma nova.

Verifique se o servidor iniciou:

```powershell
Invoke-RestMethod http://localhost:5270/health
```

## Configuração do app Expo

No `.env.local` do frontend, mantenha somente a URL do backend:

```env
EXPO_PUBLIC_API_URL=http://10.0.2.2:5270
```

`10.0.2.2` é o endereço do computador host no emulador Android. No iOS Simulator, use `http://localhost:5270`. Em um aparelho físico, use o endereço IP local do computador (por exemplo, `http://192.168.1.20:5270`) e permita a porta 5270 no firewall. Reinicie o Expo após mudar o `.env.local`.

Para clientes web em outras origens, adicione a origem exata a `Cors:AllowedOrigins` em `appsettings.Development.json`. CORS não substitui autenticação.

## Endpoint

`POST /api/produtos/identificar`

```json
{
  "mimeType": "image/jpeg",
  "imageBase64": "..."
}
```

Suporta JPEG, PNG e WEBP, com limite de 5 MB por imagem. A API limita cada IP a 10 solicitações por minuto e devolve mensagens seguras sem encaminhar a chave nem a resposta bruta do provedor.

Antes de publicar em produção, configure HTTPS, autenticação/autorização, limites apropriados de uso e monitoramento. A limitação por IP é apenas uma proteção básica para desenvolvimento.
