# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

COPY ["DiseaseMutationsApp/DiseaseMutationsApp.csproj", "DiseaseMutationsApp/"]
COPY ["gRNA/gRNA.fsproj", "gRNA/"]

RUN dotnet restore "DiseaseMutationsApp/DiseaseMutationsApp.csproj"

COPY DiseaseMutationsApp/ DiseaseMutationsApp/
COPY gRNA/ gRNA/

WORKDIR /src/DiseaseMutationsApp
RUN dotnet build "DiseaseMutationsApp.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DiseaseMutationsApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final runtime image using your pre-built indices image as the base
FROM disease-mutations-bowtie:latest AS final
WORKDIR /app

# Install Python dependencies for RNA folding
RUN --mount=type=cache,target=/root/.cache/pip \
    apt-get update -o Acquire::Check::Date=false && \
    apt-get install -y --no-install-recommends python3 python3-pip python3-venv && \
    pip3 install --break-system-packages viennarna && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Copy bowtie binary and make executable (indices are already in /app/bowtie/indexes/ from the base image)
COPY bowtie/bowtie-align-s /app/bowtie/
RUN chmod +x /app/bowtie/bowtie-align-s

EXPOSE 80
EXPOSE 5000

ENTRYPOINT ["dotnet", "DiseaseMutationsApp.dll"]