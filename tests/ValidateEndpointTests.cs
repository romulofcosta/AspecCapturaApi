using FluentAssertions;
using AspecCapturaApi.Models;
using System.Net;
using System.Net.Http.Json;

namespace AspecCapturaApi.Tests;

public class ValidateEndpointTests : IClassFixture<CaptureTestFactory>
{
    private readonly CaptureTestFactory _factory;
    private readonly HttpClient _client;

    public ValidateEndpointTests(CaptureTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task ValidateTombamento_ReturnsExists_WhenFound()
    {
        _factory.DataRoot = CaptureTestFactory.BuildDefaultRoot();
        _factory.SetupS3Mock();

        var response = await _client.GetAsync("/api/capture/validate/00000005?prefix=CE999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ValidateTombamentoResponse>();
        result.Should().NotBeNull();
        result!.Exists.Should().BeTrue();
        result.Nutomb.Should().Be("00000005");
        result.IdPatomb.Should().Be(619859188188L);
        result.Deprod.Should().Be("ARMARIO 2 PORTAS");
        result.Esfera.Should().Be("E");
    }

    [Fact]
    public async Task ValidateTombamento_ReturnsExistsFalse_WhenNotFound()
    {
        _factory.DataRoot = CaptureTestFactory.BuildDefaultRoot();
        _factory.SetupS3Mock();

        var response = await _client.GetAsync("/api/capture/validate/99999999?prefix=CE999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ValidateTombamentoResponse>();
        result!.Exists.Should().BeFalse();
        result.Nutomb.Should().Be("99999999");
    }

    [Fact]
    public async Task ValidateTombamento_Returns400_WhenPrefixFormatInvalid()
    {
        // "INVALID" não passa na validação de formato (^[A-Z]{2}\d{3}$) → 400
        var response = await _client.GetAsync("/api/capture/validate/00000005?prefix=INVALID");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateTombamento_Returns403_WhenPrefixDoesNotMatchToken()
    {
        // SP001 tem formato válido mas não bate com o prefixo do token JWT (CE999)
        // Comportamento correto: 403 Forbidden (autorização negada antes de consultar S3)
        var response = await _client.GetAsync("/api/capture/validate/00000005?prefix=SP001");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidateTombamento_Returns400_WhenPrefixMissing()
    {
        var response = await _client.GetAsync("/api/capture/validate/00000005");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
