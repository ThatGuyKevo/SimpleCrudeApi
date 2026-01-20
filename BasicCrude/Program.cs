using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddOpenApi();

// Minimal "in-memory DB"
builder.Services.AddSingleton<UserStore>();

var app = builder.Build();

// --------------------
// Middleware (5 pts)
// --------------------

// 1) Request logging middleware (simple + visible)
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    Console.WriteLine($"--> {context.Request.Method} {context.Request.Path}");
    await next();
    sw.Stop();
    Console.WriteLine($"<-- {context.Response.StatusCode} ({sw.ElapsedMilliseconds}ms) {context.Request.Method} {context.Request.Path}");
});

// 2) Simple API key auth middleware (counts as auth middleware)

app.Use(async (context, next) =>
{
    // Allow OpenAPI endpoints without key
    if (context.Request.Path.StartsWithSegments("/openapi"))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-API-KEY", out var key) || key != "dev-key")
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-API-KEY." });
        return;
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// --------------------
// CRUD Endpoints
// --------------------

// GET all users
app.MapGet("/users", (UserStore store) =>
{
    return Results.Ok(store.GetAll());
})
.WithName("GetUsers");

// GET user by id
app.MapGet("/users/{id:int}", (int id, UserStore store) =>
{
    var user = store.GetById(id);
    return user is null ? Results.NotFound(new { error = "User not found." }) : Results.Ok(user);
})
.WithName("GetUserById");

// POST create user (validation included)
app.MapPost("/users", (CreateUserRequest request, UserStore store) =>
{
    var errors = ValidateModel(request);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    var created = store.Create(request);
    return Results.Created($"/users/{created.Id}", created);
})
.WithName("CreateUser");

// PUT update user (validation included)
app.MapPut("/users/{id:int}", (int id, UpdateUserRequest request, UserStore store) =>
{
    var errors = ValidateModel(request);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    var updated = store.Update(id, request);
    return updated is null ? Results.NotFound(new { error = "User not found." }) : Results.Ok(updated);
})
.WithName("UpdateUser");

// DELETE user
app.MapDelete("/users/{id:int}", (int id, UserStore store) =>
{
    var ok = store.Delete(id);
    return ok ? Results.NoContent() : Results.NotFound(new { error = "User not found." });
})
.WithName("DeleteUser");

app.Run();

// --------------------
// Models + validation helpers 
// --------------------

static List<string> ValidateModel(object model)
{
    var ctx = new ValidationContext(model);
    var results = new List<ValidationResult>();
    Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);

    return results.Select(r => r.ErrorMessage ?? "Validation error.").ToList();
}

record User(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    DateTime CreatedOnUtc
);

record CreateUserRequest(
    [Required, MinLength(2), MaxLength(50)] string FirstName,
    [Required, MinLength(2), MaxLength(50)] string LastName,
    [Required, EmailAddress, MaxLength(120)] string Email
);

record UpdateUserRequest(
    [Required, MinLength(2), MaxLength(50)] string FirstName,
    [Required, MinLength(2), MaxLength(50)] string LastName,
    [Required, EmailAddress, MaxLength(120)] string Email,
    bool IsActive
);

// --------------------
// In-memory store
// --------------------
class UserStore
{
    private readonly List<User> _users = new();
    private int _nextId = 1;

    public UserStore()
    {
        // seed a couple users so GET shows something
        _users.Add(new User(_nextId++, "Kevin", "Rangel", "kevin@example.com", true, DateTime.UtcNow));
        _users.Add(new User(_nextId++, "Jane", "Doe", "jane@example.com", true, DateTime.UtcNow));
    }

    public IReadOnlyList<User> GetAll() => _users;

    public User? GetById(int id) => _users.FirstOrDefault(u => u.Id == id);

    public User Create(CreateUserRequest req)
    {
        // Extra validation: enforce unique email
        if (_users.Any(u => string.Equals(u.Email, req.Email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Email already exists.");

        var user = new User(
            Id: _nextId++,
            FirstName: req.FirstName.Trim(),
            LastName: req.LastName.Trim(),
            Email: req.Email.Trim(),
            IsActive: true,
            CreatedOnUtc: DateTime.UtcNow
        );

        _users.Add(user);
        return user;
    }

    public User? Update(int id, UpdateUserRequest req)
    {
        var existing = GetById(id);
        if (existing is null) return null;

        // Extra validation: unique email for others
        if (_users.Any(u => u.Id != id && string.Equals(u.Email, req.Email, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Email already exists.");

        var updated = existing with
        {
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Email = req.Email.Trim(),
            IsActive = req.IsActive
        };

        var idx = _users.FindIndex(u => u.Id == id);
        _users[idx] = updated;
        return updated;
    }

    public bool Delete(int id)
    {
        var existing = GetById(id);
        if (existing is null) return false;
        _users.Remove(existing);
        return true;
    }
}
