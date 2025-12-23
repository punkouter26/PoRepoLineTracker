# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and Directory.Packages.props for package restoration
COPY ["Directory.Packages.props", "./"]
COPY ["PoRepoLineTracker.sln", "./"]

# Copy all project files for restore
COPY ["src/PoRepoLineTracker.Api/PoRepoLineTracker.Api.csproj", "src/PoRepoLineTracker.Api/"]
COPY ["src/PoRepoLineTracker.Application/PoRepoLineTracker.Application.csproj", "src/PoRepoLineTracker.Application/"]
COPY ["src/PoRepoLineTracker.Client/PoRepoLineTracker.Client.csproj", "src/PoRepoLineTracker.Client/"]
COPY ["src/PoRepoLineTracker.Domain/PoRepoLineTracker.Domain.csproj", "src/PoRepoLineTracker.Domain/"]
COPY ["src/PoRepoLineTracker.Infrastructure/PoRepoLineTracker.Infrastructure.csproj", "src/PoRepoLineTracker.Infrastructure/"]
COPY ["src/PoRepoLineTracker.ServiceDefaults/PoRepoLineTracker.ServiceDefaults.csproj", "src/PoRepoLineTracker.ServiceDefaults/"]
COPY ["src/PoRepoLineTracker.AppHost/PoRepoLineTracker.AppHost.csproj", "src/PoRepoLineTracker.AppHost/"]

# Restore packages
RUN dotnet restore "src/PoRepoLineTracker.Api/PoRepoLineTracker.Api.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/src/PoRepoLineTracker.Api"
RUN dotnet build "PoRepoLineTracker.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "PoRepoLineTracker.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create LocalRepos directory for git operations
RUN mkdir -p /app/LocalRepos

# Install git for repository cloning operations
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "PoRepoLineTracker.Api.dll"]
