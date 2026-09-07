using System.Diagnostics;

namespace EndToEnd.Tests.Support;

/// <summary>
/// Shell-out fino para <c>docker compose stop/start &lt;serviço&gt;</c>, usado pelos testes de
/// resiliência/isolamento (issue #19) para simular um container real fora do ar — nunca mock de
/// exceção nem chaos engineering (Toxiproxy/Testcontainers ficaram de fora por decisão da issue).
/// Opera sobre os containers de longa duração já subidos via <c>docker compose up -d</c> na raiz
/// do repo (mesma infra da issue #18): não sobe nem derruba o ambiente inteiro.
/// </summary>
internal static class DockerCompose
{
    private static readonly string ProjectRoot = LocalizarRaizDoRepositorio();

    public static Task StopAsync(string servico) => ExecutarAsync("stop", servico);

    public static Task StartAsync(string servico) => ExecutarAsync("start", servico);

    private static async Task ExecutarAsync(string comando, string servico)
    {
        using var processo = new Process
        {
            StartInfo = new ProcessStartInfo("docker", $"compose {comando} {servico}")
            {
                WorkingDirectory = ProjectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        processo.Start();
        var stdout = await processo.StandardOutput.ReadToEndAsync();
        var stderr = await processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();

        if (processo.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'docker compose {comando} {servico}' falhou (exit code {processo.ExitCode}).\n" +
                $"stdout: {stdout}\nstderr: {stderr}");
        }
    }

    /// <summary>
    /// Sobe a partir de <see cref="AppContext.BaseDirectory"/> (dentro de bin/Debug/...) até achar
    /// o <c>docker-compose.yml</c> na raiz do repo — evita fixar um caminho relativo que quebraria
    /// dependendo de como/de onde <c>dotnet test</c> é invocado.
    /// </summary>
    private static string LocalizarRaizDoRepositorio()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "docker-compose.yml")))
        {
            diretorio = diretorio.Parent;
        }

        return diretorio?.FullName
            ?? throw new InvalidOperationException(
                $"docker-compose.yml não encontrado subindo a partir de {AppContext.BaseDirectory}.");
    }
}
