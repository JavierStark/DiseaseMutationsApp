# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

# Copy project files
COPY ["DiseaseMutationsApp/DiseaseMutationsApp.csproj", "DiseaseMutationsApp/"]
COPY ["gRNA/gRNA.fsproj", "gRNA/"]

# Restore dependencies
RUN dotnet restore "DiseaseMutationsApp/DiseaseMutationsApp.csproj"

# Copy source code
COPY DiseaseMutationsApp/ DiseaseMutationsApp/
COPY gRNA/ gRNA/

# Build the application
WORKDIR /src/DiseaseMutationsApp
RUN dotnet build "DiseaseMutationsApp.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DiseaseMutationsApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Imported bowtie indices image
FROM disease-mutations-bowtie:latest AS indices

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final
WORKDIR /app

# Install Python dependencies for RNA folding
RUN --mount=type=cache,target=/root/.cache/pip \
    apt-get update -o Acquire::Check::Date=false && \
    apt-get install -y python3 python3-pip python3-venv && \
    pip3 install --break-system-packages viennarna && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Copy bowtie indices from base image (changes rarely)
COPY --from=indices /app/bowtie/ ./bowtie/indexes/

# Copy bowtie binary and make executable
COPY bowtie/bowtie-align-s /app/bowtie/
RUN chmod +x /app/bowtie/bowtie-align-s

EXPOSE 80
EXPOSE 5000

ENTRYPOINT ["dotnet", "DiseaseMutationsApp.dll"]
