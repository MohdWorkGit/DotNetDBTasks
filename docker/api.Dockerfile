# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY DotNetDBTasks.sln ./
COPY src/DotNetDBTasks.Domain/DotNetDBTasks.Domain.csproj src/DotNetDBTasks.Domain/
COPY src/DotNetDBTasks.Application/DotNetDBTasks.Application.csproj src/DotNetDBTasks.Application/
COPY src/DotNetDBTasks.Infrastructure/DotNetDBTasks.Infrastructure.csproj src/DotNetDBTasks.Infrastructure/
COPY src/DotNetDBTasks.API/DotNetDBTasks.API.csproj src/DotNetDBTasks.API/

# Restore dependencies
RUN dotnet restore

# Copy all source code
COPY . .

# Build and publish
RUN dotnet publish src/DotNetDBTasks.API/DotNetDBTasks.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser

# Copy published output
COPY --from=build /app/publish .

# Create log directory
RUN mkdir -p /app/logs && chown -R appuser:appuser /app

USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget -qO- http://localhost:8080/swagger/index.html || exit 1

ENTRYPOINT ["dotnet", "DotNetDBTasks.API.dll"]
