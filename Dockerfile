# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Backend/Backend.csproj", "Backend/"]
COPY ["gRNA/gRNA.fsproj", "gRNA/"]

RUN dotnet restore "Backend/Backend.csproj"

COPY Backend/ Backend/
COPY gRNA/ gRNA/

WORKDIR /src/Backend
RUN dotnet build "Backend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use pre-built base with bowtie
FROM disease-mutations-bowtie:latest AS final

# Install Python dependencies
RUN apt-get update && \
    apt-get install -y python3 python3-pip python3-venv && \
    pip3 install --break-system-packages --no-cache-dir viennarna && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

EXPOSE 80
ENTRYPOINT ["dotnet", "Backend.dll"]