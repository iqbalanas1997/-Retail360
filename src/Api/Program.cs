using Retail360.Api.Catalog;
using Retail360.Application.Catalog;
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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
    await runner.ApplyAsync(CancellationToken.None);

    var roles = scope.ServiceProvider.GetRequiredService<ShopRoleQuery>();
    await roles.EnsureRequiredRolesAsync(CancellationToken.None);
}

app.UseMiddleware<CatalogExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
