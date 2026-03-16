# CheckPhotoList

Checks whether files from a photographer's delivery manifest have made it into an S3 bucket.

The photographer delivers files in batches and provides `ls -al` text file manifests. This tool reads those manifests, extracts filenames, and compares them against S3. TIF files are expected at the bucket root; JPG files are expected under the `jpg/` prefix.

## Setup

Copy `appsettings.template.json` to `appsettings.json` and fill in your values:

```json
{
  "Settings": {
    "ManifestDirectory": "path/to/manifest/directory",
    "S3Bucket": "your-s3-bucket-name",
    "Region": "us-east-1"
  }
}
```

AWS credentials are read from the standard credential chain (environment variables, `~/.aws/credentials`, IAM role, etc.).

## Usage

```bash
# Check manifests against S3 (uses ManifestDirectory from appsettings.json)
dotnet run

# Override manifest directory
dotnet run -- <path/to/manifests>

# List files at S3 bucket root (shows sub-prefixes in brackets, then files)
dotnet run -- list

# List files under a specific prefix
dotnet run -- list /jpg
```

### list command output example

```
S3 bucket: keithlong-art-photos  prefix: (root)
  1 prefix(es), 856 file(s)

  [jpg/]
  _S2A2205_1992W14.tif
  _U1A3422_2006W8.tif
  ...
```

```
S3 bucket: keithlong-art-photos  prefix: jpg/
  0 prefix(es), 412 file(s)

  _S2A2205_1992W14.jpg
  _U1A3422_2006W8.jpg
  ...
```

The list command uses S3's delimiter-based listing, so it only shows files at the requested level — sub-prefixes are shown in `[brackets]` rather than expanding their contents.

## Manifest format

Each `.txt` file in the manifest directory should contain `ls -al` output, e.g.:

```
-rw-r--r--  1 user group  45231234 Mar 15 23:25 _S2A2205_1992W14.tif
-rw-r--r--  1 user group   1823456 Jan  5 23:22 _U1A3422_2006W8.jpg
```

Lines that don't start with `-` or have fewer than 9 tokens are skipped.

## Output example

```
Reading manifests from: D:\...\filenamefiles
  20 files read
  412 tif filenames parsed
  389 jpg filenames parsed

Reading S3 bucket: keithlong-art-photos
  Root:  856 objects
  jpg/:  412 objects

═══ TIF Results ════════════════════════════════════════════
  In S3    : 398
  Missing  : 14

TIF files missing from S3:
  - _S2A2205_1992W14.tif  (Mar 15 23:25)

═══ JPG Results ════════════════════════════════════════════
  In S3    : 385
  Missing  : 4

JPG files missing from S3 jpg/:
  - _U1A3422_2006W8.jpg  (Jan 5 23:22)
```

Files with extensions other than `.tif` or `.jpg` are reported separately at the end but not checked against S3.

## Requirements

- .NET 10.0
- AWS credentials with `s3:ListBucket` permission on the target bucket
