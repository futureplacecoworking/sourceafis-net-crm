using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

// Media-only harness: render the released customer form with synthetic states.
// No AgentRuntime, local store, scanner, network or access permissions are used.
[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ClientVideoCapture
{
    private const int FramesPerSecond = 12;
    private const string ProductSourceCommit = "21d3ac6012f64ae8dc634d31488e1dfd85cb5064";

    [Test]
    public void CaptureCompleteClientDemo()
    {
        var output = Environment.GetEnvironmentVariable("FS80H_VIDEO_OUTPUT")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "client-demo");
        Directory.CreateDirectory(output);
        var scenarios = new[]
        {
            new Scene("preparing", "Preparando o leitor", AgentStatus.Connecting, "Conectando à Central.", 2),
            new Scene("ready", "Coloque o dedo no leitor", AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.", 3),
            new Scene("reading", "Leitura em andamento", AgentStatus.Capturing, "Capturando a digital.", 2.5),
            new Scene("lift", "Retire o dedo", AgentStatus.Capturing, "Retire o dedo completamente do leitor.", 2.5),
            new Scene("repeat", "Repita com o mesmo dedo", AgentStatus.Capturing, "Coloque o mesmo dedo novamente para confirmar.", 2.5),
            new Scene("authorized-morning", "Bom dia", AgentStatus.Authorized, "Autorizado pela Central.", 3, new DateTimeOffset(2026, 10, 30, 13, 0, 0, TimeSpan.Zero)),
            new Scene("authorized-afternoon", "Boa tarde", AgentStatus.Authorized, "Autorizado pela Central.", 3, new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero)),
            new Scene("authorized-evening", "Boa noite", AgentStatus.Authorized, "Autorizado pela Central.", 3, new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero)),
            new Scene("denied", "Acesso não autorizado", AgentStatus.Denied, "A Central não autorizou o acesso.", 3),
            new Scene("enrolled", "Digital cadastrada", AgentStatus.Ready, "Digital cadastrada na Central. O cadastro ainda não concede autorização de acesso.", 3),
            new Scene("unavailable", "Leitor indisponível", AgentStatus.Offline, "Sem conexão com a Central.", 3),
            new Scene("ready-again", "Pronto para a próxima pessoa", AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.", 2)
        };
        var manifest = new List<object>();
        var frame = 0;
        using var client = new ClientScreenForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = Point.Empty,
            ClientSize = new Size(960, 720)
        };
        try
        {
            client.Show();
            Application.DoEvents();
            var title = (Label)typeof(ClientScreenForm).GetField("title", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
            foreach (var scene in scenarios)
            {
                var snapshot = new AgentSnapshot(scene.Status, scene.Message);
                client.ShowSnapshot(snapshot);
                // Clock fixture only. Copy the greeting from the released presenter;
                // the form, all other labels and animation remain unchanged.
                if (scene.GreetingTime is { } time)
                    title.Text = ClientPresentation.From(snapshot, time).Title;
                Application.DoEvents();
                var first = frame;
                var count = (int)Math.Round(scene.Seconds * FramesPerSecond);
                var elapsed = Stopwatch.StartNew();
                for (var i = 0; i < count; i++)
                {
                    var due = TimeSpan.FromSeconds((double)i / FramesPerSecond);
                    while (elapsed.Elapsed < due)
                    {
                        Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    Application.DoEvents();
                    using var bitmap = new Bitmap(client.Width, client.Height);
                    client.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, $"frame-{frame:00000}.png"), ImageFormat.Png);
                    frame++;
                }
                manifest.Add(new
                {
                    scenario = scene.Name,
                    label = scene.Label,
                    title = title.Text,
                    startFrame = first,
                    endFrame = frame - 1,
                    duration = (double)count / FramesPerSecond
                });
                TestContext.Progress.WriteLine($"Captured {scene.Name}: frames {first}–{frame - 1}; title={title.Text}");
            }
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                sourceCommit = ProductSourceCommit,
                captureCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                version = "1.3.0",
                width = 960,
                height = 720,
                fps = FramesPerSecond,
                totalFrames = frame,
                duration = (double)frame / FramesPerSecond,
                syntheticStates = true,
                scenarios = manifest
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            client.Close();
            Application.DoEvents();
        }
    }

    private sealed record Scene(string Name, string Label, AgentStatus Status, string Message,
        double Seconds, DateTimeOffset? GreetingTime = null);
}
