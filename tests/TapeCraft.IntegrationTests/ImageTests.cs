using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

public sealed class ImageTests : IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly List<IAsyncDisposable> _started = [];
    private IFutureDockerImage _image = null!;
    private PostgreSqlContainer _database = null!;

    private const string DatabaseUrl = "postgres://postgres:postgres@database:5432/postgres";

    public async ValueTask InitializeAsync()
    {
        await _network.CreateAsync();
        _database = new PostgreSqlBuilder(Postgres.Image).WithNetwork(_network).WithNetworkAliases("database").Build();
        _started.Add(_database);
        // A new tag each run, so that a failed build cannot fall back on an older image.
        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Repo.Root)
            .WithDockerfile("Dockerfile")
            .WithName($"localhost/tape-craft:test-{Guid.NewGuid():N}")
            .WithCleanUp(true)
            .Build();
        _started.Add(_image);
        await Task.WhenAll(_database.StartAsync(), _image.CreateAsync());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var resource in Enumerable.Reverse(_started)) await resource.DisposeAsync();
        await _network.DisposeAsync();
    }

    private IContainer Container(params string[] command)
    {
        var builder = new ContainerBuilder(_image).WithNetwork(_network).WithEnvironment("DATABASE_URL", DatabaseUrl);
        if (command.Length > 0) builder = builder.WithCommand(command);
        // Only the web server needs the sign-in settings. /keys is where the README mounts the key volume.
        else foreach (var (name, value) in SignInSettings.For("/keys")) builder = builder.WithEnvironment(name, value);
        var container = builder
            .WithPortBinding(3000, assignRandomHostPort: true)
            .WithWaitStrategy(command.Length > 0
                ? Wait.ForUnixContainer()
                : Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPath("/healthz").ForPort(3000)))
            .Build();
        _started.Add(container);
        return container;
    }

    [Fact]
    public async Task Runs_the_migrations_and_serves_the_app_configured_only_by_environment_variables()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var migrate = Container("migrate");
        await migrate.StartAsync(cancellation);
        Assert.Equal(0, await migrate.GetExitCodeAsync(cancellation));

        var app = Container();
        await app.StartAsync(cancellation);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://{app.Hostname}:{app.GetMappedPublicPort(3000)}"),
        };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz", cancellation)).StatusCode);

        // Signing in writes the key ring, so the app user must be able to write to /keys.
        var signIn = await client.GetAsync("/auth/spotify/sign-in", cancellation);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        var keys = await app.ExecAsync(["ls", "/keys"], cancellation);
        Assert.Equal(0, keys.ExitCode);
        Assert.StartsWith("key-", keys.Stdout);
    }
}
