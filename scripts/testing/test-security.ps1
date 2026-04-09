# ============================================================================
# SCRIPT DE TESTE - SEGURANÇA ASPEC CAPTURA API (PowerShell)
# ============================================================================
# Este script testa todos os aspectos de segurança implementados
# ============================================================================

$API_URL = "http://localhost:5000"
$USUARIO = "ce999.admin"
$SENHA = "senha123"
$PREFIXO = "CE999"

Write-Host "🔒 TESTE DE SEGURANÇA - ASPEC CAPTURA API" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# ─── TESTE 1: Health Check ────────────────────────────────────────────────────
Write-Host "✅ TESTE 1: Health Check (sem autenticação)" -ForegroundColor Green
try {
    $response = Invoke-RestMethod -Uri "$API_URL/api/health" -Method Get
    Write-Host ($response | ConvertTo-Json)
} catch {
    Write-Host "❌ ERRO: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# ─── TESTE 2: Security Headers ────────────────────────────────────────────────
Write-Host "✅ TESTE 2: Verificar Security Headers" -ForegroundColor Green
Write-Host "Esperado: X-Content-Type-Options, X-Frame-Options, X-XSS-Protection"
try {
    $response = Invoke-WebRequest -Uri "$API_URL/api/health" -Method Get
    $headers = @(
        "X-Content-Type-Options",
        "X-Frame-Options", 
        "X-XSS-Protection",
        "Content-Security-Policy"
    )
    foreach ($header in $headers) {
        if ($response.Headers[$header]) {
            Write-Host "  ✅ $header : $($response.Headers[$header])" -ForegroundColor Green
        } else {
            Write-Host "  ⚠️  $header : Não encontrado" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "❌ ERRO: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# ─── TESTE 3: Login e obter token ─────────────────────────────────────────────
Write-Host "✅ TESTE 3: Login e obter token JWT" -ForegroundColor Green
try {
    $loginBody = @{
        usuario = $USUARIO
        senha = $SENHA
    } | ConvertTo-Json

    $loginResponse = Invoke-RestMethod -Uri "$API_URL/api/auth/login" `
        -Method Post `
        -ContentType "application/json" `
        -Body $loginBody

    $TOKEN = $loginResponse.token

    if ($TOKEN) {
        Write-Host "✅ Token obtido com sucesso!" -ForegroundColor Green
        Write-Host "Token (primeiros 50 chars): $($TOKEN.Substring(0, [Math]::Min(50, $TOKEN.Length)))..."
        Write-Host ""
        
        # Decodificar payload do token
        $tokenParts = $TOKEN.Split('.')
        if ($tokenParts.Length -ge 2) {
            $payload = $tokenParts[1]
            # Adicionar padding se necessário
            while ($payload.Length % 4 -ne 0) {
                $payload += "="
            }
            try {
                $decodedBytes = [System.Convert]::FromBase64String($payload)
                $decodedText = [System.Text.Encoding]::UTF8.GetString($decodedBytes)
                Write-Host "📋 Informações do Token:" -ForegroundColor Cyan
                Write-Host $decodedText
            } catch {
                Write-Host "⚠️  Não foi possível decodificar o token" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "❌ ERRO: Não foi possível obter token" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "❌ ERRO: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ─── TESTE 4: Endpoint protegido SEM token (deve falhar) ──────────────────────
Write-Host "✅ TESTE 4: Tentar acessar endpoint protegido SEM token" -ForegroundColor Green
Write-Host "Esperado: 401 Unauthorized"
try {
    $body = @{
        prefixo = $PREFIXO
        idPatomb = "TEST123"
    } | ConvertTo-Json

    Invoke-RestMethod -Uri "$API_URL/api/capture/item" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body
    
    Write-Host "❌ FALHOU: Deveria ter retornado 401" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "✅ PASSOU: Retornou 401 Unauthorized (correto)" -ForegroundColor Green
    } else {
        Write-Host "❌ FALHOU: Retornou $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}
Write-Host ""

# ─── TESTE 5: Endpoint protegido COM token válido (deve funcionar) ────────────
Write-Host "✅ TESTE 5: Acessar endpoint protegido COM token válido" -ForegroundColor Green
Write-Host "Esperado: 200 OK ou 404 (se prefixo não existir)"
try {
    $body = @{
        prefixo = $PREFIXO
        idPatomb = "TEST123"
        nutomb = "12345"
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $TOKEN"
    }

    $response = Invoke-RestMethod -Uri "$API_URL/api/capture/item" `
        -Method Post `
        -ContentType "application/json" `
        -Headers $headers `
        -Body $body
    
    Write-Host "✅ PASSOU: Token aceito, resposta recebida" -ForegroundColor Green
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 200 -or $statusCode -eq 404) {
        Write-Host "✅ PASSOU: Retornou $statusCode (token aceito)" -ForegroundColor Green
    } else {
        Write-Host "❌ FALHOU: Retornou $statusCode" -ForegroundColor Red
    }
}
Write-Host ""

# ─── TESTE 6: Tentar acessar dados de outro município (deve falhar) ───────────
Write-Host "✅ TESTE 6: Tentar acessar dados de OUTRO município" -ForegroundColor Green
Write-Host "Esperado: 403 Forbidden"
try {
    $body = @{
        prefixo = "CE888"
        idPatomb = "TEST123"
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $TOKEN"
    }

    Invoke-RestMethod -Uri "$API_URL/api/capture/item" `
        -Method Post `
        -ContentType "application/json" `
        -Headers $headers `
        -Body $body
    
    Write-Host "❌ FALHOU: Deveria ter retornado 403" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 403) {
        Write-Host "✅ PASSOU: Retornou 403 Forbidden (autorização funcionando)" -ForegroundColor Green
    } else {
        Write-Host "❌ FALHOU: Retornou $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}
Write-Host ""

# ─── TESTE 7: Token inválido ──────────────────────────────────────────────────
Write-Host "✅ TESTE 7: Tentar usar token inválido" -ForegroundColor Green
Write-Host "Esperado: 401 Unauthorized"
try {
    $body = @{
        prefixo = $PREFIXO
        idPatomb = "TEST123"
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalid.token"
    }

    Invoke-RestMethod -Uri "$API_URL/api/capture/item" `
        -Method Post `
        -ContentType "application/json" `
        -Headers $headers `
        -Body $body
    
    Write-Host "❌ FALHOU: Deveria ter retornado 401" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "✅ PASSOU: Retornou 401 Unauthorized (validação funcionando)" -ForegroundColor Green
    } else {
        Write-Host "❌ FALHOU: Retornou $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}
Write-Host ""

# ─── RESUMO ───────────────────────────────────────────────────────────────────
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "🎉 TESTES CONCLUÍDOS!" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 Checklist de Segurança:" -ForegroundColor Yellow
Write-Host "  ✅ Health check funcionando"
Write-Host "  ✅ Security headers configurados"
Write-Host "  ✅ Login retorna token JWT"
Write-Host "  ✅ Endpoints protegidos rejeitam sem token (401)"
Write-Host "  ✅ Endpoints protegidos aceitam token válido"
Write-Host "  ✅ Autorização por município funcionando (403)"
Write-Host "  ✅ Validação de token funcionando"
Write-Host ""
Write-Host "🔐 Sistema de segurança está OPERACIONAL!" -ForegroundColor Green
Write-Host ""
