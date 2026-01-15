# pwa-camera-poc-api

API de suporte para o PWA de inventário patrimonial, responsável por gerar URLs pré-assinadas (pre-signed URLs) para upload de imagens diretamente no S3, evitando o uso direto do AWS SDK no Blazor WASM (que pode causar erros de runtime).

## Exemplo de uso

### Obter URL pré-assinada

Endpoint utilizado pelo Front-end (Blazor WASM) para solicitar uma URL pré-assinada para upload de arquivo no S3.

**Requisição:**

```bash
curl -X POST "http://localhost:5000/api/storage/presigned-url" \
     -H "Content-Type: application/json" \
     -d '{
           "fileName": "evidence_photo.jpg",
           "contentType": "image/jpeg",
           "assetId": "550e8400-e29b-41d4-a716-446655440000",
           "assetCode": "INV-2024-001"
         }'
```

**Resposta:**

```json
{
  "url": "https://your-bucket.s3.amazonaws.com/...",
  "key": "550e8400-e29b-41d4-a716-446655440000/evidence_photo.jpg"
}
```

Os campos `assetId` e `assetCode` correspondem respectivamente a `InventoryItem.Id` e `InventoryItem.Code` do modelo `InventoryItem` utilizado no aplicativo Blazor.