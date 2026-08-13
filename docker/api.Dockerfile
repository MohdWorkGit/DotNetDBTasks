# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY Bayan.sln ./
COPY src/Bayan.Domain/Bayan.Domain.csproj src/Bayan.Domain/
COPY src/Bayan.Application/Bayan.Application.csproj src/Bayan.Application/
COPY src/Bayan.Infrastructure/Bayan.Infrastructure.csproj src/Bayan.Infrastructure/
COPY src/Bayan.API/Bayan.API.csproj src/Bayan.API/

# Restore dependencies
RUN dotnet restore

# Copy all source code
COPY . .

# Build and publish
RUN dotnet publish src/Bayan.API/Bayan.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN useradd --no-create-home --shell /bin/false appuser

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

ENTRYPOINT ["dotnet", "Bayan.API.dll"]
