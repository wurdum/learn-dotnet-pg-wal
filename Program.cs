using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationContext>(options => options
    .UseNpgsql("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres")
    .UseSnakeCaseNamingConvention());

builder.Services.AddHostedService<Biography>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();

public class LifeEvent
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required bool Positive { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
}

public class ApplicationContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<LifeEvent> LifeEvents => Set<LifeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LifeEvent>(b =>
        {
            b.HasKey(e => e.Id);
        });
    }
}

public class Biography(IServiceProvider serviceProvider) : BackgroundService
{
    private static readonly Random Random = new();
    private static readonly IReadOnlyList<string> LifeEvents = ["Born", "Married", "Divorced", "Died"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        await context.Database.EnsureCreatedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var lifeEvent = new LifeEvent
            {
                Id = Guid.NewGuid(),
                Name = LifeEvents[Random.Next(LifeEvents.Count)],
                Positive = true,
                Timestamp = DateTimeOffset.UtcNow
            };

            context.LifeEvents.Add(lifeEvent);
            await context.SaveChangesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
