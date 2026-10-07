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
- `trim-final-lines=false` - Keep the empty lines at the end of the referenced content
- `lines=12-14` or `lines=12` - Only include the specified lines (inclusive, 1-based)
- `region=Name` - Only include the content of the named region (see below)
- `dedent=true` - Remove the leading whitespace common to all the included lines, while preserving relative indentation
- `format=md-fenced-code-block` - Wrap the content in a Markdown fenced code block (see below)
- `language=csharp` - Set the language of the fenced code block (default: inferred from the file extension; `none` to omit it)
- `source-link=auto|<url>` - Add a `[source code](...)` link after the content (see below)

If a reference is invalid (e.g. unknown option value, out-of-bounds line range, missing or ambiguous region, unresolvable source link), the tool reports an error, leaves the existing content of this reference unchanged, and exits with a non-zero exit code.

### Markdown Code Snippets

In Markdown files, you can embed code from tests or sample projects in a fenced code block. The tool generates and refreshes the code fence, so you don't need to maintain it manually:

````markdown
<!-- ref:../tests/ExampleTests.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true;source-link=auto -->
<!-- endref -->
````

After running the tool:

`````markdown
<!-- ref:../tests/ExampleTests.cs;region=BasicUsage;format=md-fenced-code-block;dedent=true;source-link=auto -->
```csharp
var value = 42;
Console.WriteLine(value);
```

[source code](https://github.com/owner/repo/blob/main/tests/ExampleTests.cs#L12-L13)
<!-- endref -->
`````

- Without `lines` or `region`, the whole file is included.
- `lines=12-14` selects an explicit range of lines. It cannot be combined with `region`.
- `region=BasicUsage` selects the lines between `#region BasicUsage` and the matching `#endregion`, excluding these delimiters. Nested regions are supported. The region name must be unique in the file.
- The language is inferred from the file extension (e.g. `.cs` → `csharp`). Unknown extensions produce a code block without language. Use `language=<name>` to override it, or `language=none` to omit it.
- The fence uses more backticks than the longest run of backticks in the content, so the content can itself contain code fences.
- The indentation of the start marker is applied to the whole generated block, so snippets can be nested in lists.

#### Source links

`source-link` adds a `[source code](...)` link after the code block (separated by an empty line). There is no link when the option is omitted.

- `source-link=https://...` uses the URL as-is.
- `source-link=auto` generates a link to GitHub:
  - For local files, the link targets the GitHub repository of the `origin` remote of the Git repository containing the referenced file, on its **default branch**. The default branch is read from `origin/HEAD` (`git remote set-head origin --auto`) or queried from the remote. Note that these links are live links: local changes that are not yet merged in the default branch may not be visible on GitHub.
  - For remote files (`https://github.com/{owner}/{repo}/blob/...`, `https://github.com/{owner}/{repo}/raw/...`, or `https://raw.githubusercontent.com/...`), the link targets the same repository, revision, and path.
  - When using `lines` or `region`, the link points to the selected lines (e.g. `#L12-L14`) in the original file.
  - The tool reports an error if the link cannot be resolved (e.g. not a Git repository, no `origin` remote, or not a GitHub repository).

The `format`, `lines`, `region`, `dedent`, and `source-link` options are only supported in reference markers. Files defined in `FileReferences.json` are copied as-is.

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

### Running with Renovate postUpgradeTasks

The recommended approach is to use Renovate's [`postUpgradeTasks`](https://docs.renovatebot.com/configuration-options/#postupgradetasks) feature to automatically run the tool after updating references. This keeps all automation within Renovate's workflow:

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
  ],
  "postUpgradeTasks": {
    "commands": [
      "dotnet tool install --global Meziantou.FileReferencer",
      "Meziantou.FileReferencer ."
    ],
    "fileFilters": ["**/*"],
    "executionMode": "update"
  }
}
````

This configuration:
- Installs the tool when processing updates
- Runs it to refresh referenced file content
- Includes all updated files in the commit
- Only executes during dependency updates (not on initial PR creation)

### Alternative: Running in CI/CD

Alternatively, you can run the tool in a separate CI workflow after Renovate creates a pull request:

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

Both approaches ensure that whenever Renovate updates a version tag, the actual file content is automatically refreshed to match the new version.