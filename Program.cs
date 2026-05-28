using Apache.Ignite.Core;
using apiplatform.Controllers;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.VisualBasic;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "AllowStoryPopCORS",
                      policy =>
                      {
                          policy.WithOrigins("https://icpress.org", "https://www.icpress.org")
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                      });
});

var serverSettings =
    builder.Configuration.GetSection(nameof(ServerSettings))
                     .Get<ServerSettings>(); //parse serverSettings from json

if (serverSettings != null)
{
    builder.Services.AddSingleton(serverSettings); // register serverSettings
     ConfigUtil.ConfigureServices(builder.Services, serverSettings);
    if (!string.IsNullOrEmpty(serverSettings.APIEndpoint))
    {
        // Sets the hosting endpoint
        builder.WebHost.UseUrls(serverSettings.APIEndpoint);
    }
}

var app = builder.Build();

if (serverSettings != null && serverSettings?.SwiftTempAuthUser != "" && serverSettings?.SwiftBucketLargePath != "" && serverSettings?.SwiftBucketSmallPath != "" && serverSettings?.SwiftBucketUserMessagePath != "")
{ //check & create buckets for storage
    await GeneralUtil.CheckDependencyStartup(serverSettings!, new List<string> { serverSettings!.SwiftBucketSmallPath, serverSettings.SwiftBucketLargePath, serverSettings.SwiftBucketUserMessagePath }, app.Logger);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors("AllowStoryPopCORS");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();


app.Run();

