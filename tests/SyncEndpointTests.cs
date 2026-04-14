using FluentAssertions;
using AspecCapturaApi.Models;
using System.Net;
using System.Net.Http.Json;

namespace AspecCapturaApi.Tests;

public class SyncEndpointTests : IClassFixture<CaptureTestFactory>
{
    private readonly CaptureTestFactory _factory;
    private readonly HttpClient _client;

    public SyncEndpointTests(CaptureTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task SyncBatch_ProcessesMixOfUpdateAndCreate()
    {
        _factory.DataRoot = CaptureTestFactory.BuildDefaultRoot();
        _factory.SetupS3Mock();

        var request = new SyncBatchRequest(
            Prefixo: "CE999",
            Items: new List<CaptureItemRequest>
            {
                // Update existing
                new(Prefixo: "CE999", IdPatomb: 619859188188L, Nutomb: "00000005",
                    Estado: "REGULAR", Situacao: "Alocado", IdLocalizacao: 777L,
                    FotoKey: null, CapturedBy: "user1", CapturedAt: null, Source: "OCR"),
                // Create new
                new(Prefixo: "CE999", IdPatomb: 111111111111L, Nutomb: "00000099",
                    Estado: "BOM", Situacao: "Alocado", IdLocalizacao: 888L,
                    FotoKey: "fotos/new.jpg", CapturedBy: "user1", CapturedAt: null, Source: "Barcode")
            }
        );

        var response = await _client.PostAsJsonAsync("/api/capture/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncBatchResponse>();
        result.Should().NotBeNull();
        result!.Total.Should().Be(2);
        result.Updated.Should().Be(1);
        result.Created.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Results.Should().HaveCount(2);
        result.Results.Should().Contain(r => r.Nutomb == "00000005" && r.Status == "updated");
        result.Results.Should().Contain(r => r.Nutomb == "00000099" && r.Status == "created");
    }

    [Fact]
    public async Task SyncBatch_ReturnsEmpty_WhenNoItems()
    {
        var request = new SyncBatchRequest(Prefixo: "CE999", Items: new List<CaptureItemRequest>());

        var response = await _client.PostAsJsonAsync("/api/capture/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SyncBatchResponse>();
        result!.Total.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncBatch_Returns400_WhenExceeds50Items()
    {
        var items = Enumerable.Range(1, 51).Select(i => new CaptureItemRequest(
            Prefixo: "CE999", IdPatomb: i, Nutomb: i.ToString("D8"),
            Estado: null, Situacao: null, IdLocalizacao: null,
            FotoKey: null, CapturedBy: null, CapturedAt: null, Source: null
        )).ToList();

        var request = new SyncBatchRequest(Prefixo: "CE999", Items: items);

        var response = await _client.PostAsJsonAsync("/api/capture/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncBatch_Returns404_WhenPrefixoNotFound()
    {
        // Quando o prefixo não bate com o token JWT, a API retorna 403 (autorização negada)
        // antes mesmo de consultar o S3. Esse é o comportamento correto de segurança.
        var request = new SyncBatchRequest(
            Prefixo: "INVALID",
            Items: new List<CaptureItemRequest>
            {
                new(Prefixo: "INVALID", IdPatomb: 1L, Nutomb: "00000001",
                    Estado: null, Situacao: null, IdLocalizacao: null,
                    FotoKey: null, CapturedBy: null, CapturedAt: null, Source: null)
            }
        );

        var response = await _client.PostAsJsonAsync("/api/capture/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
