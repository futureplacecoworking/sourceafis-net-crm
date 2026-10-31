using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Futureplace.AgenteFS80H;

public sealed class ProtectedLocalStore(string directory) : ILocalStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Futureplace.AgenteFS80H.protocolo1");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string root = Path.GetFullPath(directory);
    private string Templates => Path.Combine(root, "templates");

    public AgentState LoadState() => File.Exists(Path.Combine(root, "state.dpapi")) ? Read<AgentState>(Path.Combine(root, "state.dpapi")) : new AgentState();
    public void SaveState(AgentState state) => Write(Path.Combine(root, "state.dpapi"), state);
    public IReadOnlyList<StoredTemplate> LoadTemplates()
    {
        if (!Directory.Exists(Templates)) return [];
        var result = new List<StoredTemplate>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Templates, "*.dpapi"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) throw new LocalDataException();
                var template = Read<StoredTemplate>(file);
                result.Add(template);
                // Arquivos da 1.3 têm só `template`; os da 1.4 guardam as outras leituras boas em `extraTemplates`.
                if (template.CadastroId != id || template.ComandoId == Guid.Empty || !ValidSample(template.Template) ||
                    template.ExtraTemplates is { } extras && (extras.Length > EnrollmentCommand.MaximumReadings - 1 || !extras.All(ValidSample)))
                    throw new LocalDataException();
            }
            return result;
        }
        catch
        {
            foreach (var value in result) value.Clear();
            throw;
        }
    }
    private static bool ValidSample(byte[]? sample) => sample is { Length: >= 1 and <= 1024 * 1024 };
    public IReadOnlyList<Guid> ListTemplateIds()
    {
        if (!Directory.Exists(Templates)) return [];
        // Inclui cópias .new de uma gravação interrompida: também devem ser apagadas antes de confirmar revogação.
        return Directory.EnumerateFiles(Templates).Select(file =>
        {
            var name = Path.GetFileName(file);
            if (name.Length < 38 || name[32..38] != ".dpapi" || !Guid.TryParseExact(name[..32], "N", out var id))
                throw new LocalDataException();
            return id;
        }).Distinct().ToArray();
    }
    public void DeleteAllTemplates()
    {
        if (Directory.Exists(Templates)) Directory.Delete(Templates, recursive: true);
    }
    public void SaveTemplate(StoredTemplate template)
    {
        if (template.CadastroId == Guid.Empty || template.ComandoId == Guid.Empty) throw new ArgumentException("Cadastro inválido.");
        Write(Path.Combine(Templates, template.CadastroId.ToString("N") + ".dpapi"), template);
    }
    public void DeleteTemplate(Guid id)
    {
        if (!Directory.Exists(Templates)) return;
        foreach (var file in Directory.EnumerateFiles(Templates, id.ToString("N") + ".dpapi*"))
            File.Delete(file);
    }
    public void Clear()
    {
        // O instalador oferece preservação explícita. Esta ação só é chamada após confirmação na UI.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
    private static T Read<T>(string path)
    {
        byte[]? plaintext = null;
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new LocalDataException();
            plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plaintext, Json) ?? throw new LocalDataException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            throw new LocalDataException();
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        string temporary = path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            var protectedData = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(protectedData);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
