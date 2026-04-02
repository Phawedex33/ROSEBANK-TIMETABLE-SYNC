using Microsoft.Extensions.FileProviders;
using System.Text.Json.Serialization;
using TimetableSync.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ReferenceAdminOptions>(builder.Configuration.GetSection("ReferenceAdmin"));

builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddScoped<ITimetableParser, TimetableParser>();
builder.Services.AddSingleton<IRosebankReferenceService, RosebankReferenceService>();
builder.Services.AddScoped<IAcademicParser, AcademicParser>();
builder.Services.AddScoped<IAssessmentParser, AssessmentParser>();
builder.Services.AddScoped<IAiParsingService, AiParsingService>();
builder.Services.AddScoped<IAcademicScheduleBuilder, AcademicScheduleBuilder>();
builder.Services.AddScoped<ICalendarExportService, CalendarExportService>();
builder.Services.AddScoped<IRosebankParserService, RosebankParserService>();

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
