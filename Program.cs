using BookStoreApi.Models;
using BookStoreApi.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// 1. Read the BookStoreDatabase settings and register the service (one MongoClient for the app)
builder.Services.Configure<BookStoreDatabaseSettings>(
    builder.Configuration.GetSection("BookStoreDatabase"));
builder.Services.AddSingleton<BooksService>();

// 2. Keep JSON property names as written in C# (Name, Price), not camelCase
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// 3. OpenAPI document at /openapi/v1.json.
//    Version 3.0 so Swagger UI can read it (.NET 11 defaults to 3.2, which Swagger UI rejects).
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
});

var app = builder.Build();

// 4. Swagger UI at /swagger (Development only).
//    Needs: dotnet add package Swashbuckle.AspNetCore.SwaggerUI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "BookStore API");
    });
}

// 5. Endpoints, all under /books, shown under "Books" in Swagger
var books = app.MapGroup("/books").WithTags("Books");

// GET /books
books.MapGet("/", async (BooksService service) =>
    await service.GetAsync());

books.MapGet("/search",
    async (
        string? category,
        decimal? minPrice,
        decimal? maxPrice,
        BooksService service) =>
{
    var books = await service.SearchAsync(
        category,
        minPrice,
        maxPrice);

    return Results.Ok(books);
});

books.MapGet("/top",
    async (
        int count,
        BooksService service) =>
{
    var books = await service.GetTopBooksAsync(count);

    return Results.Ok(books);
});

// GET /books/{id}   (an ObjectId is 24 characters)
books.MapGet("/{id:length(24)}", async (string id, BooksService service) =>
    await service.GetAsync(id) is Book book
        ? Results.Ok(book)
        : Results.NotFound());

// POST /books
books.MapPost("/", async (Book newBook, BooksService service) =>
{
    await service.CreateAsync(newBook);
    return Results.Created($"/books/{newBook.Id}", newBook);
});

books.MapPost("/upload", async (
    IFormFile file,
    BooksService service) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    if (!file.FileName.EndsWith(".json"))
    {
        return Results.BadRequest("File must be a JSON file.");
    }

    List<Book>? booksToUpload;

    try
    {
        using var reader = new StreamReader(file.OpenReadStream());

        var json = await reader.ReadToEndAsync();

        booksToUpload = JsonSerializer.Deserialize<List<Book>>(json);
    }
    catch
    {
        return Results.BadRequest("Invalid JSON.");
    }

    if (booksToUpload == null)
    {
        return Results.BadRequest("Invalid JSON.");
    }

    var invalidBooks = booksToUpload
        .Where(b =>
            string.IsNullOrWhiteSpace(b.BookName) ||
            string.IsNullOrWhiteSpace(b.Category) ||
            string.IsNullOrWhiteSpace(b.Author) ||
            b.Price <= 0)
        .ToList();

    if (invalidBooks.Any())
    {
        return Results.BadRequest(new
        {
            Message = "Invalid books found.",
            InvalidBooks = invalidBooks
        });
    }

    await service.InsertManyAsync(booksToUpload);

    return Results.Ok(new
    {
        InsertedCount = booksToUpload.Count,
        Ids = booksToUpload.Select(b => b.Id)
    });
})
.DisableAntiforgery();


// PUT /books/{id}
books.MapPut("/{id:length(24)}", async (string id, Book updatedBook, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    updatedBook.Id = book.Id;
    await service.UpdateAsync(id, updatedBook);
    return Results.NoContent();
});

// DELETE /books/{id}
books.MapDelete("/{id:length(24)}", async (string id, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    await service.RemoveAsync(id);
    return Results.NoContent();
});

books.MapPatch("/{id:length(24)}/price",
    async (
        string id,
        UpdatePriceRequest request,
        BooksService service) =>
{
    var book = await service.GetAsync(id);

    if (book is null)
    {
        return Results.NotFound();
    }

    await service.UpdatePriceAsync(
        id,
        request.Price);

    return Results.NoContent();
});

app.Run();
