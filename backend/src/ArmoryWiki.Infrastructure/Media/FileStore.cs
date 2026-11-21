namespace ArmoryWiki.Infrastructure.Media;

/// <summary>
/// Image files on disk (a Docker volume in production), one folder per content hash:
/// <c>{root}/{hash}/original.jpg</c>, <c>medium.webp</c>, <c>thumb.webp</c>. Served at <c>/media/{hash}/{file}</c>.
/// A hash folder only ever appears complete: files are written to a private temp folder that is then renamed.
/// </summary>
// ponytail: local disk only; moving to S3-compatible storage means changing this one class.
public sealed class FileStore(string root)
{
    public bool Has(string hash) => Directory.Exists(Folder(hash));

    public async Task WriteAsync(string hash, IReadOnlyDictionary<string, byte[]> files, CancellationToken ct)
    {
        var final = Folder(hash);
        var temp = Path.Combine(root, $".tmp-{hash}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var (name, content) in files)
            {
                await File.WriteAllBytesAsync(Path.Combine(temp, name), content, ct);
            }

            if (!Directory.Exists(final))
            {
                Directory.Move(temp, final);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true); // failed, or another writer finished first
            }
        }
    }

    public void Delete(string hash)
    {
        if (Has(hash))
        {
            Directory.Delete(Folder(hash), recursive: true);
        }
    }

    private string Folder(string hash) =>
        hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower)
            ? Path.Combine(root, hash)
            : throw new ArgumentException("Not a SHA-256 hex hash.", nameof(hash)); // never let a value escape the root
}
