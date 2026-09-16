using ControlR.Libraries.WebSocketRelay.Common.Extensions;
using ControlR.Web.Client.Components.Layout;
using ControlR.Web.Server.Components;
using ControlR.Web.Server.Components.Account;
using ControlR.Web.Server.Middleware;
using ControlR.Web.Server.Startup;
using ControlR.Web.ServiceDefaults;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using System.Reflection;
using ControlR.Web.Server.EndpointFilters;

var isOpenApiBuild = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSystemd();

await builder.AddControlrServer(isOpenApiBuild);

var appOptions = builder.Configuration
  .GetSection(AppOptions.SectionKey)
  .Get<AppOptions>() ?? new AppOptions();

var app = builder.Build();

app.UseForwardedHeaders();

if (appOptions.UseHttpLogging)
{
  app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/health"),
    appBuilder => appBuilder.UseHttpLogging());
}

app.MapDefaultEndpoints();

if (appOptions.EnableScalarUi)
{
  var versionDescriptions = app.DescribeApiVersions();
  app
    .MapScalarApiReference(options =>
    {
      foreach (var version in versionDescriptions)
      {
        options.AddDocument(version.GroupName.ToLowerInvariant(), version.GroupName);
      }
    })
    .WithDocumentPerVersion();

  app
    .MapOpenApi()
    .WithDocumentPerVersion();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.UseWebAssemblyDebugging();
  app.UseMigrationsEndPoint();
}
else
{
  app.UseHttpsRedirection();
  app.UseExceptionHandler();
  app.UseHsts();
}

// Errors that never reach a controller - no matching route, no credentials, a rejected rate limit -
// leave the pipeline with a status code and no body, so something has to say what the status means.
// The parameterless UseStatusCodePages() already writes an RFC 9457 document on this SDK because
// AddControlrServer registers IProblemDetailsService, but that is a framework default rather than a
// decision made here, and it has changed before. The handler is spelled out so /api answers with one
// ProblemDetails shape wherever the status came from, and so V1ProblemDetailsMiddlewareTests fails
// here rather than silently shipping text/plain if the default ever moves again.
//
// The caveat worth knowing: an MVC ForbidResult from cookie authentication never produces a body of
// its own - AuthorizationRegistrationExtensions sets 401/403 on /api and stops there - so these
// middleware-written documents are the whole answer a cookie caller gets for 401 and 403. A caller
// that expects the body to say why is out of luck; the status code is the message.
app.UseWhen(
  ctx => ctx.Request.Path.StartsWithSegments("/api"),
  apiApp => apiApp.UseStatusCodePages(new StatusCodePagesOptions
  {
    HandleAsync = async context =>
    {
      var problemDetailsService = context.HttpContext.RequestServices
        .GetRequiredService<IProblemDetailsService>();

      await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
      {
        HttpContext = context.HttpContext
      });
    }
  }));

app.MapStaticAssets();
app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
  FileProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "novnc")),
  RequestPath = "/novnc",
  ServeUnknownFileTypes = true,
});

app.MapHub<AgentHub>(AppConstants.AgentHubPath);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<RequirePasswordChangeMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapWebSocketRelay();

// Configure output cache - must be before any middleware that generates response
app.UseOutputCache();

app.MapControllers();

if (appOptions.EnableInteractiveBearerLogin || isOpenApiBuild)
{
  var authGroup = app
    .MapGroup(HttpConstants.Internal.AuthEndpoint)
    .WithGroupName(OpenApiConstants.InternalGroupName)
    .WithTags("Auth");

  authGroup
    .MapIdentityApi<AppUser>()
    .AddEndpointFilter<IEndpointConventionBuilder, IdentityApiRegisterFilter>();
}

app.UseWhen(
  ctx => !ctx.Request.Path.StartsWithSegments("/api"),
  _ =>
  {
    app.MapRazorComponents<App>()
      .AddInteractiveWebAssemblyRenderMode()
      .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
  });

app
  .MapAdditionalIdentityEndpoints()
  .WithGroupName(OpenApiConstants.InternalGroupName)
  .WithTags("Account");

app.MapHub<ViewerHub>(AppConstants.ViewerHubPath);

if (appOptions.UseInMemoryDatabase)
{
  await app.EnsureDatabaseCreated();
}
else
{
  await app.ApplyMigrations();
  await app.SetAllDevicesOffline();
  await app.SetAllUsersOffline();
}

await app.BootstrapAdminUser();
await app.BootstrapServerServiceAccount();

await app.RunAsync();