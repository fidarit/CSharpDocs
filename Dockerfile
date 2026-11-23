# syntax=docker/dockerfile:1

# --- Тесты ---
FROM mcr.microsoft.com/dotnet/sdk:11.0-alpine-aot AS test

WORKDIR /src
COPY . .
RUN dotnet test CSharpDocs.sln --configuration Release

# --- Сборка: Native AOT ---
FROM test AS build

RUN dotnet restore CSharpDocs/CSharpDocs.csproj -r linux-musl-x64
RUN dotnet publish CSharpDocs/CSharpDocs.csproj \
        -c Release -r linux-musl-x64 --no-restore \
        -o /app/publish

# --- Рантайм: alpine с нативными зависимостями (musl) ---
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-alpine AS runtime

WORKDIR /app
COPY --from=build /app/publish ./

# База комментариев — в томе, чтобы данные не терялись между запусками.
VOLUME /data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV MCP_ENABLED=false
ENV MCP_READ_ONLY=false
ENTRYPOINT ["/app/CSharpDocs"]
# Аргументы по умолчанию; переопределяются через `docker run ... <args>`.
CMD ["-db", "/data/comments.db"]
