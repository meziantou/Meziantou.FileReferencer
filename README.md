# Meziantou.FileReferencer

A tool to automatically insert and update file references in your source code. It supports both local and remote files (HTTP/HTTPS URLs).

## Installation

````bash
dotnet tool update Meziantou.FileReferencer --global
````

## Usage

### Basic Usage

Run the tool on a file or directory:

````bash
# Update a single file
Meziantou.FileReferencer myfile.cs

# Update all files in a directory (recursive by default)
Meziantou.FileReferencer ./src

# Update without recursing into subdirectories
Meziantou.FileReferencer ./src --recurse false
````

### Adding a Reference in Your Files

To include external file content in your code, add reference markers in comments. The tool will automatically download and insert the content between the start and end markers.

**Example in C#:**

````csharp
// ref:https://example.com/LICENSE.txt
// endref
````

After running the tool:

````csharp
// ref:https://example.com/LICENSE.txt
MIT License
...
// endref
````

**Example with local file:**

````csharp
// ref:./shared/config.json
// endref
````

### Reference Options

You can customize how references are inserted using options after the reference path:

````csharp
// ref:https://example.com/file.txt;indent=false;eol=lf
// endref
````

Available options:
- `indent=false` - Disable automatic indentation matching
- `eol=lf|crlf|cr|auto` - Control line ending format
  - `lf` - Line Feed (Unix/Linux)
  - `crlf` - Carriage Return + Line Feed (Windows)
  - `cr` - Carriage Return
  - `auto` - Match the line endings of the current file (default)

### FileReferences.json

You can also define references in a `FileReferences.json` file to automatically create or update entire files:

````json
{
  "references": {
    "LICENSE.txt": {
      "ref": "https://raw.githubusercontent.com/user/repo/main/LICENSE.txt"
    },
    "local-copy.txt": {
      "ref": "./source/file.txt"
    }
  }
}
````

## Supported File Formats

The tool supports multiple comment styles to work with various file types:

### Double Slash Comments (`//`)
Used in: `.cs`, `.js`, `.ts`, `.json`, `.json5`, `.less`, `.scss`

````csharp
// ref:file.txt
// endref
````

### Slash-Star Comments (`/* */`)
Used in: `.cs`, `.css`, `.js`, `.ts`, `.less`, `.scss`

````css
/* ref:file.txt */
/* endref */
````

### HTML/XML Comments (`<!-- -->`)
Used in: `.htm`, `.html`, `.xml`, `.md`

````html
<!-- ref:file.txt -->
<!-- endref -->
````

### Hash Comments (`#`)
Used in: `.sh`, `.yaml`, `.yml`, `.editorconfig`, `dockerfile`

````bash
# ref:file.txt
# endref
````

### SQL Comments (`--`)
Used in: `.sql`

````sql
-- ref:file.txt
-- endref
````

### Semicolon Comments (`;`)
Used in: `.ini`

````ini
; ref:file.txt
; endref
````

### C# Regions (`#region`/`#endregion`)
Used in: `.cs`

````csharp
#region ref:file.txt
#endregion
````

### Mixed Format Support

For files without a specific extension or generic text files, the tool attempts to match any of the supported comment styles.

## Integration with Renovate

[Renovate](https://docs.renovatebot.com/) can automatically detect and update version tags in file references. Combined with this tool, you can keep your referenced files up-to-date with the latest versions.

### How It Works

1. Renovate detects version tags in your file references (e.g., `https://github.com/user/repo/blob/v1.2.3/file.txt`)
2. When Renovate updates the version tag in a pull request, it creates a new commit
3. Run `Meziantou.FileReferencer` in your CI/CD pipeline to automatically update the referenced content
4. The tool downloads the new version and updates the content between the reference markers

### Example Configuration

Consider a file with a versioned reference:

````csharp
// ref:https://github.com/meziantou/SampleProject/blob/1.2.3/.editorconfig
// endref
````

To enable Renovate to detect and update this reference, add a custom manager to your `renovate.json`:

````json
{
  "extends": ["config:base"],
  "customManagers": [
    {
      "customType": "regex",
      "fileMatch": ["\\.(cs|js|ts|py|md|yml|yaml)$"],
      "matchStrings": [
        "ref:https://github\\.com/(?<depName>[^/]+/[^/]+)/blob/(?<currentValue>[^/]+)/"
      ],
      "datasourceTemplate": "github-tags",
      "versioningTemplate": "semver"
    }
  ]
}
````

This configuration:
- Searches for references matching the pattern in your source files
- Extracts the repository name and version tag
- Uses GitHub tags as the data source to check for updates
- Applies semantic versioning rules

### Running in CI/CD

To automatically update referenced content after Renovate creates a pull request, add this tool to your CI workflow:

````yaml
name: Update File References

on:
  pull_request:
    branches: [main]

jobs:
  update-references:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ github.head_ref }}
          token: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Install Meziantou.FileReferencer
        run: dotnet tool install --global Meziantou.FileReferencer
      
      - name: Update file references
        run: Meziantou.FileReferencer .
      
      - name: Commit updated references
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add .
          git diff --staged --quiet || git commit -m "Update file references"
          git push
````

This ensures that whenever Renovate updates a version tag, the actual file content is automatically refreshed to match the new version.