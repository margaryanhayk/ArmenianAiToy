using ArmenianAiToy.Api.Middleware;
using ArmenianAiToy.Infrastructure;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Infrastructure (DB, OpenAI, services)
builder.Services.AddInfrastructure(builder.Configuration);

// JWT authentication for parent endpoints
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ArmenianAiToyDefaultSecretKeyThatShouldBeChanged123!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ArmenianAiToy",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ArmenianAiToy",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// CORS: allow all for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Auto-create/migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Device auth middleware (for /api/chat, /api/audio endpoints)
app.UseMiddleware<DeviceAuthMiddleware>();

// Serve static files (for web UI testing)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "ArmenianAiToy API" }));

app.Run();
