# CSharpDocs

Веб-сервер документации .NET-сборок. Работает с XML-документацией компилятора
(`<Assembly>.xml`) и соответствующей сборкой (`<Assembly>.dll`).

## Возможности

- Дерево API (пространства имён → типы → члены) и страница на каждый элемент.
- Поиск в панели: по имени и описанию, `*` и `?`, с сохранением иерархии.
- Комментарии (Markdown, без авторизации) и сводная страница `/all`.

## Источники данных

| Данные | Источник |
| --- | --- |
| Описания, параметры, исключения | `<Assembly>.xml` |
| Наследование, вид, вложенность, доступность | метаданные `<Assembly>.dll` (`System.Reflection.Metadata`) |
| Методы-расширения | атрибут `[Extension]` (модификатор `this`) |
| Метаданные сборки/пакета (`nuspec`) | `AssemblyDefinition`, `.nuspec` |

## Запуск

```bash
dotnet run --project CSharpDocs -- -xml path/to/MyLibrary.xml
dotnet run --project CSharpDocs -- -nupkg path/to/mylib.1.2.3.nupkg
API_PACKAGE=MyLibrary dotnet run --project CSharpDocs   # поиск последнего пакета в кэше NuGet
```

### Параметры

| Флаг | По умолчанию | Назначение |
| --- | --- | --- |
| `-urls` | `http://localhost:8080` | адрес прослушивания (или `ASPNETCORE_URLS`) |
| `-xml` | — | XML-документация; DLL берётся рядом |
| `-dll` | рядом с `-xml` | сборка |
| `-nupkg` | — | `.nupkg`; XML и DLL берутся из `lib/<tfm>/` |
| `-package` | `API_PACKAGE` | id пакета для поиска в кэше NuGet |
| `-title` | `API Docs` | заголовок сайта |
| `-version` | из пути источника | отображаемая версия |
| `-db` | `comments.db` | файл SQLite с комментариями |
| `-mcp` | `false` | включает MCP endpoint `/mcp` по Streamable HTTP |
| `-mcp-read-only` | `false` | в режиме MCP оставляет только чтение |

При включённом MCP доступны поиск документации и комментариев, чтение элемента
вместе с комментариями и чтение комментариев отдельно. Добавление комментариев
доступно, если MCP не работает в режиме только для чтения. Например:
`-mcp -mcp-read-only`.

## Сборка

```bash
dotnet publish CSharpDocs -c Release -r linux-x64
```

Нужен Native AOT-тулчейн: `clang`, `zlib1g-dev`.

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

### Безопасность и данные

- Комментарии доступны без авторизации и хранятся вместе с именами авторов в SQLite (`/data/comments.db` в Docker).
- MCP также не имеет встроенной аутентификации. Публикуйте сервис только за
  TLS reverse proxy с контролем доступа; `MCP_READ_ONLY=true` запрещает запись через MCP.

## Тесты

```bash
CSHARPDOCS_XML=path/to/MyLibrary.xml dotnet test CSharpDocs.Tests
```

Сэмпл задаётся `CSHARPDOCS_XML` (DLL ищется рядом); без него тесты модели пропускаются.


## License

CSharpDocs is licensed under the [MIT License](LICENSE).
