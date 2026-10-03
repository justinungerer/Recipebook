using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeTool.Core;
using System.Security.Cryptography;
using System.Text;

namespace RecipeTool.App;

public sealed class RecipeCaptureServer
{
    private const int Port = 47831;
    private const string BridgeToken = "cba61e4a-f2c9-4b40-a691-3fd428d775b6";
    private readonly Func<CapturedRecipe, Task> _onRecipeCaptured;
    private readonly object _requestLock = new();
    private WebApplication? _application;
    private Guid? _pendingRequestId;
    private DateTimeOffset _pendingExpiry;
    private DateTimeOffset? _lastExtensionSeen;

    public DateTimeOffset? LastExtensionSeen
    {
        get
        {
            lock (_requestLock)
            {
                return _lastExtensionSeen;
            }
        }
    }

    public RecipeCaptureServer(Func<CapturedRecipe, Task> onRecipeCaptured)
    {
        _onRecipeCaptured = onRecipeCaptured;
    }

    public async Task StartAsync()
    {
        if (_application is not null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
        builder.Logging.ClearProviders();
        builder.Services.AddCors(options => options.AddPolicy("extension", policy =>
            policy.SetIsOriginAllowed(IsChromeExtensionOrigin)
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type", "X-RecipeTool-Token")));
        var app = builder.Build();
        app.UseCors("extension");
        app.MapGet("/api/status", (HttpContext context) =>
        {
            if (!IsAuthorized(context))
            {
                return Results.Unauthorized();
            }

            TouchExtension();
            return Results.Ok(new { running = true });
        });
        app.MapGet("/api/capture-request", (HttpContext context) =>
        {
            if (!IsAuthorized(context))
            {
                return Results.Unauthorized();
            }

            TouchExtension();
            var requestId = GetPendingRequest();
            return requestId is null
                ? Results.NoContent()
                : Results.Ok(new { requestId });
        });
        app.MapPost("/api/capture/{requestId:guid}", async (Guid requestId, HttpContext context, CapturedRecipe captured) =>
        {
            if (!IsAuthorized(context))
            {
                return Results.Unauthorized();
            }

            if (!TryCompleteRequest(requestId))
            {
                return Results.Conflict(new { error = "No matching capture request is waiting." });
            }

            captured.SourceUrl = string.IsNullOrWhiteSpace(captured.SourceUrl)
                ? "https://unknown.invalid/"
                : captured.SourceUrl;
            await _onRecipeCaptured(captured);
            return Results.Accepted();
        });

        _application = app;
        try
        {
            await app.StartAsync();
        }
        catch
        {
            _application = null;
            await app.DisposeAsync();
            throw;
        }
    }

    public Guid RequestCapture()
    {
        lock (_requestLock)
        {
            _pendingRequestId = Guid.NewGuid();
            _pendingExpiry = DateTimeOffset.UtcNow.AddSeconds(30);
            return _pendingRequestId.Value;
        }
    }

    public async Task StopAsync()
    {
        var app = _application;
        _application = null;
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private Guid? GetPendingRequest()
    {
        lock (_requestLock)
        {
            if (_pendingRequestId.HasValue && _pendingExpiry <= DateTimeOffset.UtcNow)
            {
                _pendingRequestId = null;
            }

            return _pendingRequestId;
        }
    }

    private bool TryCompleteRequest(Guid requestId)
    {
        lock (_requestLock)
        {
            if (_pendingRequestId != requestId || _pendingExpiry <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            _pendingRequestId = null;
            return true;
        }
    }

    private void TouchExtension()
    {
        lock (_requestLock)
        {
            _lastExtensionSeen = DateTimeOffset.UtcNow;
        }
    }

    private static bool IsAuthorized(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        var suppliedToken = context.Request.Headers["X-RecipeTool-Token"].FirstOrDefault();
        if (suppliedToken is null || (origin is not null && !IsChromeExtensionOrigin(origin)))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(suppliedToken),
            Encoding.UTF8.GetBytes(BridgeToken));
    }

    private static bool IsChromeExtensionOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == "chrome-extension"
        && uri.Host.Length == 32
        && uri.Host.All(character => character is >= 'a' and <= 'p');
}

public sealed class CapturedRecipe
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    public List<string> Tags { get; set; } = [];
    public int Servings { get; set; } = 4;
    public List<string> Ingredients { get; set; } = [];
    public List<string> Instructions { get; set; } = [];
    public string SourceUrl { get; set; } = "";

    public Recipe ToRecipe() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Recipe from webpage" : Name.Trim(),
        Description = Description.Trim(),
        Category = string.IsNullOrWhiteSpace(Category) ? "Other" : Category.Trim(),
        Tags = Tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Servings = Servings is > 0 and <= 10000 ? Servings : 4,
        Ingredients = Ingredients.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList(),
        Instructions = Instructions.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList(),
        SourceUrl = SourceUrl.Trim(),
        NeedsReview = true
    };
}
