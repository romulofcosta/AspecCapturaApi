# pwa-camera-poc-api

API Backend (Minimal API .NET 10) projetada para dar suporte ao PWA de Inventário Patrimonial. Sua função principal é intermediar operações sensíveis ou incompatíveis com o ambiente WebAssembly (Blazor WASM), especificamente a geração de URLs assinadas para upload seguro no Amazon S3.

##  Arquitetura

### Problema Resolvido
O SDK da AWS para .NET, quando executado diretamente no navegador via Blazor WebAssembly, enfrenta limitações de criptografia e chamadas HTTP que resultam em erros de runtime (`System.PlatformNotSupportedException` ou falhas de assinatura).

### Solução
Esta API atua como um **BFF (Backend for Frontend)** leve:
1. O Front-end (PWA) solicita uma URL de upload para esta API.
2. Esta API (rodando no servidor) usa as credenciais da AWS para gerar uma **Pre-Signed URL** válida por 15 minutos.
3. A API retorna a URL e a Key (caminho) para o Front-end.
4. O Front-end faz o upload do arquivo diretamente para o S3 usando a URL assinada, sem trafegar o binário pela API.

##  Como Rodar

### Pré-requisitos
- .NET 10 SDK instalado.
- Acesso a uma conta AWS com permissões de S3 (`PutObject`).

### Configuração
Configure as credenciais no arquivo `appsettings.Development.json` ou via Variáveis de Ambiente (recomendado para produção).

**appsettings.Development.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AWS": {
    "Region": "us-east-1",
    "BucketName": "seu-bucket-aqui",
    "AccessKey": "SUA_ACCESS_KEY",
    "SecretKey": "SUA_SECRET_KEY"
  }
}
```

### Execução
Na raiz da pasta `pwa-camera-poc-api`:

```bash
dotnet run
```

A API estará disponível em:
- Swagger UI: http://localhost:5069/swagger
- Endpoint: http://localhost:5069/api/storage/presigned-url

## � Documentação da API

### `POST /api/storage/presigned-url`

Gera uma URL temporária para upload de um arquivo (imagem) para o S3.

**Corpo da Requisição (JSON):**

| Propriedade   | Tipo   | Obrigatório | Descrição |
|---------------|--------|-------------|-----------|
| `fileName`    | string | Sim         | Nome do arquivo (ex: `foto1.jpg`). |
| `contentType` | string | Sim         | Tipo MIME (ex: `image/jpeg`). |
| `assetId`     | string | Sim         | ID do Ativo (`InventoryItem.Id`). Usado como pasta no S3. |
| `assetCode`   | string | Não         | Código do Ativo (`InventoryItem.Code`). Salvo como metadado. |

**Exemplo de Requisição:**

```bash
curl -X POST "http://localhost:5069/api/storage/presigned-url" \
     -H "Content-Type: application/json" \
     -d '{
           "fileName": "evidence_photo.jpg",
           "contentType": "image/jpeg",
           "assetId": "550e8400-e29b-41d4-a716-446655440000",
           "assetCode": "INV-2024-001"
         }'
```

**Exemplo de Resposta (200 OK):**

```json
{
  "url": "https://seu-bucket.s3.amazonaws.com/550e8400.../evidence_photo.jpg?X-Amz-Algorithm=...",
  "key": "550e8400-e29b-41d4-a716-446655440000/evidence_photo.jpg"
}
```

##  Desenvolvimento

### Dependências Principais
- `AWSSDK.S3`: Comunicação com Amazon S3.
- `AWSSDK.Extensions.NETCore.Setup`: Injeção de dependência AWS.
- `Swashbuckle.AspNetCore`: Documentação Swagger (v10.1.0+).

### Estrutura de Pastas
- `Program.cs`: Contém toda a lógica da Minimal API (Configuração, DI, Rotas).
- `docs/`: Documentação do projeto (Changelog, Guias).