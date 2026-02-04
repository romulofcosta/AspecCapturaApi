# Changelog - ASPEC Capture API (BFF)

Todas as mudanças notáveis na API Backend for Frontend serão documentadas neste arquivo.

## [1.0.0] - 2026-01-16

### Adicionado
- **Pre-signed URL Generator**: Endpoint para geração segura de URLs de upload (PUT) diretamente para o S3.
- **S3 Object Exists**: Endpoint para verificação rápida de existência de arquivos no bucket.
- **Sanitização de Arquivos**: Lógica para sanitizar nomes de arquivos e chaves S3 (remoção de acentos e caracteres especiais).
- **Auto-Configuração CORS**: A API agora tenta configurar automaticamente a política CORS do bucket S3 alvo na inicialização.
- **Suporte a Múltiplas Fotos**: Ajuste nos endpoints para lidar com pastas por item de inventário.
- **Isolamento por Ativo**: Suporte ao parâmetro `AssetCode` nos metadados da imagem no S3.

### Segurança
- Credenciais AWS (AccessKey/SecretKey) removidas do cliente PWA e centralizadas no servidor.
- Validação de expiração de URLs (10 minutos).

## [0.1.0] - 2026-01-09
### Inicial
- Implementação básica de proxy S3 para resolver `PlatformNotSupportedException` no Blazor WASM.