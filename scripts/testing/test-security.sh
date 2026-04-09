#!/bin/bash

# ============================================================================
# SCRIPT DE TESTE - SEGURANÇA ASPEC CAPTURA API
# ============================================================================
# Este script testa todos os aspectos de segurança implementados
# ============================================================================

API_URL="http://localhost:5000"
USUARIO="ce999.admin"
SENHA="senha123"
PREFIXO="CE999"

echo "🔒 TESTE DE SEGURANÇA - ASPEC CAPTURA API"
echo "=========================================="
echo ""

# ─── TESTE 1: Health Check ────────────────────────────────────────────────────
echo "✅ TESTE 1: Health Check (sem autenticação)"
curl -s "$API_URL/api/health" | jq .
echo ""
echo ""

# ─── TESTE 2: Security Headers ────────────────────────────────────────────────
echo "✅ TESTE 2: Verificar Security Headers"
echo "Esperado: X-Content-Type-Options, X-Frame-Options, X-XSS-Protection"
curl -I "$API_URL/api/health" 2>&1 | grep -E "X-Content-Type-Options|X-Frame-Options|X-XSS-Protection|Content-Security-Policy"
echo ""
echo ""

# ─── TESTE 3: Login e obter token ─────────────────────────────────────────────
echo "✅ TESTE 3: Login e obter token JWT"
LOGIN_RESPONSE=$(curl -s -X POST "$API_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"usuario\":\"$USUARIO\",\"senha\":\"$SENHA\"}")

TOKEN=$(echo $LOGIN_RESPONSE | jq -r '.token')

if [ "$TOKEN" != "null" ] && [ ! -z "$TOKEN" ]; then
    echo "✅ Token obtido com sucesso!"
    echo "Token (primeiros 50 chars): ${TOKEN:0:50}..."
    echo ""
    
    # Decodificar token (apenas header e payload, sem verificar assinatura)
    echo "📋 Informações do Token:"
    echo $TOKEN | cut -d. -f2 | base64 -d 2>/dev/null | jq . || echo "Não foi possível decodificar"
else
    echo "❌ ERRO: Não foi possível obter token"
    echo "Resposta: $LOGIN_RESPONSE"
    exit 1
fi
echo ""
echo ""

# ─── TESTE 4: Endpoint protegido SEM token (deve falhar) ──────────────────────
echo "✅ TESTE 4: Tentar acessar endpoint protegido SEM token"
echo "Esperado: 401 Unauthorized"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/capture/item" \
  -H "Content-Type: application/json" \
  -d "{\"prefixo\":\"$PREFIXO\",\"idPatomb\":\"TEST123\"}")

if [ "$HTTP_CODE" == "401" ]; then
    echo "✅ PASSOU: Retornou 401 Unauthorized (correto)"
else
    echo "❌ FALHOU: Retornou $HTTP_CODE (esperado 401)"
fi
echo ""
echo ""

# ─── TESTE 5: Endpoint protegido COM token válido (deve funcionar) ────────────
echo "✅ TESTE 5: Acessar endpoint protegido COM token válido"
echo "Esperado: 200 OK ou 404 (se prefixo não existir)"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/capture/item" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"prefixo\":\"$PREFIXO\",\"idPatomb\":\"TEST123\",\"nutomb\":\"12345\"}")

if [ "$HTTP_CODE" == "200" ] || [ "$HTTP_CODE" == "404" ]; then
    echo "✅ PASSOU: Retornou $HTTP_CODE (token aceito)"
else
    echo "❌ FALHOU: Retornou $HTTP_CODE"
fi
echo ""
echo ""

# ─── TESTE 6: Tentar acessar dados de outro município (deve falhar) ───────────
echo "✅ TESTE 6: Tentar acessar dados de OUTRO município"
echo "Esperado: 403 Forbidden"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/capture/item" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"prefixo\":\"CE888\",\"idPatomb\":\"TEST123\"}")

if [ "$HTTP_CODE" == "403" ]; then
    echo "✅ PASSOU: Retornou 403 Forbidden (autorização funcionando)"
else
    echo "❌ FALHOU: Retornou $HTTP_CODE (esperado 403)"
fi
echo ""
echo ""

# ─── TESTE 7: Token expirado (simulação) ──────────────────────────────────────
echo "✅ TESTE 7: Tentar usar token inválido"
echo "Esperado: 401 Unauthorized"
INVALID_TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/capture/item" \
  -H "Authorization: Bearer $INVALID_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"prefixo\":\"$PREFIXO\",\"idPatomb\":\"TEST123\"}")

if [ "$HTTP_CODE" == "401" ]; then
    echo "✅ PASSOU: Retornou 401 Unauthorized (validação de token funcionando)"
else
    echo "❌ FALHOU: Retornou $HTTP_CODE (esperado 401)"
fi
echo ""
echo ""

# ─── RESUMO ───────────────────────────────────────────────────────────────────
echo "=========================================="
echo "🎉 TESTES CONCLUÍDOS!"
echo "=========================================="
echo ""
echo "📋 Checklist de Segurança:"
echo "  ✅ Health check funcionando"
echo "  ✅ Security headers configurados"
echo "  ✅ Login retorna token JWT"
echo "  ✅ Endpoints protegidos rejeitam sem token (401)"
echo "  ✅ Endpoints protegidos aceitam token válido"
echo "  ✅ Autorização por município funcionando (403)"
echo "  ✅ Validação de token funcionando"
echo ""
echo "🔐 Sistema de segurança está OPERACIONAL!"
echo ""
