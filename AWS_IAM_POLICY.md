# Política IAM Necessária para AspecCaptura API

## Erro Identificado

```
Acesso negado para servicecatalog:ListApplications
Usuário: arn:aws:iam::703671917681:user/aspec-capture
```

## Política IAM Mínima para S3

O usuário `aspec-capture` precisa das seguintes permissões no bucket `aspec-capture`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AspecCaptureS3Access",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:GetObjectMetadata",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::aspec-capture",
        "arn:aws:s3:::aspec-capture/*"
      ]
    }
  ]
}
```

## Como Aplicar

### Opção 1: Via Console AWS (IAM)

1. Acesse o console AWS IAM
2. Navegue até **Users** > `aspec-capture`
3. Clique em **Add permissions** > **Attach policies directly**
4. Clique em **Create policy**
5. Cole o JSON acima
6. Nomeie como `AspecCaptureS3ReadOnly`
7. Anexe ao usuário

### Opção 2: Via AWS CLI

```bash
# Salve a política em um arquivo policy.json
aws iam put-user-policy \
  --user-name aspec-capture \
  --policy-name AspecCaptureS3Access \
  --policy-document file://policy.json
```

## Permissões Detalhadas

| Ação | Necessária Para | Usado Em |
|------|----------------|----------|
| `s3:GetObject` | Ler arquivos do bucket | Login, Sync, Lotes |
| `s3:GetObjectMetadata` | Verificar existência e versão | Cache, Sync-info |
| `s3:ListBucket` | Listar objetos (fallback) | GetS3ObjectWithFallback |

## Verificação

Após aplicar a política, teste com:

```bash
aws s3 ls s3://aspec-capture/usuarios/ --profile aspec-capture
```

## Nota sobre Service Catalog

O erro `servicecatalog:ListApplications` é do console AWS, não da API. 
Isso não afeta o funcionamento da aplicação, apenas a navegação no console.

Para remover este erro do console, adicione:

```json
{
  "Effect": "Allow",
  "Action": "servicecatalog:ListApplications",
  "Resource": "*"
}
```

Mas isso **não é necessário** para o funcionamento da API.
