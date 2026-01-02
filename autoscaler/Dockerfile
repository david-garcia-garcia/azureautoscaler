# Use the official .NET runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Use the official .NET SDK as the build image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file(s) and restore dependencies
COPY ["poolautoscaler.csproj", "./"]
RUN dotnet restore "poolautoscaler.csproj"

# Copy the rest of the application source code
COPY . .

# Build argument for version
ARG VERSION=0.0.0-dev

# Build the application
RUN dotnet publish "poolautoscaler.csproj" -c Release -o /app/publish -p:VERSION=${VERSION}

# Final stage: runtime
FROM base AS final
WORKDIR /app

# Copy the published output and the config file
COPY --from=build /app/publish .
#COPY config.yaml ./config.yaml

# Expose a port if necessary (e.g., for monitoring/health checks)
# EXPOSE 8080

# Set the entry point for the container
ENTRYPOINT ["dotnet", "poolautoscaler.dll"]
