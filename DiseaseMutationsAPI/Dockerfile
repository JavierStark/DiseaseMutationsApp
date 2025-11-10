# Stage 1: Build the .NET app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y wget python3-venv python3-distutils && rm -rf /var/lib/apt/lists/*  && mkdir /var/run/sshd

# Copy the published .NET app
COPY --from=build /app/publish .

# Copy the bowtie file from the build context root to /app/bowtie
COPY bowtie /app/bowtie
COPY bowtie-align-l.exe /app/bowtie-align-l.exe
COPY bowtie-align-s.exe /app/bowtie-align-s.exe
COPY bowtie-build-l.exe /app/bowtie-build-l.exe
COPY bowtie-build-s.exe /app/bowtie-build-s.exe
COPY bowtie-inspect-l.exe /app/bowtie-inspect-l.exe
COPY bowtie-inspect-s.exe /app/bowtie-inspect-s.exe

# Ensure bowtie has executable permissions
RUN chmod +x /app/bowtie /app/bowtie-*.exe

EXPOSE 8080

CMD bash -c "ls -la /app && dotnet DiseaseMutationsAPI.dll"

#ENTRYPOINT ["dotnet", "DiseaseMutationsAPI.dll"]
