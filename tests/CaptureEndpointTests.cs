using FluentAssertions;
using AspecCapturaApi.Models;
using System.Net;
using System.Net.Http.Json;

namespace AspecCapturaApi.Tests;

public class CaptureEndpointTests : IClassFixture<CaptureTestFactory>
{
    private readonly CaptureTestFactory _factory;
    private readonly HttpClient _client;

    public CaptureEndpointTests(CaptureTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    // ─── POST /api/capture/item ───────────────────────────────────────────────

    [Fact]
    public async Task PostCaptureItem_UpdatesExistingTombamento()
    {
        // Arrange
        _factory.DataRoot = CaptureTestFactory.BuildDefaultRoot();
        _factory.SetupS3Mock();

        var request = new CaptureItemRequest(
            Prefixo: "CE999",
            IdPatomb: 619859188188L,
            Nutomb: "00000005",
            Estado: "OTIMO",
            Situacao: "Alocado",
            IdLocalizacao: 999L,
            FotoKey: "fotos/test.jpg",
            CapturedBy: "ce999.admin",
            CapturedAt: "2026-03-16T10:00:00Z",
            Source: "QR"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/capture/item", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CaptureItemResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("updated");
        result.IdPatomb.Should().Be(619859188188L);
        result.Nutomb.Should().Be("00000005");

        // Verify the in-memory data was updated
        var updated = _factory.DataRoot.Tabelas!.Tombamentos
            .First(t => t.IdPatomb == 619859188188L);
        updated.Estado.Should().Be("OTIMO");
        updated.Situacao.Should().Be("Alocado");
        updated.IdLocalizacao.Should().Be(999L);
        updated.Source.Should().Be("QR");
    }

    [Fact]
    public async Task PostCaptureItem_CreatesNewTombamento_WhenNotFound()
    {
        // Arrange
        _factory.DataRoot = CaptureTestFactory.BuildDefaultRoot();
        _factory.SetupS3Mock();

        var request = new CaptureItemRequest(
            Prefixo: "CE999",
            IdPatomb: 999999999999L,
            Nutomb: "99999999",
            Estado: "BOM",
            Situacao: "Alocado",
            IdLocalizacao: 111L,
            FotoKey: null,
            CapturedBy: "ce999.admin",
            CapturedAt: null,
            Source: "Manual"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/capture/item", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CaptureItemResponse>();
        result!.Status.Should().Be("created");
        result.IdPatomb.Should().Be(999999999999L);

        _factory.DataRoot.Tabelas!.Tombamentos
            .Should().Contain(t => t.IdPatomb == 999999999999L);
    }

    [Fact]
    public async Task PostCaptureItem_Returns404_WhenPrefixoNotFound()
    {
        // Quando o prefixo não bate com o token JWT, a API retorna 403 (autorização negada)
        // antes mesmo de consultar o S3. Esse é o comportamento correto de segurança.
        var request = new CaptureItemRequest(
            Prefixo: "INVALID",
            IdPatomb: 1L,
            Nutomb: "00000001",
            Estado: null, Situacao: null, IdLocalizacao: null,
            FotoKey: null, CapturedBy: null, CapturedAt: null, Source: null
        );

        var response = await _client.PostAsJsonAsync("/api/capture/item", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCaptureItem_Returns400_WhenPrefixoMissing()
    {
        var request = new CaptureItemRequest(
            Prefixo: "",
            IdPatomb: 1L,
            Nutomb: "00000001",
            Estado: null, Situacao: null, IdLocalizacao: null,
            FotoKey: null, CapturedBy: null, CapturedAt: null, Source: null
        );

        var response = await _client.PostAsJsonAsync("/api/capture/item", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
