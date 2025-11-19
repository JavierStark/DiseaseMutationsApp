# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution file and project files
COPY ["Backend/Backend.csproj", "Backend/"]
COPY ["gRNA/gRNA.fsproj", "gRNA/"]

# Restore dependencies
RUN dotnet restore "Backend/Backend.csproj"

# Copy the rest of the source code
COPY Backend/ Backend/
COPY gRNA/ gRNA/

# Build the application
WORKDIR /src/Backend
RUN dotnet build "Backend.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Expose port
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "Backend.dll"]

