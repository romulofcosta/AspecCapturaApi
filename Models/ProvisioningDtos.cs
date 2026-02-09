using System;
using System.Collections.Generic;

namespace pwa_camera_poc_api.Models
{
    /// <summary>
    /// DTO for user provisioning from S3 to PWA clients.
    /// Used in GET /api/v2/auth/usuarios/{ugId} endpoint response.
    /// Contains Argon2id password hash for offline authentication capability.
    /// </summary>
    public class UserProvisioningDto
    {
        /// <summary>Unique username for login (lowercase, no spaces)</summary>
        public string NomeUsuario { get; set; } = string.Empty;

        /// <summary>User's first name</summary>
        public string PrimeiroNome { get; set; } = string.Empty;

        /// <summary>User's last name</summary>
        public string UltimoNome { get; set; } = string.Empty;

        /// <summary>Argon2id password hash for offline validation</summary>
        /// <remarks>
        /// Format: $argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>
        /// Parameters: m=65536 (64MB memory), t=3 (3 iterations), p=4 (4 parallelism)
        /// Never send plaintext passwords, always use hashes
        /// </remarks>
        public string HashSenha { get; set; } = string.Empty;

        /// <summary>List of UG IDs authorized for this user</summary>
        public List<int> IdsUnidadesGestoras { get; set; } = new();

        /// <summary>Current/default UG for this user</summary>
        public int UnidadeGestoraAtualId { get; set; }

        /// <summary>User account creation timestamp</summary>
        public DateTime DataCriacao { get; set; }
    }

    /// <summary>
    /// DTO for inventory item provisioning from S3 to PWA clients.
    /// Used in GET /api/v2/inventario/carga/{ugId} endpoint response.
    /// Represents "official" inventory items provisioned by backend.
    /// </summary>
    public class ItemProvisioningDto
    {
        /// <summary>Unique identifier for the item (UUID)</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Item name/description</summary>
        public string Nome { get; set; } = string.Empty;

        /// <summary>Item code (unique per UG)</summary>
        public string Codigo { get; set; } = string.Empty;

        /// <summary>Inventory category (e.g., 'Informática', 'Móvel', 'Geral')</summary>
        public string Category { get; set; } = "Geral";

        /// <summary>Physical location of the item</summary>
        public string Localizacao { get; set; } = string.Empty;

        /// <summary>Additional observations or notes</summary>
        public string Observacoes { get; set; } = string.Empty;

        /// <summary>Item status (ativo, inativo, descartado)</summary>
        public string Status { get; set; } = "ativo";

        /// <summary>Date/time when item was recorded</summary>
        public DateTime DataHora { get; set; } = DateTime.Now;

        /// <summary>The UnidadeGestora this item belongs to</summary>
        public int UnidadeGestoraId { get; set; }

        /// <summary>Username of the employee who created the item (audit trail)</summary>
        public string CriadoPor { get; set; } = string.Empty;

        /// <summary>URLs to item photos stored in S3</summary>
        public List<string> RemoteUrls { get; set; } = new();
    }

    /// <summary>
    /// DTO for API v2 Pre-Signed URL response.
    /// Both inventory and user provisioning endpoints return this format.
    /// </summary>
    public class ProvisioningUrlResponseDto
    {
        /// <summary>Pre-Signed URL valid for retrieving provisioning data from S3</summary>
        /// <remarks>
        /// - Valid for 30 minutes
        /// - Supports GET requests only
        /// - Contains AWS credentials in query string
        /// - No authentication header required for S3 download
        /// </remarks>
        public string PresignedUrl { get; set; } = string.Empty;

        /// <summary>S3 object key (path in bucket)</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>S3 bucket name</summary>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Timestamp when URL was generated</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Timestamp when URL expires</summary>
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);

        /// <summary>Expected content type of the S3 object</summary>
        public string ContentType { get; set; } = "application/json";
    }
}
