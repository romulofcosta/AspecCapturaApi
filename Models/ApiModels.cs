using System.Text.Json.Serialization;

namespace PwaCameraPocApi.Models;

// ─── DTOs e Records ───────────────────────────────────────────────────────────
public record PresignedUrlRequest(string FileName, string ContentType, string AssetId, string AssetCode);
public record PresignedUrlResponse(string Url, string Key);
public record LoginRequest(string Usuario, string Senha);

public record AuthResponse(
    string NomeCompleto,
    string Prefixo,
    string Esfera,
    List<OrgaoRecord> Orgaos,
    List<TombamentoItemResponse> Tombamentos,
    string Token
);

public record TombamentoItemResponse(long IdPatomb, string Nutomb, string Esfera, string Deprod);

public class TombamentoCargaItem
{
    [JsonPropertyName("idpatomb")]
    public long IdPatomb { get; set; }
    [JsonPropertyName("nutomb")]
    public string Nutomb { get; set; } = string.Empty;
    [JsonPropertyName("deprod")]
    public string Deprod { get; set; } = string.Empty;
    [JsonPropertyName("esfera")]
    public string Esfera { get; set; } = string.Empty;
    [JsonPropertyName("cdorgao")]
    public string CdOrgao { get; set; } = string.Empty;
    [JsonPropertyName("cdunid")]
    public string CdUnid { get; set; } = string.Empty;
    [JsonPropertyName("cdarea")]
    public string CdArea { get; set; } = string.Empty;
    [JsonPropertyName("cdsarea")]
    public string CdSArea { get; set; } = string.Empty;
}

// ─── Records para o JSON Unificado ───────────────────────────────────────────
// CORREÇÃO BUG #1: [JsonPropertyName] garante que a desserialização case-insensitive
// funcione corretamente com records posicionais em .NET

public record UnifiedDataRecord(
    [property: JsonPropertyName("cliente")] string? Cliente,
    [property: JsonPropertyName("usuarios")] List<UserRecord>? Usuarios,
    [property: JsonPropertyName("tabelas")] TabelasRecord? Tabelas
);

// CORREÇÃO BUG #2: NomeCompleto é nullable pois pode não existir no JSON
public record UserRecord(
    [property: JsonPropertyName("idusuario")] string IdUsuario,
    [property: JsonPropertyName("nmusuario")] string NmUsuario,
    [property: JsonPropertyName("pwdusuario")] string PwdUsuario,
    [property: JsonPropertyName("esfera")] string Esfera,
    [property: JsonPropertyName("nomecompleto")] string? NomeCompleto
);

public record TabelasRecord(
    [property: JsonPropertyName("xxorga")] List<XxOrgaRecord> XxOrga,
    [property: JsonPropertyName("xxunid")] List<XxUnidRecord> XxUnid,
    [property: JsonPropertyName("paarea")] List<PaAreaRecord> PaArea,
    [property: JsonPropertyName("pasarea")] List<PasAreaRecord> PasArea,
    [property: JsonPropertyName("localizacao")] List<LocalizacaoRecord> Localizacao,
    [property: JsonPropertyName("tombamentos")] List<TombamentoRecord> Tombamentos
);

public record XxOrgaRecord(
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("nmorgao")] string NmOrgao,
    [property: JsonPropertyName("dtestr")]  int DtEstr = 0   // Exercício fiscal (YYYYMMDD); 0 quando ausente
);

public record XxUnidRecord(
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("cdunid")] string CdUnid,
    [property: JsonPropertyName("nmunid")] string NmUnid
);

public record PaAreaRecord(
    [property: JsonPropertyName("cdarea")] string CdArea,
    [property: JsonPropertyName("nmarea")] string NmArea
);

public record PasAreaRecord(
    [property: JsonPropertyName("cdsarea")] string CdSArea,
    [property: JsonPropertyName("nmsarea")] string NmSArea
);

public record LocalizacaoRecord(
    [property: JsonPropertyName("idlocalizacao")] long IdLocalizacao,
    [property: JsonPropertyName("cdorgao")] string CdOrgao,
    [property: JsonPropertyName("cdunid")] string CdUnid,
    [property: JsonPropertyName("cdarea")] string CdArea,
    [property: JsonPropertyName("cdsarea")] string CdSArea,
    [property: JsonPropertyName("dtestr")] int DtEstr = 0
);

// Mutable class to support in-place updates during capture
public class TombamentoRecord
{
    [JsonPropertyName("idpatomb")] public long IdPatomb { get; set; }
    [JsonPropertyName("nutomb")] public string Nutomb { get; set; } = string.Empty;
    [JsonPropertyName("databomb")] public int? Databomb { get; set; }
    [JsonPropertyName("cdprod")] public int? Cdprod { get; set; }
    [JsonPropertyName("deprod")] public string Deprod { get; set; } = string.Empty;
    [JsonPropertyName("estado")] public string? Estado { get; set; }
    [JsonPropertyName("dataestado")] public int? Dataestado { get; set; }
    [JsonPropertyName("situacao")] public string? Situacao { get; set; }
    [JsonPropertyName("datasituacao")] public int? Datasituacao { get; set; }
    [JsonPropertyName("idlocalizacao")] public long? IdLocalizacao { get; set; }
    [JsonPropertyName("esfera")] public string Esfera { get; set; } = string.Empty;
    [JsonPropertyName("cdorgao")] public string CdOrgao { get; set; } = string.Empty;
    [JsonPropertyName("cdunid")] public string CdUnid { get; set; } = string.Empty;
    [JsonPropertyName("cdarea")] public string CdArea { get; set; } = string.Empty;
    [JsonPropertyName("cdsarea")] public string CdSArea { get; set; } = string.Empty;
    [JsonPropertyName("exerciciofiscal")] public int ExercicioFiscal { get; set; } = 0;  // Ano do exercício fiscal (YYYY); 0 quando ausente
    // Capture metadata (written on capture, not present in original data)
    [JsonPropertyName("fotoKey")] public string? FotoKey { get; set; }
    [JsonPropertyName("capturedBy")] public string? CapturedBy { get; set; }
    [JsonPropertyName("capturedAt")] public string? CapturedAt { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
}

// ─── Capture Endpoint Models ──────────────────────────────────────────────────
public record CaptureItemRequest(
    [property: JsonPropertyName("prefixo")] string Prefixo,
    [property: JsonPropertyName("idpatomb")] long IdPatomb,
    [property: JsonPropertyName("nutomb")] string Nutomb,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("situacao")] string? Situacao,
    [property: JsonPropertyName("idlocalizacao")] long? IdLocalizacao,
    [property: JsonPropertyName("fotoKey")] string? FotoKey,
    [property: JsonPropertyName("capturedBy")] string? CapturedBy,
    [property: JsonPropertyName("capturedAt")] string? CapturedAt,
    [property: JsonPropertyName("source")] string? Source
);

public record CaptureItemResponse(
    [property: JsonPropertyName("idpatomb")] long IdPatomb,
    [property: JsonPropertyName("nutomb")] string Nutomb,
    [property: JsonPropertyName("status")] string Status,   // "updated" | "created"
    [property: JsonPropertyName("updatedAt")] string UpdatedAt
);

public record ValidateTombamentoResponse(
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("idpatomb")] long? IdPatomb,
    [property: JsonPropertyName("nutomb")] string? Nutomb,
    [property: JsonPropertyName("esfera")] string? Esfera,
    [property: JsonPropertyName("deprod")] string? Deprod,
    [property: JsonPropertyName("cdprod")] int? Cdprod,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("situacao")] string? Situacao,
    [property: JsonPropertyName("idlocalizacao")] long? IdLocalizacao
);

public record CapturedItemSummary(
    [property: JsonPropertyName("idpatomb")] long IdPatomb,
    [property: JsonPropertyName("nutomb")] string Nutomb,
    [property: JsonPropertyName("deprod")] string Deprod,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("situacao")] string? Situacao,
    [property: JsonPropertyName("esfera")] string Esfera
);

public record SyncBatchRequest(
    [property: JsonPropertyName("prefixo")] string Prefixo,
    [property: JsonPropertyName("items")] List<CaptureItemRequest> Items
);

public record SyncItemResult(
    [property: JsonPropertyName("idpatomb")] long IdPatomb,
    [property: JsonPropertyName("nutomb")] string Nutomb,
    [property: JsonPropertyName("status")] string Status   // "updated" | "created" | "failed"
);

public record SyncBatchResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("updated")] int Updated,
    [property: JsonPropertyName("created")] int Created,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("results")] List<SyncItemResult> Results
);

// ─── Records de resposta hierárquica ─────────────────────────────────────────
public record OrgaoRecord(string IdOrgao, string NomeOrgao, List<UnidadeOrcamentariaRecord> UnidadesOrcamentarias, int DtEstr = 0);
public record UnidadeOrcamentariaRecord(string IdUO, string NomeUO, List<AreaRecord> Areas);
public record AreaRecord(string IdArea, string NomeArea, List<SubareaRecord> Subareas);
public record SubareaRecord(string IdSubarea, string NomeSubarea);

record ChunkMeta(int Start, int Count, string Hash);
record ChunkIndex(string Version, int TotalRecords, string GlobalHash, List<ChunkMeta> Chunks, System.Text.Json.JsonSerializerOptions Options);
