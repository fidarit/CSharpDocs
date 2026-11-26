# CSharpDocs

A documentation web server for .NET assemblies. It uses the compiler-generated
XML documentation (`<Assembly>.xml`) together with the corresponding assembly
(`.dll`).

## Features

- API tree (namespaces → types → members) with a page for each item.
- Sidebar search by name and description, including `*` and `?` wildcards, while preserving the hierarchy.
- Markdown comments (no authentication required) and an `/all` summary page.

## Data sources

| Data | Source |
| --- | --- |
| Descriptions, parameters, and exceptions | `<Assembly>.xml` |
| Inheritance, kind, nesting, and visibility | `<Assembly>.dll` metadata (`System.Reflection.Metadata`) |
| Extension methods | `[Extension]` attribute (`this` modifier) |
| Assembly/package metadata (`nuspec`) | `AssemblyDefinition`, `.nuspec` |

## Run

```bash
dotnet run --project CSharpDocs -- -xml path/to/MyLibrary.xml
dotnet run --project CSharpDocs -- -nupkg path/to/mylib.1.2.3.nupkg
API_PACKAGE=MyLibrary dotnet run --project CSharpDocs   # find the latest package in the NuGet cache
```

### Options

| Flag | Default | Description |
| --- | --- | --- |
| `-urls` | `http://localhost:8080` | Listening address (or `ASPNETCORE_URLS`) |
| `-xml` | — | XML documentation; the DLL is searched for alongside it |
| `-dll` | Next to `-xml` | Assembly file |
| `-nupkg` | — | `.nupkg`; XML and DLL are read from `lib/<tfm>/` |
| `-package` | `API_PACKAGE` | Package ID to search for in the NuGet cache |
| `-title` | `API Docs` | Site title |
| `-version` | Detected from source path | Displayed version |
| `-db` | `comments.db` | SQLite database for comments |
| `-mcp` | `false` | Enables the Streamable HTTP MCP endpoint at `/mcp` |
| `-mcp-read-only` | `false` | Enables read-only mode for MCP |

When MCP is enabled, it supports searching documentation and comments, reading
an item with its comments, and reading comments separately. Adding comments is
available unless MCP is running in read-only mode. For example:
`-mcp -mcp-read-only`.

## Build

```bash
dotnet publish CSharpDocs -c Release -r linux-x64
```

The Native AOT toolchain is required: `clang`, `zlib1g-dev`.

### Docker

```bash
docker build -t csharpdocs .

docker run --rm -p 8080:8080 -e API_PACKAGE=MyLibrary \
    -v csharpdocs-data:/data csharpdocs

docker run --rm -p 8080:8080 \
    -e MCP_ENABLED=true -e MCP_READ_ONLY=true \
    -v "$PWD:/packages:ro" -v csharpdocs-data:/data \
    csharpdocs -nupkg /packages/MyLibrary.1.2.3.nupkg -db /data/comments.db

docker run --rm -p 8080:8080 \
    -v "$PWD:/src:ro" -v csharpdocs-data:/data \
    csharpdocs -xml /src/MyLibrary.xml -db /data/comments.db
```

### Security and data

- Comments are available without authentication and are stored with author names in SQLite (`/data/comments.db` in Docker).
- Comment editing and deletion are disabled by default. Set `COMMENTS_ADMIN_KEY` to enable them. The UI prompts for this shared key; deletion also requires browser confirmation. Keep the key secret and use HTTPS.
- MCP has no built-in authentication either. Expose the service only behind a TLS reverse proxy with access control; `MCP_READ_ONLY=true` disables writes through MCP.

## Tests

```bash
CSHARPDOCS_XML=path/to/MyLibrary.xml dotnet test CSharpDocs.Tests
```

Set `CSHARPDOCS_XML` to a sample assembly's XML documentation file (the DLL is
searched for alongside it). Model tests are skipped when no sample is provided.

## License

CSharpDocs is licensed under the [MIT License](LICENSE).
