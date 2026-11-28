using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using MudBlazor;
using MudBlazor.Services;
using NUnit.Framework;
using OccupancyTracker.Components.Pages.Forms;
using OccupancyTracker.Models;
using OccupancyTracker.Service;

namespace OccupancyTracker.Tests.Components.OrganizationForm;

public sealed class OrganizationFormTests
{
    [Test]
    public async Task Save_ValidNewOrganization_PostsToApi()
    {
        // Arrange
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        var organization = new Organization
        {
            OrganizationName = "Example",
            OrganizationAddress = new Address
            {
                AddressLine1 = "123 Main Street", City = "Seattle", State = "WA",
                PostalCode = "98101", Country = "US"
            },
            PhoneNumber = new PhoneNumber { CountryCode = "1", Number = "5550100" }
        };
        var saved = new Organization { OrganizationId = 1, OrganizationSqid = "abc" };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(saved) });
        context.Services.AddSingleton(new OrganizationApiClient(
            new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost/") },
            new TokenProvider { AccessToken = "test-token" }));
        var component = context.Render<OccupancyTracker.Components.Pages.Forms.OrganizationForm>(parameters => parameters
            .Add(x => x.organization, organization)
            .Add(x => x.userInformation, new UserInformation { UserInformationSqid = "user" })
            .Add(x => x.readOnly, false));

        // Act
        await component.FindAll("button").Single(button => button.TextContent.Trim() == "Save").ClickAsync(new());

        // Assert
        handler.Protected().Verify(
            "SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath == "/api/organizations"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task Save_CityTooShort_DisplaysValidationErrorAndDoesNotPost()
    {
        // Arrange
        const string expectedError = "City is required and must be between 2 and 512 characters long.";
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        var organization = new Organization
        {
            OrganizationName = "Example",
            OrganizationAddress = new Address
            {
                AddressLine1 = "123 Main Street", City = "S", State = "WA",
                PostalCode = "98101", Country = "US"
            },
            PhoneNumber = new PhoneNumber { CountryCode = "1", Number = "5550100" }
        };
        var handler = new Mock<HttpMessageHandler>();
        context.Services.AddSingleton(new OrganizationApiClient(
            new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost/") },
            new TokenProvider { AccessToken = "test-token" }));
        var component = context.Render<OccupancyTracker.Components.Pages.Forms.OrganizationForm>(parameters => parameters
            .Add(x => x.organization, organization)
            .Add(x => x.userInformation, new UserInformation { UserInformationSqid = "user" })
            .Add(x => x.readOnly, false));

        // Act
        await component.FindAll("button").Single(button => button.TextContent.Trim() == "Save").ClickAsync(new());

        // Assert
        component.Markup.Should().Contain(expectedError);
        handler.Protected().Verify(
            "SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task Save_MissingRequiredFields_ShowsFeedbackAndDoesNotPost()
    {
        // Arrange
        const string expectedMessage = "Please correct the highlighted organization fields before saving.";
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        var handler = new Mock<HttpMessageHandler>();
        context.Services.AddSingleton(new OrganizationApiClient(
            new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost/") },
            new TokenProvider { AccessToken = "test-token" }));
        var component = context.Render<OccupancyTracker.Components.Pages.Forms.OrganizationForm>(parameters => parameters
            .Add(x => x.organization, new Organization())
            .Add(x => x.userInformation, new UserInformation { UserInformationSqid = "user" })
            .Add(x => x.readOnly, false));

        // Act
        await component.FindAll("button").Single(button => button.TextContent.Trim() == "Save").ClickAsync(new());

        // Assert
        context.Services.GetRequiredService<ISnackbar>().ShownSnackbars.Should()
            .Contain(snackbar => snackbar.Message == expectedMessage);
        handler.Protected().Verify(
            "SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}
