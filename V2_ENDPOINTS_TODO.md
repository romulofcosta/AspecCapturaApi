## Backend API v2 - Endpoints Implementation Guide

**Status**: DTOs and middleware created, endpoints NOT YET implemented  
**Location**: `c:\Users\romulo.costa\.gemini\antigravity\scratch\pwa-camera-poc-api`

---

## 📋 Remaining Backend Implementation

### 1. Update Program.cs

Add middleware registration and v2 endpoints:

```csharp
// Add to Program.cs after builder services configuration

// Register ApiKeyAuthMiddleware
app.UseMiddleware<ApiKeyAuthMiddleware>();

// Map v2 endpoints
app.MapGet("/api/v2/inventario/carga/{ugId}", GetInventorioCargaPresignedUrl)
    .WithName("GetInventorioCarga")
    .WithOpenApi()
    .Produces<ProvisioningUrlResponseDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/api/v2/auth/usuarios/{ugId}", GetUsuariosPresignedUrl)
    .WithName("GetUsuarios")
    .WithOpenApi()
    .Produces<ProvisioningUrlResponseDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status500InternalServerError);

// Endpoint implementations (see below)
static async Task<IResult> GetInventorioCargaPresignedUrl(
    int ugId, 
    IAmazonS3 s3Client, 
    IConfiguration config, 
    ILogger<Program> logger)
{
    try
    {
        var bucketName = config["AWS:BucketName"] ?? "aspec-capture";
        var s3CarlosPath = config["AWS:S3Paths:Cargas"] ?? "cargas";
        var key = $"{s3CarlosPath}/ug_{ugId}_itens.json";

        logger.LogInformation($"[v2 API] Generating Pre-Signed URL for inventory carga: {key}");

        var presignedUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        var presignedUrl = s3Client.GetPreSignedURL(presignedUrlRequest);

        return Results.Ok(new ProvisioningUrlResponseDto
        {
            PresignedUrl = presignedUrl,
            Key = key,
            Bucket = bucketName,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ContentType = "application/json"
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"[v2 API] Error generating inventory URL: {ex.Message}");
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
}

static async Task<IResult> GetUsuariosPresignedUrl(
    int ugId,
    IAmazonS3 s3Client,
    IConfiguration config,
    ILogger<Program> logger)
{
    try
    {
        var bucketName = config["AWS:BucketName"] ?? "aspec-capture";
        var s3CarlosPath = config["AWS:S3Paths:Cargas"] ?? "cargas";
        var key = $"{s3CarlosPath}/ug_{ugId}_users.json";

        logger.LogInformation($"[v2 API] Generating Pre-Signed URL for users: {key}");

        var presignedUrlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        };

        var presignedUrl = s3Client.GetPreSignedURL(presignedUrlRequest);

        return Results.Ok(new ProvisioningUrlResponseDto
        {
            PresignedUrl = presignedUrl,
            Key = key,
            Bucket = bucketName,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ContentType = "application/json"
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"[v2 API] Error generating users URL: {ex.Message}");
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }
}
```

### 2. Update appsettings.json

Ensure these settings exist:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "AWS": {
    "Region": "us-east-1",
    "BucketName": "aspec-capture",
    "S3Paths": {
      "Cargas": "cargas",
      "Capturas": "capturas"
    }
  },
  "Api": {
    "ApiKey": "your-secure-api-key-here-change-in-production"
  }
}
```

### 3. Update appsettings.Production.json

```json
{
  "AWS": {
    "Region": "us-east-1",
    "BucketName": "aspec-capture-prod",
    "S3Paths": {
      "Cargas": "cargas",
      "Capturas": "capturas"
    }
  },
  "Api": {
    "ApiKey": "${API_KEY_SECRET}"
  }
}
```

### 4. Add Middleware using statement to Program.cs

```csharp
using pwa_camera_poc_api.Middleware;
```

---

## ✅ S3 Test Data Setup

Create sample JSON files in S3:

### File: `s3://aspec-capture/cargas/ug_1_users.json`

```json
[
  {
    "nomeUsuario": "admin",
    "primeiroNome": "Administrador",
    "ultimoNome": "do Sistema",
    "hashSenha": "$argon2id$v=19$m=65536,t=3,p=4$salt123456789$hash1234567890abcdef",
    "idsUnidadesGestoras": [1],
    "unidadeGestoraAtualId": 1,
    "dataCriacao": "2026-01-01T00:00:00Z"
  },
  {
    "nomeUsuario": "joao.silva",
    "primeiroNome": "João",
    "ultimoNome": "Silva",
    "hashSenha": "$argon2id$v=19$m=65536,t=3,p=4$salt234567890ab$hash234567890abcdef1",
    "idsUnidadesGestoras": [1, 2],
    "unidadeGestoraAtualId": 1,
    "dataCriacao": "2026-01-10T00:00:00Z"
  }
]
```

### File: `s3://aspec-capture/cargas/ug_1_itens.json`

```json
[
  {
    "id": "uuid-1",
    "nome": "Computador Dell XPS 13",
    "codigo": "INF-001",
    "category": "Informática",
    "localizacao": "Sala 101",
    "observacoes": "Monitorado - Serial XYZ123",
    "status": "ativo",
    "dataHora": "2026-01-15T10:00:00Z",
    "unidadeGestoraId": 1,
    "criadoPor": "admin",
    "remoteUrls": []
  },
  {
    "id": "uuid-2",
    "nome": "Impressora HP LaserJet Pro",
    "codigo": "IMP-001",
    "category": "Periféricos",
    "localizacao": "Recepção",
    "observacoes": "Toner preto 80%",
    "status": "ativo",
    "dataHora": "2026-01-15T11:00:00Z",
    "unidadeGestoraId": 1,
    "criadoPor": "admin",
    "remoteUrls": []
  }
]
```

---

## 🧪 Testing the API

### Test 1: Valid API Key
```bash
curl -X GET \
  http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: your-secure-api-key-here-change-in-production" \
  -H "Content-Type: application/json"
```

**Expected Response** (200 OK):
```json
{
  "presignedUrl": "https://aspec-capture.s3.us-east-1.amazonaws.com/cargas/ug_1_itens.json?X-Amz-Algorithm=...",
  "key": "cargas/ug_1_itens.json",
  "bucket": "aspec-capture",
  "generatedAt": "2026-02-09T14:30:00Z",
  "expiresAt": "2026-02-09T15:00:00Z",
  "contentType": "application/json"
}
```

### Test 2: Missing API Key
```bash
curl -X GET \
  http://localhost:5069/api/v2/inventario/carga/1 \
  -H "Content-Type: application/json"
```

**Expected Response** (401 Unauthorized):
```json
{
  "error": "Missing X-Api-Key header"
}
```

### Test 3: Invalid API Key
```bash
curl -X GET \
  http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: wrong-key" \
  -H "Content-Type: application/json"
```

**Expected Response** (401 Unauthorized):
```json
{
  "error": "Invalid X-Api-Key"
}
```

### Test 4: Get Users URL
```bash
curl -X GET \
  http://localhost:5069/api/v2/auth/usuarios/1 \
  -H "X-Api-Key: your-secure-api-key-here-change-in-production" \
  -H "Content-Type: application/json"
```

---

## 📊 Integration Verification

After implementation, verify:

1. **API Responds**
   - [ ] `GET /api/v2/inventario/carga/1` returns Pre-Signed URL
   - [ ] `GET /api/v2/auth/usuarios/1` returns Pre-Signed URL
   - [ ] X-Api-Key validation works

2. **Pre-Signed URLs Work**
   - [ ] URLs are valid for 30 minutes
   - [ ] URLs allow GET requests to S3
   - [ ] URLs expire correctly

3. **S3 Integration**
   - [ ] Test data files exist in S3
   - [ ] Files are readable via Pre-Signed URLs
   - [ ] JSON format matches expected schemas

4. **Frontend Integration**
   - [ ] PWA calls v2 endpoints after login
   - [ ] Data syncs to IndexedDB
   - [ ] Search/merge works with downloaded data
   - [ ] Offline auth works with downloaded users

---

## 🔐 Security Checklist

- [ ] API key stored in appsettings.Production.json (not in code)
- [ ] API key uses strong random value (32+ chars)
- [ ] Middleware validates all /api/v2/* routes
- [ ] Pre-Signed URLs expire after 30 minutes
- [ ] S3 bucket has restrictive permissions
- [ ] Logging includes failed auth attempts
- [ ] CORS configured appropriately for PWA domain

---

## 📝 Implementation Order

1. Add using statement for Middleware
2. Add middleware registration to Program.cs
3. Add v2 endpoint implementations to Program.cs
4. Update appsettings.json with API key
5. Create S3 test data files
6. Test API endpoints with curl
7. Test frontend integration
8. Update CHANGELOG.md

---

**Next Action**: Create v2 endpoints in Backend API Program.cs
