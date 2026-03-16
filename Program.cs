using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

class Program
{
  static async Task Main(string[] args)
  {
    var configuration = new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile("appsettings.json")
      .Build();

    var manifestDir = configuration["Settings:ManifestDirectory"] ?? "";
    var s3Bucket = configuration["Settings:S3Bucket"] ?? "";
    var region = configuration["Settings:Region"] ?? "us-east-1";

    // list [/prefix] subcommand
    if (args.Length >= 1 && args[0] == "list")
    {
      var rawPrefix = args.Length >= 2 ? args[1] : "";
      // Normalize: strip leading slash, ensure trailing slash if non-empty
      var prefix = rawPrefix.TrimStart('/');
      if (prefix.Length > 0 && !prefix.EndsWith("/"))
        prefix += "/";

      try
      {
        var s3Client = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));
        var (files, subPrefixes) = await ListS3Prefix(s3Client, s3Bucket, prefix);
        var display = prefix.Length > 0 ? prefix : "(root)";
        Console.WriteLine($"S3 bucket: {s3Bucket}  prefix: {display}");
        Console.WriteLine($"  {subPrefixes.Count} prefix(es), {files.Count} file(s)");
        Console.WriteLine();
        foreach (var p in subPrefixes.OrderBy(p => p))
          Console.WriteLine($"  [{p}]");
        foreach (var f in files.OrderBy(f => f))
          Console.WriteLine($"  {f}");
      }
      catch (AmazonS3Exception ex)
      {
        Console.WriteLine($"✗ AWS S3 Error: {ex.Message}");
        Console.WriteLine($"  Error Code: {ex.ErrorCode}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"✗ Error: {ex.Message}");
      }
      return;
    }

    // Allow manifest directory override from first positional arg
    if (args.Length >= 1)
      manifestDir = args[0];

    Console.WriteLine($"Reading manifests from: {manifestDir}");

    if (!Directory.Exists(manifestDir))
    {
      Console.WriteLine($"✗ Manifest directory does not exist: {manifestDir}");
      return;
    }

    // --- Parse manifest files ---
    var txtFiles = Directory.GetFiles(manifestDir, "*.txt");
    Console.WriteLine($"  {txtFiles.Length} files read");

    var tifFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // filename → date
    var jpgFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // filename → date
    var otherExtensions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    int skippedLines = 0;

    foreach (var txtFile in txtFiles)
    {
      var lines = await File.ReadAllLinesAsync(txtFile);
      foreach (var line in lines)
      {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Skip malformed lines: need at least 9 tokens, first token must start with '-'
        if (tokens.Length < 9 || !tokens[0].StartsWith("-"))
        {
          skippedLines++;
          continue;
        }

        var filename = tokens[8];
        var date = $"{tokens[5]} {tokens[6]} {tokens[7]}";
        var ext = Path.GetExtension(filename).ToLowerInvariant();

        if (ext == ".tif")
          tifFiles[filename] = date;
        else if (ext == ".jpg")
          jpgFiles[filename] = date;
        else
        {
          if (!otherExtensions.ContainsKey(ext))
            otherExtensions[ext] = new List<string>();
          otherExtensions[ext].Add(filename);
        }
      }
    }

    Console.WriteLine($"  {tifFiles.Count} tif filenames parsed");
    Console.WriteLine($"  {jpgFiles.Count} jpg filenames parsed");
    Console.WriteLine();

    try
    {
      var s3Client = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));

      Console.WriteLine($"Reading S3 bucket: {s3Bucket}");

      // Load root objects (for TIF check)
      var s3RootFiles = await ListS3Objects(s3Client, s3Bucket, "");
      Console.WriteLine($"  Root:  {s3RootFiles.Count} objects");

      // Load jpg/ prefix objects (for JPG check)
      var s3JpgFiles = await ListS3Objects(s3Client, s3Bucket, "jpg/");
      Console.WriteLine($"  jpg/:  {s3JpgFiles.Count} objects");

      Console.WriteLine();

      // --- TIF Results ---
      var tifMissing = tifFiles
        .Where(kv => !s3RootFiles.Contains(kv.Key))
        .OrderBy(kv => kv.Key)
        .ToList();
      var tifFound = tifFiles.Count - tifMissing.Count;

      Console.WriteLine("═══ TIF Results ════════════════════════════════════════════");
      Console.WriteLine($"  In S3    : {tifFound}");
      Console.WriteLine($"  Missing  : {tifMissing.Count}");
      Console.WriteLine();

      if (tifMissing.Count > 0)
      {
        Console.WriteLine("TIF files missing from S3:");
        foreach (var kv in tifMissing)
          Console.WriteLine($"  - {kv.Key}  ({kv.Value})");
        Console.WriteLine();
      }

      // --- JPG Results ---
      var jpgMissing = jpgFiles
        .Where(kv => !s3JpgFiles.Contains(kv.Key))
        .OrderBy(kv => kv.Key)
        .ToList();
      var jpgFound = jpgFiles.Count - jpgMissing.Count;

      Console.WriteLine("═══ JPG Results ════════════════════════════════════════════");
      Console.WriteLine($"  In S3    : {jpgFound}");
      Console.WriteLine($"  Missing  : {jpgMissing.Count}");
      Console.WriteLine();

      if (jpgMissing.Count > 0)
      {
        Console.WriteLine("JPG files missing from S3 jpg/:");
        foreach (var kv in jpgMissing)
          Console.WriteLine($"  - {kv.Key}  ({kv.Value})");
        Console.WriteLine();
      }

      // --- Other extensions ---
      if (otherExtensions.Count > 0)
      {
        Console.WriteLine("═══ Other Extensions Found (not checked) ══════════════════");
        foreach (var kv in otherExtensions.OrderBy(kv => kv.Key))
        {
          var label = kv.Value.Count == 1 ? "file" : "files";
          Console.WriteLine($"  {kv.Key,-6}: {kv.Value.Count} {label}");
          foreach (var f in kv.Value.OrderBy(f => f))
            Console.WriteLine($"    - {f}");
        }
        Console.WriteLine();
      }

      if (skippedLines > 0)
        Console.WriteLine($"(Skipped {skippedLines} malformed lines)");
    }
    catch (AmazonS3Exception ex)
    {
      Console.WriteLine($"✗ AWS S3 Error: {ex.Message}");
      Console.WriteLine($"  Error Code: {ex.ErrorCode}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"✗ Error: {ex.Message}");
    }
  }

  // Returns only files at this exact level (delimiter="/"), no recursion into sub-prefixes
  static async Task<HashSet<string>> ListS3Objects(AmazonS3Client s3Client, string bucket, string prefix)
  {
    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var request = new ListObjectsV2Request
    {
      BucketName = bucket,
      Prefix = prefix,
      Delimiter = "/"
    };

    ListObjectsV2Response response;
    do
    {
      response = await s3Client.ListObjectsV2Async(request);
      foreach (var obj in response.S3Objects)
      {
        var key = obj.Key;
        if (key.StartsWith(prefix))
          key = key.Substring(prefix.Length);
        if (!string.IsNullOrEmpty(key) && !key.EndsWith("/"))
          files.Add(key);
      }
      request.ContinuationToken = response.NextContinuationToken;
    } while (response.IsTruncated == true);

    return files;
  }

  // Like ListS3Objects but also returns common prefixes (sub-"directories") for display
  static async Task<(HashSet<string> files, List<string> subPrefixes)> ListS3Prefix(AmazonS3Client s3Client, string bucket, string prefix)
  {
    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var subPrefixes = new List<string>();
    var request = new ListObjectsV2Request
    {
      BucketName = bucket,
      Prefix = prefix,
      Delimiter = "/"
    };

    ListObjectsV2Response response;
    do
    {
      response = await s3Client.ListObjectsV2Async(request);
      foreach (var obj in response.S3Objects)
      {
        var key = obj.Key;
        if (key.StartsWith(prefix))
          key = key.Substring(prefix.Length);
        if (!string.IsNullOrEmpty(key) && !key.EndsWith("/"))
          files.Add(key);
      }
      foreach (var cp in response.CommonPrefixes ?? [])
        subPrefixes.Add(cp);
      request.ContinuationToken = response.NextContinuationToken;
    } while (response.IsTruncated == true);

    return (files, subPrefixes);
  }
}
