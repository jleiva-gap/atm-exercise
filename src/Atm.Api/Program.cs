using Atm.Api;
using Atm.Application;
using Atm.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Local execution must not require access to the Windows Event Log.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TransactionService>();
builder.Services.AddAtmInfrastructure(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var configuredPath = configuration["Atm:DatabasePath"];
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return configuredPath;
    // Source runs find the repository from the content root. Published deployments default to their content root.
    var directory = new DirectoryInfo(
        provider.GetRequiredService<IHostEnvironment>().ContentRootPath
    );
    while (
        directory.Parent is not null && !File.Exists(Path.Combine(directory.FullName, "Atm.slnx"))
    )
        directory = directory.Parent;
    var root = File.Exists(Path.Combine(directory.FullName, "Atm.slnx"))
        ? directory.FullName
        : provider.GetRequiredService<IHostEnvironment>().ContentRootPath;
    return Path.Combine(root, "data", "atm.db");
});

var app = builder.Build();
await app.Services.InitializeAtmDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages(async status =>
{
    var context = status.HttpContext;
    await ApiProblems
        .Create(
            context,
            context.Response.StatusCode,
            "http_error",
            "The requested HTTP operation is unavailable."
        )
        .ExecuteAsync(context);
});
app.MapAtmApi();
app.Run();

public partial class Program;
