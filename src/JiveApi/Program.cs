using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting; // Lab6 --> limit the flow

var builder = WebApplication.CreateBuilder(args);

// Swagger + CORS
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// （Lab6）flow limit policy：60/minute
builder.Services.AddRateLimiter(_ => _.AddFixedWindowLimiter("fixed", o =>
{
    o.Window = TimeSpan.FromMinutes(1);
    o.PermitLimit = 60;
    o.QueueLimit = 0;
}));

var app = builder.Build();

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseRateLimiter();

// （Lab6）Simple API Key for protection /v1/*
var apiKey = builder.Configuration["ApiKey"] ?? Environment.GetEnvironmentVariable("API_KEY");
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/v1"))
    {
        if (string.IsNullOrEmpty(apiKey) ||
            !ctx.Request.Headers.TryGetValue("x-api-key", out var key) || key != apiKey)
        { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsync("Unauthorized"); return; }
    }
    await next();
});

// Root Path → Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

// Health Check
app.MapGet("/health", () => Results.Ok(new { status = "OK" }));

// Dummy data + basic CRUD for testing
List<Item> items = new() {
    new(1,"Alpha",12.3m), new(2,"Beta",45.6m), new(3,"Gamma",78.9m), new(4,"Delta",10m), new(5,"Epsilon",20m)
};

// GET /v1/items?page=1&pageSize=2
app.MapGet("/v1/items", ([FromQuery] int page = 1, [FromQuery] int pageSize = 2) =>
{
    if (page < 1 || pageSize < 1)
        return Results.ValidationProblem(new Dictionary<string, string[]> { { "page", [">=1"] }, { "pageSize", [">=1"] } });
    var total = items.Count;
    var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize);
    return Results.Ok(new { page, pageSize, total, items = pageItems });
}).RequireRateLimiting("fixed");

// GET /v1/items/{id}
app.MapGet("/v1/items/{id:int}", (int id) =>
{
    var it = items.FirstOrDefault(x => x.Id == id);
    return it is null ? Results.NotFound() : Results.Ok(it);
}).RequireRateLimiting("fixed");

// POST /v1/items
app.MapPost("/v1/items", (ItemDto dto) =>
{
    if (!ItemValidator.Validate(dto, out var problems)) return Results.ValidationProblem(problems);
    var id = items.Any() ? items.Max(i => i.Id) + 1 : 1;
    var ni = new Item(id, dto.Name, dto.Price);
    items.Add(ni);
    return Results.Created($"/v1/items/{id}", ni);
}).RequireRateLimiting("fixed");

// PUT /v1/items/{id}
app.MapPut("/v1/items/{id:int}", (int id, ItemDto dto) =>
{
    if (!ItemValidator.Validate(dto, out var problems)) return Results.ValidationProblem(problems);
    var i = items.FindIndex(x => x.Id == id);
    if (i < 0) return Results.NotFound();
    items[i] = items[i] with { Name = dto.Name, Price = dto.Price };
    return Results.Ok(items[i]);
}).RequireRateLimiting("fixed");

// DELETE /v1/items/{id}
app.MapDelete("/v1/items/{id:int}", (int id) =>
{
    var it = items.FirstOrDefault(x => x.Id == id);
    if (it is null) return Results.NotFound();
    items.Remove(it);
    return Results.NoContent();
}).RequireRateLimiting("fixed");

app.Run();

record Item(int Id, string Name, decimal Price);
record ItemDto(string Name, decimal Price);

// Add static class for validation
static class ItemValidator
{
    public static bool Validate(ItemDto d, out Dictionary<string, string[]> probs)
    {
        probs = new();
        if (string.IsNullOrWhiteSpace(d.Name)) probs["name"] = ["required"];
        if (d.Price <= 0) probs["price"] = ["must be > 0"];
        return probs.Count == 0;
    }
}
