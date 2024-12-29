using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationContext>((sp, options) => options
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddHostedService<Biography>();
builder.Services.AddHostedService<WalListener>();

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

public class WalListener(IConfiguration configuration, ILogger<WalListener> logger) : BackgroundService
{
    private const string ReplicationSlotName = "wal_listener";
    private const string PublicationName = "life_events";
    private const string TableName = "life_events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var connectionString = configuration.GetConnectionString("Postgres")!;

        await EnsurePublication(connectionString, stoppingToken);
        await EnsureReplicationSlot(connectionString, stoppingToken);

        await using var connection = new LogicalReplicationConnection(connectionString);
        await connection.Open(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var message in connection.StartReplication(
                   new PgOutputReplicationSlot(ReplicationSlotName),
                   new PgOutputReplicationOptions(PublicationName, PgOutputProtocolVersion.V3),
                   stoppingToken))
                {
                    if (message is InsertMessage insert)
                    {
                        var record = new Dictionary<string, object?>();

                        var columnIndex = 0;
                        await foreach (var replicationValue in insert.NewRow)
                        {
                            var columnName = insert.Relation.Columns[columnIndex++].ColumnName;
                            var value = await replicationValue.Get(stoppingToken);
                            record[columnName] = replicationValue.IsDBNull ? null : value;
                        }

                        logger.LogInformation(JsonSerializer.Serialize(record));
                    }

                    connection.SetReplicationStatus(message.WalEnd);
                }
            }
            catch (PostgresException exception) when (exception.SqlState == "55006") // Replication slot is active.
            {
                logger.LogWarning("Replication slot is active");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task EnsureReplicationSlot(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            do $$
            begin
                if not exists (
                    select 1
                    from pg_replication_slots
                    where slot_name = '{ReplicationSlotName}'
                ) then
                    perform pg_create_logical_replication_slot('{ReplicationSlotName}', 'pgoutput');
                end if;
            end
            $$;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsurePublication(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = $"""
            do $$
            begin
                if not exists (
                    select 1
                    from pg_publication
                    where pubname = '{PublicationName}'
                ) then
                    create publication {PublicationName}
                        for table public.{TableName};
                end if;
            end
            $$;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
