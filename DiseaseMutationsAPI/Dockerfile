# Stage 1: Build the .NET app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y wget python3-venv python3-distutils && rm -rf /var/lib/apt/lists/*

# Copy the published .NET app
COPY --from=build /app/publish .

# Ensure bowtie executable permissions
RUN chmod +x ./bowtie

EXPOSE 8080
ENTRYPOINT ["dotnet", "DiseaseMutationsAPI.dll"]
