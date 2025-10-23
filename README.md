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
Used in: `.css`, `.js`, `.ts`, `.less`, `.scss`

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