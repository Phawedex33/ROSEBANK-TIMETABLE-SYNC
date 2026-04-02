using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
using TimetableSync.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Data protection persistence to survive restarts/replacements
var keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimetableSyncKeys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("TimetableSync")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
// Session is retained solely for OAuth CSRF state (google_oauth_state).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

builder.Services.AddSingleton<ITokenStore, EncryptedFileTokenStore>();

builder.Services.Configure<GoogleCalendarOptions>(builder.Configuration.GetSection("GoogleCalendar"));
builder.Services.Configure<ReferenceAdminOptions>(builder.Configuration.GetSection("ReferenceAdmin"));

builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddScoped<ITimetableParser, TimetableParser>();
builder.Services.AddSingleton<IRosebankReferenceService, RosebankReferenceService>();
builder.Services.AddScoped<IAcademicParser, AcademicParser>();
builder.Services.AddScoped<IAssessmentParser, AssessmentParser>();
builder.Services.AddScoped<IAiParsingService, AiParsingService>();
builder.Services.AddScoped<IAcademicScheduleBuilder, AcademicScheduleBuilder>();
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IRosebankParserService, RosebankParserService>();
// IHttpContextAccessor is no longer needed; tokens are stored by FileTokenStore.

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        // Hardened CORS: Restrict to known frontend origins.
        // In production, these should come from configuration.
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "https://localhost:7068")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
var frontendCandidates = new[]
{
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "frontend", "dist")),
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "frontend"))
};
var frontendPath = frontendCandidates.FirstOrDefault(Directory.Exists);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseCors("frontend");
app.UseSession();
if (Directory.Exists(frontendPath))
{
    var provider = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = provider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = provider
    });
}
app.UseAuthorization();
app.MapControllers();
if (Directory.Exists(frontendPath))
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await context.Response.SendFileAsync(Path.Combine(frontendPath, "index.html"));
    });
}

app.Run();

public partial class Program { }
