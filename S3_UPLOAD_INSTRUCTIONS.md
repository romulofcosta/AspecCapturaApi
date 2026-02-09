# S3 Test Data Upload Instructions

**Date**: February 9, 2026  
**Purpose**: Upload provisioning test data to S3 for Phase 3 testing

---

## 📋 Files to Upload

Two JSON files in this directory need to be uploaded to S3:
- `S3_TEST_DATA_ug_1_users.json` → `s3://aspec-capture/cargas/ug_1_users.json`
- `S3_TEST_DATA_ug_1_itens.json` → `s3://aspec-capture/cargas/ug_1_itens.json`

---

## 🔧 Option 1: Using AWS CLI

### Install AWS CLI (if needed)
```bash
# Windows
choco install awscli

# macOS
brew install awscli

# Linux
sudo apt-get install awscli
```

### Configure AWS Credentials
```bash
aws configure
# Enter your AWS Access Key ID: YOUR_AWS_ACCESS_KEY_ID
# Enter your AWS Secret Access Key: YOUR_AWS_SECRET_ACCESS_KEY
# Default region: us-east-2
# Default output format: json
```

### Upload Files
```bash
# Upload users JSON
aws s3 cp S3_TEST_DATA_ug_1_users.json s3://aspec-capture/cargas/ug_1_users.json

# Upload items JSON
aws s3 cp S3_TEST_DATA_ug_1_itens.json s3://aspec-capture/cargas/ug_1_itens.json

# Verify upload
aws s3 ls s3://aspec-capture/cargas/
```

---

## 🔧 Option 2: Using AWS S3 Console (Web)

1. Go to https://s3.console.aws.amazon.com
2. Login with AWS credentials
3. Click on bucket: `aspec-capture`
4. Create folder: `cargas` (if not exists)
5. Upload both JSON files to `cargas/` folder

---

## 🔧 Option 3: Using S3 Upload Script

### PowerShell Script
```powershell
# Set AWS credentials
$env:AWS_ACCESS_KEY_ID = "YOUR_AWS_ACCESS_KEY_ID"
$env:AWS_SECRET_ACCESS_KEY = "YOUR_AWS_SECRET_ACCESS_KEY"
$env:AWS_DEFAULT_REGION = "us-east-2"

# Upload files
aws s3 cp S3_TEST_DATA_ug_1_users.json s3://aspec-capture/cargas/ug_1_users.json
aws s3 cp S3_TEST_DATA_ug_1_itens.json s3://aspec-capture/cargas/ug_1_itens.json

# Verify
aws s3 ls s3://aspec-capture/cargas/
```

---

## ✅ Verification

### Verify Files Are in S3
```bash
aws s3 ls s3://aspec-capture/cargas/
```

**Expected Output:**
```
2026-02-09 14:30:00     1234 ug_1_users.json
2026-02-09 14:31:00     5678 ug_1_itens.json
```

### Test Pre-Signed URL Generation
```bash
# Test users endpoint
curl -X GET http://localhost:5069/api/v2/auth/usuarios/1 \
  -H "X-Api-Key: aspec-pwa-v2-dev-key-2026" \
  -H "Content-Type: application/json"

# Should return Pre-Signed URL in response
```

### Download from Pre-Signed URL
```bash
# Get the presignedUrl from response above, then:
curl -X GET "https://aspec-capture.s3.us-east-2.amazonaws.com/cargas/ug_1_users.json?X-Amz-Algorithm=AWS4-HMAC-SHA256&..."

# Should return the JSON content
```

---

## 📊 Test Data Contents

### Users (3 test users)
- `admin` - Administrador do Sistema (all access)
- `joao.silva` - João Silva (UG 1 & 2)
- `maria.santos` - Maria Santos (UG 1)

All have same test password hash for easier testing (replace in production!)

### Items (10 test items)
- Computers (INF-001)
- Printers (IMP-001)
- Webcams (CAM-001)
- Peripherals (PER-001, PER-002, MON-001)
- Infrastructure (INF-100)
- Furniture (MÓV-001, MÓV-002, MÓV-003)

---

## 🚀 Testing Workflow

After uploading test data:

1. Start backend API server (localhost:5069)
2. Call v2 endpoint to get Pre-Signed URL
3. Download data from S3
4. Verify JSON structure matches schemas
5. Test frontend provisioning (Login.razor)
6. Verify data syncs to IndexedDB
7. Test search and merge logic

---

## ⚠️ Production Notes

- Replace API key in appsettings.Production.json with strong random value
- Use IAM role credentials instead of Access Key/Secret Key
- Set S3 object ACLs to private (restrict public access)
- Enable versioning on S3 bucket
- Enable server-side encryption (AES-256 or KMS)
- Enable access logging for audit trail
- Rotate credentials regularly

---

**Status**: Ready for S3 Upload  
**Estimated Time**: 5 minutes  
**Next Step**: Run testing workflow after upload
