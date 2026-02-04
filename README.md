# ASPEC Capture API (BFF)

API Backend (Minimal API .NET 10) projetada para dar suporte ao PWA de Inventário Patrimonial. Sua função principal é intermediar operações sensíveis ou incompatíveis com o ambiente WebAssembly (Blazor WASM), especificamente a geração de URLs assinadas para upload seguro no Amazon S3.

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
- .NET 10 SDK instalado.
- Acesso a uma conta AWS com permissões de S3 (`PutObject`).

### Configuração
Configure as credenciais no arquivo `appsettings.json` ou via Variáveis de Ambiente.

**Exemplo de Configuração:**
```json
{
  "AWS": {
    "Region": "us-east-1",
    "BucketName": "seu-bucket-aqui",
    "AccessKey": "SUA_ACCESS_KEY",
    "SecretKey": "SUA_SECRET_KEY"
  }
}
```

### Execução
```bash
dotnet run
```
Acesse Swagger UI em: `http://localhost:5069/swagger`

## 🔌 Documentação da API

### `POST /api/storage/presigned-url`
Gera uma URL temporária para upload de um arquivo.
- **Campos**: `fileName`, `contentType`, `assetId`, `assetCode`.

### `GET /api/storage/exists/{*filePath}`
Verifica se um objeto existe no S3.

## 🛠️ Desenvolvimento
### Dependências
- `AWSSDK.S3`, `AWSSDK.Extensions.NETCore.Setup`, `Swashbuckle.AspNetCore`.

---
© 2026 ASPEC. Todos os direitos reservados.