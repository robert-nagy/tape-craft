using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace TapeCraft.IntegrationTests;

public static class Repo
{
    /// <summary>The folder that holds TapeCraft.slnx.</summary>
    public static string Root { get; } = Find(AppContext.BaseDirectory);

    private static string Find(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TapeCraft.slnx"))) return dir.FullName;
        throw new InvalidOperationException("TapeCraft.slnx not found above " + start);
    }
}

public static class Postgres
{
    public const string Image = "postgres:17-alpine";

    public static PostgreSqlContainer Create() => new PostgreSqlBuilder(Image).Build();

    /// <summary>The container's address as the DATABASE_URL the app expects.</summary>
    public static string DatabaseUrl(PostgreSqlContainer container) =>
        $"postgres://postgres:postgres@{container.Hostname}:{container.GetMappedPublicPort(5432)}/postgres";

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>The settings the web server needs for sign-in. The key ring goes to <paramref name="keysDirectory"/>.</summary>
public static class SignInSettings
{
    public static Dictionary<string, string> For(string keysDirectory) => new()
    {
        ["SPOTIFY_CLIENT_ID"] = "testclientid0123",
        ["SPOTIFY_REDIRECT_URI"] = "http://127.0.0.1:3000/auth/spotify/callback",
        ["DATA_PROTECTION_KEYS_DIR"] = keysDirectory,
    };
}

/// <summary>
/// The real app in memory, against the database at <paramref name="databaseUrl"/>, with its key ring in
/// <paramref name="keysDirectory"/> (a new temporary folder when null).
/// </summary>
public sealed class AppFactory(string databaseUrl, string? keysDirectory = null) : WebApplicationFactory<Program>
{
    private readonly string? _ownKeys = keysDirectory is null ? Directory.CreateTempSubdirectory("tape-craft-keys-").FullName : null;

    public string KeysDirectory => keysDirectory ?? _ownKeys!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", databaseUrl);
        foreach (var (name, value) in SignInSettings.For(KeysDirectory)) builder.UseSetting(name, value);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownKeys is not null) Directory.Delete(_ownKeys, recursive: true);
    }
}

/// <summary>The built app as its own process, as a host would run it.</summary>
public sealed class AppProcess : IAsyncDisposable
{
    private static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "TapeCraft.Api.dll");
    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private string? _keys;

    private AppProcess(Process process)
    {
        _process = process;
        process.OutputDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { lock (_output) _output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string Output { get { lock (_output) return _output.ToString(); } }

    public int? Port { get; private init; }

    public string Url => $"http://127.0.0.1:{Port}";

    public static AppProcess Start(string databaseUrl, int? port = null, params string[] args) => Start(databaseUrl, port, null, args);

    /// <summary>Starts the app with <paramref name="environment"/> applied last; a null value removes a variable.</summary>
    public static AppProcess Start(string databaseUrl, int? port, IReadOnlyDictionary<string, string?>? environment, params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.Combine(Repo.Root, "src", "TapeCraft.Api") };
        info.ArgumentList.Add(Dll);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["DATABASE_URL"] = databaseUrl;
        info.Environment["PORT"] = port?.ToString() ?? "0";
        info.Environment["HOST"] = "127.0.0.1";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        // Only the web server needs the sign-in settings, so `migrate` runs without them, as in production.
        string? keys = null;
        if (args.Length == 0)
        {
            keys = Directory.CreateTempSubdirectory("tape-craft-keys-").FullName;
            foreach (var (name, value) in SignInSettings.For(keys)) info.Environment[name] = value;
        }
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null) info.Environment.Remove(name);
            else info.Environment[name] = value;
        }
        return new AppProcess(Process.Start(info)!) { Port = port, _keys = keys };
    }

    /// <summary>Starts the web server and waits until it answers.</summary>
    public static async Task<AppProcess> StartServerAsync(string databaseUrl, CancellationToken cancellationToken)
    {
        var app = Start(databaseUrl, Postgres.FreePort());
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !app._process.HasExited)
        {
            try
            {
                await client.GetAsync(app.Url + "/manifest.webmanifest", cancellationToken);
                return app;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            await Task.Delay(100, cancellationToken);
        }
        await app.DisposeAsync();
        throw new InvalidOperationException("The app did not start:\n" + app.Output);
    }

    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        await _process.WaitForExitAsync(cancellation.Token);
        return _process.ExitCode;
    }

    /// <summary>Sends SIGTERM, as a container runtime does on stop.</summary>
    public void Terminate() => Process.Start("kill", ["-TERM", _process.Id.ToString()])!.WaitForExit();

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill();
        await _process.WaitForExitAsync();
        _process.Dispose();
        if (_keys is not null) Directory.Delete(_keys, recursive: true);
    }
}
