# Documentação do Backend - PWA Camera PoC API

Este diretório contém a documentação técnica e guias de referência para a API de suporte ao PWA de captura de ativos.

## Conteúdo

- [**Arquitetura**](ARCHITECTURE.md): Detalhes sobre o funcionamento do Broker de Autenticação e integração S3.
- [**Changelog**](CHANGELOG.md): Registro histórico de alterações e novas implementações.
- [**Modelo de Lista de Usuários**](USER_LIST_EXAMPLE.json): Exemplo de formato JSON para ser utilizado no S3.

## Configuração Rápida

Para rodar a API localmente, certifique-se de configurar as seguintes variáveis no `appsettings.json` ou variáveis de ambiente:

```json
{
  "AWS": {
    "AccessKey": "SUA_CHAVE",
    "SecretKey": "SEU_SECRET",
    "BucketName": "NOME_DO_BUCKET",
    "Region": "us-east-1"
  }
}
```

## Endpoints Principais

- `POST /api/auth/login`: Autenticação via Broker S3.
- `POST /api/storage/presigned-url`: Geração de URLs para upload direto ao S3.
- `GET /api/storage/exists/{path}`: Verificação de existência de arquivos no S3.

## Atualizações Recentes

- Respostas de login enviadas via streaming, reduzindo uso de memória em grandes volumes.
- Compressão de resposta habilitada para HTTPS.
- Desserialização mais tolerante (vírgulas finais e comentários no JSON).
- Auto-configuração de CORS do bucket S3 no startup.
