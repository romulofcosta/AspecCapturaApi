# ASPEC Capture API (BFF)

API Backend for Frontend (Minimal API .NET 8) projetada para dar suporte ao PWA de Inventário Patrimonial ASPEC Capture. Sua função principal é intermediar operações sensíveis ou incompatíveis com o ambiente WebAssembly (Blazor WASM), especificamente a geração de URLs assinadas para upload seguro no Amazon S3.

![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![Status](https://img.shields.io/badge/Status-Production-green)
![Version](https://img.shields.io/badge/version-1.0.0-blue)

## 📚 Documentação

- **[API Roadmap](docs/API_ROADMAP.md)** - Planejamento de evolução da API
- **[JSON Schemas](../pwa-camera-poc-blazor/docs/API_JSON_SCHEMAS.md)** - Modelos de dados completos
- **[Arquitetura](../pwa-camera-poc-blazor/docs/ARCHITECTURE.md)** - Arquitetura do sistema completo

## 🏗️ Arquitetura

### Problema Resolvido
O SDK da AWS para .NET, quando executado diretamente no navegador via Blazor WebAssembly, enfrenta limitações de criptografia e chamadas HTTP que resultam em erros de runtime (`System.PlatformNotSupportedException`).

### Solução
Esta API atua como um **BFF (Backend for Frontend)** leve:
1. O Front-end (PWA) solicita uma URL de upload para esta API.
2. Esta API (rodando no servidor) usa as credenciais da AWS para gerar uma **Pre-Signed URL** válida por 10 minutos.
3. A API retorna a URL e a Key (caminho) para o Front-end.
4. O Front-end faz o upload do arquivo diretamente para o S3 usando a URL assinada.

## 🚀 Como Rodar

### Pré-requisitos
- .NET 8 SDK instalado
- Acesso a uma conta AWS com permissões de S3 (`PutObject`, `GetObject`, `PutBucketCors`)
- Bucket S3 criado

### Configuração
Configure as credenciais no arquivo `appsettings.json` ou via Variáveis de Ambiente.

**Exemplo de Configuração (`appsettings.json`):**
```json
{
  "AWS": {
    "Region": "us-east-1",
    "BucketName": "aspec-inventory-uploads",
    "AccessKey": "SUA_ACCESS_KEY_AQUI",
    "SecretKey": "SUA_SECRET_KEY_AQUI"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Variáveis de Ambiente (Produção):**
```bash
export AWS__Region="us-east-1"
export AWS__BucketName="aspec-inventory-uploads"
export AWS__AccessKey="SUA_ACCESS_KEY"
export AWS__SecretKey="SUA_SECRET_KEY"
```

### Execução
```bash
dotnet run
```
Acesse Swagger UI em: `http://localhost:5069/swagger`

## 🔌 Documentação da API

### Swagger UI
Acesse a documentação interativa em: `http://localhost:5069/swagger`

### Endpoints Disponíveis

#### `POST /api/storage/presigned-url`
Gera uma URL temporária (pré-assinada) para upload de um arquivo no S3.

**Request Body:**
```json
{
  "fileName": "foto-1.jpg",
  "contentType": "image/jpeg",
  "assetId": "550e8400-e29b-41d4-a716-446655440000",
  "assetCode": "PAT-2024-001234",
  "username": "fiscal.silva",
  "unitName": "Prefeitura-Municipal-Fortaleza"
}
```

**Response:**
```json
{
  "url": "https://bucket.s3.amazonaws.com/capturas/550e8400.../foto-1.jpg?X-Amz-Algorithm=...",
  "key": "capturas/550e8400-e29b-41d4-a716-446655440000/foto-1.jpg"
}
```

**Detalhes:**
- **Validade**: 10 minutos
- **Método HTTP**: PUT (para upload)
- **Sanitização**: Remove acentos e espaços dos nomes
- **Estrutura**: `capturas/{assetId}/{fileName}`

#### `GET /api/storage/exists/{*filePath}`
Verifica se um objeto existe no S3.

**Exemplo:**
```
GET /api/storage/exists/capturas/550e8400-e29b-41d4-a716-446655440000/foto-1.jpg
```

**Response (Existe):**
```json
{
  "exists": true,
  "key": "capturas/550e8400-e29b-41d4-a716-446655440000/foto-1.jpg",
  "url": "https://bucket.s3.us-east-1.amazonaws.com/capturas/550e8400.../foto-1.jpg"
}
```

**Response (Não Existe):**
```json
{
  "exists": false,
  "key": "capturas/550e8400-e29b-41d4-a716-446655440000/foto-1.jpg"
}
```

### Códigos de Status HTTP

| Código | Descrição |
|--------|-----------|
| 200 | Sucesso |
| 400 | Request inválido |
| 403 | Acesso negado (permissões AWS) |
| 404 | Objeto não encontrado |
| 500 | Erro interno do servidor |

### Exemplos de Uso

**cURL - Obter URL Pré-assinada:**
```bash
curl -X POST http://localhost:5069/api/storage/presigned-url \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "foto-1.jpg",
    "contentType": "image/jpeg",
    "assetId": "550e8400-e29b-41d4-a716-446655440000",
    "assetCode": "PAT-2024-001234",
    "username": "fiscal.silva",
    "unitName": "Prefeitura-Municipal-Fortaleza"
  }'
```

**cURL - Upload para S3:**
```bash
curl -X PUT "{URL_PRE_ASSINADA}" \
  -H "Content-Type: image/jpeg" \
  --data-binary @foto-1.jpg
```

**C# - Cliente Blazor:**
```csharp
// 1. Obter URL pré-assinada
var request = new PresignedUrlRequest(
    FileName: "foto-1.jpg",
    ContentType: "image/jpeg",
    AssetId: item.Id,
    AssetCode: item.Codigo,
    Username: currentUser.NomeUsuario,
    UnitName: "Prefeitura-Municipal-Fortaleza"
);

var response = await httpClient.PostAsJsonAsync("/api/storage/presigned-url", request);
var presignedUrl = await response.Content.ReadFromJsonAsync<PresignedUrlResponse>();

// 2. Upload direto para S3
var imageBytes = Convert.FromBase64String(photoBase64.Split(',')[1]);
var uploadResponse = await httpClient.PutAsync(
    presignedUrl.Url, 
    new ByteArrayContent(imageBytes)
);

if (uploadResponse.IsSuccessStatusCode)
{
    Console.WriteLine($"Upload concluído: {presignedUrl.Key}");
}
```

## 🛠️ Desenvolvimento

### Estrutura do Projeto
```
pwa-camera-poc-api/
├── Program.cs              # Configuração e endpoints
├── appsettings.json        # Configurações (Development)
├── appsettings.Development.json
├── Dockerfile              # Container Docker
├── docs/
│   └── API_ROADMAP.md     # Roadmap de evolução
└── Properties/
    └── launchSettings.json # Configurações de debug
```

### Dependências
```xml
<PackageReference Include="AWSSDK.S3" Version="3.7.x" />
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.7.x" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.x" />
```

### Executar em Desenvolvimento
```bash
# Restaurar dependências
dotnet restore

# Executar
dotnet run

# Ou com hot reload
dotnet watch run
```

### Build para Produção
```bash
dotnet publish -c Release -o ./publish
```

### Docker
```bash
# Build
docker build -t aspec-capture-api .

# Run
docker run -p 5069:8080 \
  -e AWS__Region="us-east-1" \
  -e AWS__BucketName="aspec-inventory-uploads" \
  -e AWS__AccessKey="YOUR_KEY" \
  -e AWS__SecretKey="YOUR_SECRET" \
  aspec-capture-api
```

### Testes
```bash
# Unit tests (quando implementados)
dotnet test

# Testes de integração
dotnet test --filter Category=Integration
```

---

## 🔒 Segurança

### CORS
A API está configurada para aceitar requisições de:
- **Development**: Qualquer origem (localhost)
- **Production**: `https://pwa-camera-poc-blazor.pages.dev`

### Credenciais AWS
- ⚠️ **NUNCA** commitar credenciais no código
- ✅ Usar variáveis de ambiente em produção
- ✅ Usar AWS IAM Roles quando possível (ECS, Lambda)
- ✅ Rotacionar chaves regularmente

### Permissões S3 Necessárias
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:GetObjectMetadata",
        "s3:PutBucketCors"
      ],
      "Resource": [
        "arn:aws:s3:::aspec-inventory-uploads/*",
        "arn:aws:s3:::aspec-inventory-uploads"
      ]
    }
  ]
}
```

### Validações Implementadas
- ✅ Sanitização de nomes de arquivo
- ✅ Validação de Content-Type
- ✅ Expiração de URLs (10 minutos)
- ⚠️ TODO: Rate limiting
- ⚠️ TODO: Autenticação JWT
- ⚠️ TODO: Validação de tamanho de arquivo

---

## 📊 Monitoramento

### Logs
A API gera logs estruturados no console:
```
✅ S3 CORS Configured for bucket aspec-inventory-uploads
DEBUG: Generated Key for Pre-signed URL: capturas/550e8400.../foto-1.jpg
DEBUG: Signing with asset-code: PAT-2024-001234
```

### Health Checks (TODO)
```bash
GET /health
GET /health/ready
GET /health/live
```

### Métricas (TODO)
- Requests por segundo
- Tempo de resposta (p50, p95, p99)
- Taxa de erro
- Uploads bem-sucedidos

---

## 🚀 Deploy

### Azure App Service
```bash
az webapp up --name aspec-capture-api --resource-group aspec-rg
```

### AWS Elastic Beanstalk
```bash
eb init -p "Docker running on 64bit Amazon Linux 2" aspec-capture-api
eb create aspec-capture-api-env
```

### Kubernetes
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aspec-capture-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: aspec-capture-api
  template:
    metadata:
      labels:
        app: aspec-capture-api
    spec:
      containers:
      - name: api
        image: aspec-capture-api:latest
        ports:
        - containerPort: 8080
        env:
        - name: AWS__Region
          valueFrom:
            secretKeyRef:
              name: aws-credentials
              key: region
        - name: AWS__BucketName
          valueFrom:
            secretKeyRef:
              name: aws-credentials
              key: bucket
```

---

## 🐛 Troubleshooting

### Erro: "AWS BucketName not configured"
**Solução:** Configurar `AWS:BucketName` no `appsettings.json` ou variável de ambiente.

### Erro: "Access Denied (Forbidden)"
**Solução:** Verificar permissões IAM do usuário AWS. Necessário `s3:PutObject` e `s3:GetObject`.

### Erro: "CORS policy blocked"
**Solução:** 
1. Verificar se a API configurou CORS no S3 (log: "✅ S3 CORS Configured")
2. Verificar se a origem está permitida na política CORS da API

### Erro: "Pre-signed URL expired"
**Solução:** URLs expiram em 10 minutos. Gerar nova URL.

---

## 📝 Changelog

Ver [CHANGELOG.md](../pwa-camera-poc-blazor/CHANGELOG.md) para histórico completo de versões.

### v1.0.0 (2024-02-09)
- ✨ Geração de URLs pré-assinadas
- ✨ Verificação de existência de objetos
- ✨ Configuração automática de CORS no S3
- ✨ Sanitização de nomes de arquivo
- ✨ Swagger UI

---

## 🛠️ Desenvolvimento

---
© 2026 ASPEC. Todos os direitos reservados.