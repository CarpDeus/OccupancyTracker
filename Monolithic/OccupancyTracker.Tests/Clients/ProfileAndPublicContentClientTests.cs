using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NUnit.Framework;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Clients;

public sealed class ProfileAndPublicContentClientTests
{
    [Test]
    public async Task ProfileApiClient_SaveAsyncWithoutToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var sut = new ProfileApiClient(http, new TokenProvider());
        var profile = new UserInformation
        {
            EmailAddress = "alice@example.com",
            FirstName = "Alice",
            LastName = "Adams"
        };

        // Act
        Func<Task> saveProfile = () => sut.SaveAsync(profile);

        // Assert
        await saveProfile.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task ProfileApiClient_SaveAsyncWithToken_ReturnsSavedProfileAndUsesBearerToken()
    {
        // Arrange
        var profile = new UserInformation
        {
            UserInformationId = 1,
            EmailAddress = "alice@example.com",
            FirstName = "Alice",
            LastName = "Adams"
        };
        var handler = new RecordingHandler(HttpStatusCode.OK, profile);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var sut = new ProfileApiClient(http, new TokenProvider { AccessToken = "current-token" });

        // Act
        var result = await sut.SaveAsync(profile);

        // Assert
        result.UserInformationId.Should().Be(1);
        handler.Method.Should().Be(HttpMethod.Put);
        handler.Path.Should().Be("/api/me");
        handler.Authorization.Should().Be("Bearer current-token");
    }

    [Test]
    public async Task PublicContentApiClient_GetAsyncEscapedPage_ReturnsContent()
    {
        // Arrange
        var handler = new RecordingHandler(HttpStatusCode.OK, "Privacy Policy");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var sut = new PublicContentApiClient(http);

        // Act
        var result = await sut.GetAsync("privacy policy");

        // Assert
        result.Should().Be("Privacy Policy");
        handler.Path.Should().Be("/api/public-content/privacy%20policy");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;
        private readonly object? responseBody;

        public RecordingHandler(HttpStatusCode statusCode, object? responseBody = null)
        {
            this.statusCode = statusCode;
            this.responseBody = responseBody;
        }

        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            var content = responseBody == null
                ? null
                : JsonContent.Create(responseBody);
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }
}
