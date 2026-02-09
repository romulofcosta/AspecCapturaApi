# API v2 Testing Guide

**Date**: February 9, 2026  
**Purpose**: Test v2 endpoints and integration after Phase 3 implementation

---

## 🧪 Quick Start Tests

### Test 1: Verify API Server Running
```bash
curl -X GET http://localhost:5069/ \
  -H "Content-Type: application/json"
```

**Expected**: Redirect to Swagger UI (status 302 or 200)

---

### Test 2: Get Inventory Pre-Signed URL (Missing API Key)
```bash
curl -X GET http://localhost:5069/api/v2/inventario/carga/1 \
  -H "Content-Type: application/json"
```

**Expected Response** (401 Unauthorized):
```json
{
  "error": "Missing X-Api-Key header"
}
```

---

### Test 3: Get Inventory Pre-Signed URL (Invalid API Key)
```bash
curl -X GET http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: wrong-key" \
  -H "Content-Type: application/json"
```

**Expected Response** (401 Unauthorized):
```json
{
  "error": "Invalid X-Api-Key"
}
```

---

### Test 4: Get Inventory Pre-Signed URL (Valid API Key) ✅
```bash
curl -X GET http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" \
  -H "Content-Type: application/json"
```

**Expected Response** (200 OK):
```json
{
  "presignedUrl": "https://aspec-capture.s3.us-east-2.amazonaws.com/cargas/ug_1_itens.json?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...",
  "key": "cargas/ug_1_itens.json",
  "bucket": "aspec-capture",
  "generatedAt": "2026-02-09T14:30:00Z",
  "expiresAt": "2026-02-09T15:00:00Z",
  "contentType": "application/json"
}
```

---

### Test 5: Get Users Pre-Signed URL (Valid API Key) ✅
```bash
curl -X GET http://localhost:5069/api/v2/auth/usuarios/1 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" \
  -H "Content-Type: application/json"
```

**Expected Response** (200 OK):
```json
{
  "presignedUrl": "https://aspec-capture.s3.us-east-2.amazonaws.com/cargas/ug_1_users.json?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...",
  "key": "cargas/ug_1_users.json",
  "bucket": "aspec-capture",
  "generatedAt": "2026-02-09T14:30:00Z",
  "expiresAt": "2026-02-09T15:00:00Z",
  "contentType": "application/json"
}
```

---

## 📥 Download from Pre-Signed URL

### Test 6: Download Inventory Data from S3
```bash
# First, get the presignedUrl from Test 4 response
PRESIGNED_URL="<paste-url-from-response>"

# Download the inventory JSON
curl -X GET "$PRESIGNED_URL" \
  --output inventory.json

# Verify JSON is valid
jq . inventory.json | head -20
```

**Expected**: Valid JSON array with 10+ item objects

---

### Test 7: Download Users Data from S3
```bash
# First, get the presignedUrl from Test 5 response
PRESIGNED_URL="<paste-url-from-response>"

# Download the users JSON
curl -X GET "$PRESIGNED_URL" \
  --output users.json

# Verify JSON is valid
jq . users.json | head -20
```

**Expected**: Valid JSON array with 3 user objects

---

## 🔄 Full Integration Test

### Test 8: Full Provisioning Flow (Simulated PWA)
```bash
#!/bin/bash
# Save as test_full_flow.sh

API_KEY="aspec-pwa-v2-dev-key-2026"
API_URL="http://localhost:5069"
UG_ID=1

echo "=== Step 1: Get Users Pre-Signed URL ==="
USERS_RESPONSE=$(curl -s -X GET "$API_URL/api/v2/auth/usuarios/$UG_ID" \
  -H "X-Api-Key: $API_KEY" \
  -H "Content-Type: application/json")

USERS_URL=$(echo $USERS_RESPONSE | jq -r '.presignedUrl')
echo "Users URL: $USERS_URL"

echo ""
echo "=== Step 2: Get Inventory Pre-Signed URL ==="
INVENTORY_RESPONSE=$(curl -s -X GET "$API_URL/api/v2/inventario/carga/$UG_ID" \
  -H "X-Api-Key: $API_KEY" \
  -H "Content-Type: application/json")

INVENTORY_URL=$(echo $INVENTORY_RESPONSE | jq -r '.presignedUrl')
echo "Inventory URL: $INVENTORY_URL"

echo ""
echo "=== Step 3: Download Users JSON ==="
curl -s -X GET "$USERS_URL" --output users.json
USERS_COUNT=$(jq 'length' users.json)
echo "Downloaded $USERS_COUNT users"
jq '.[] | {nomeUsuario, primeiroNome}' users.json

echo ""
echo "=== Step 4: Download Inventory JSON ==="
curl -s -X GET "$INVENTORY_URL" --output inventory.json
ITEMS_COUNT=$(jq 'length' inventory.json)
echo "Downloaded $ITEMS_COUNT items"
jq '.[] | {codigo, nome, category}' inventory.json | head -20

echo ""
echo "✅ Full provisioning flow completed successfully!"
```

**Run the script**:
```bash
chmod +x test_full_flow.sh
./test_full_flow.sh
```

---

## 🧬 Advanced Tests

### Test 9: Check API Response Headers
```bash
curl -I -X GET http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026"
```

**Expected Headers**:
```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Content-Length: 1234
Date: Sun, 09 Feb 2026 14:30:00 GMT
```

---

### Test 10: Test Different UG IDs
```bash
# Test UG 1
curl -X GET http://localhost:5069/api/v2/inventario/carga/1 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" | jq '.key'

# Test UG 2
curl -X GET http://localhost:5069/api/v2/inventario/carga/2 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" | jq '.key'

# Note: UG 2 file doesn't exist in S3, but endpoint still returns Pre-Signed URL
# (S3 will return 404 when trying to download)
```

---

## 🔍 Debugging

### Check API Logs
```bash
# If running dotnet with logging:
# Look for "[v2 API]" entries in console output

# Example log:
# [v2 API] Generating Pre-Signed URL for inventory carga: cargas/ug_1_itens.json
# [v2 API] Generating Pre-Signed URL for users: cargas/ug_1_users.json
```

### Validate JSON Schema
```bash
# Check if users JSON matches expected schema
jq 'map(keys)' users.json | head -1

# Expected keys: ["dataCriacao", "hashSenha", "idsUnidadesGestoras", ...]

# Check if inventory JSON matches expected schema
jq 'map(keys)' inventory.json | head -1

# Expected keys: ["categoria", "codigo", "criadoPor", "dataHora", ...]
```

### Test Pre-Signed URL Expiry
```bash
# Get a Pre-Signed URL and note the timestamp
PRESIGNED_URL="<url-from-response>"

# Wait 30+ minutes (or manually test expiry by modifying system time)
# Try to download again - should fail with 403 Forbidden

curl -X GET "$PRESIGNED_URL"
# Expected error: Access Denied
```

---

## ✅ Integration Verification Checklist

- [ ] API v2 endpoints return 200 OK for valid API key
- [ ] API v2 endpoints return 401 for missing API key
- [ ] API v2 endpoints return 401 for invalid API key
- [ ] Pre-Signed URLs are generated correctly
- [ ] Pre-Signed URLs point to correct S3 paths
- [ ] Pre-Signed URLs expire in 30 minutes
- [ ] Users JSON downloads successfully
- [ ] Inventory JSON downloads successfully
- [ ] JSON schemas match expected DTOs
- [ ] API logging shows all requests

---

## 🌐 Browser Testing (Manual)

### Test in Browser Console
```javascript
// Open browser console (F12) on any page

// Test 1: Fetch users
fetch('http://localhost:5069/api/v2/auth/usuarios/1', {
  method: 'GET',
  headers: {
    'X-Api-Key': 'aspec-pwa-v2-dev-key-2026',
    'Content-Type': 'application/json'
  }
})
.then(r => r.json())
.then(data => console.log(data))

// Test 2: Fetch inventory
fetch('http://localhost:5069/api/v2/inventario/carga/1', {
  method: 'GET',
  headers: {
    'X-Api-Key': 'aspec-pwa-v2-dev-key-2026',
    'Content-Type': 'application/json'
  }
})
.then(r => r.json())
.then(data => console.log(data))
```

---

## 📊 Performance Testing

### Test Load with Multiple Requests
```bash
# Simulate 10 concurrent requests
for i in {1..10}; do
  curl -s -X GET http://localhost:5069/api/v2/inventario/carga/1 \
    -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" &
done
wait

echo "✅ All 10 requests completed"
```

---

## 🚨 Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| 401 Unauthorized | Missing/invalid API key | Check appsettings.json Api:ApiKey value |
| 500 Server Error | AWS credentials invalid | Verify AWS:AccessKey and AWS:SecretKey |
| 500 Server Error | Bucket not configured | Check AWS:BucketName setting |
| Cannot download from Pre-Signed URL | URL expired (>30 min) | Generate new Pre-Signed URL |
| Cannot download from Pre-Signed URL | S3 file doesn't exist | Upload test data to S3 first |
| CORS error in browser | Frontend calling wrong domain | Check CORS policy in Program.cs |

---

**Status**: Phase 3a API Implementation Complete ✅  
**Next**: Execute tests and verify all endpoints working
