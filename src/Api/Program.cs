using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Retail360.Api.Catalog;
using Retail360.Api.Identity;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;
using Retail360.Application.Inventory;
using Retail360.Domain.Identity;
using Retail360.Infrastructure;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<BrandService>();
builder.Services.AddScoped<UnitService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<AreaService>();
builder.Services.AddScoped<ShelfService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<ShopSignInService>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ShopAuthorizationResultHandler>();

var authentication = ShopAuthenticationSettings.Read(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authentication.Issuer,
            ValidateAudience = true,
            ValidAudience = authentication.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authentication.SigningKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ShopTokenValidation.ReplaceRolesFromDatabaseAsync
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(ShopPolicies.CatalogRead, policy => policy.RequireRole(
        ShopRoleNames.Admin, ShopRoleNames.Manager, ShopRoleNames.Cashier, ShopRoleNames.InventoryStaff));
    options.AddPolicy(ShopPolicies.CatalogWrite, policy => policy.RequireRole(
        ShopRoleNames.Admin, ShopRoleNames.Manager));
    options.AddPolicy(ShopPolicies.InventoryRead, policy => policy.RequireRole(
        ShopRoleNames.Admin, ShopRoleNames.Manager, ShopRoleNames.Cashier, ShopRoleNames.InventoryStaff));
    options.AddPolicy(ShopPolicies.InventoryManage, policy => policy.RequireRole(
        ShopRoleNames.Admin, ShopRoleNames.Manager, ShopRoleNames.InventoryStaff));
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [scheme] = Array.Empty<string>()
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
    await runner.ApplyAsync(CancellationToken.None);

    var roles = scope.ServiceProvider.GetRequiredService<ShopRoleQuery>();
    await roles.EnsureRequiredRolesAsync(CancellationToken.None);

    var bootstrap = scope.ServiceProvider.GetRequiredService<ShopAdminBootstrap>();
    await bootstrap.EnsureAdminAsync(CancellationToken.None);
}

app.UseMiddleware<CatalogExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
