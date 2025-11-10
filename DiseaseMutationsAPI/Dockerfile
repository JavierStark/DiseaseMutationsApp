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

# Copy the bowtie file from the build context root to /app
COPY /bowtieFiles/bowtie /app/bowtie
COPY /bowtieFiles/bowtie-align-l /app/bowtie-align-l
COPY /bowtieFiles/bowtie-align-s /app/bowtie-align-s
COPY /bowtieFiles/bowtie-build-l /app/bowtie-build-l
COPY /bowtieFiles/bowtie-build-s /app/bowtie-build-s
COPY /bowtieFiles/bowtie-inspect-l /app/bowtie-inspect-l
COPY /bowtieFiles/bowtie-inspect-s /app/bowtie-inspect-s

# Ensure bowtie has executable permissions
RUN chmod +x /app/bowtie /app/bowtie-*.exe

EXPOSE 8080

CMD bash -c "ls -la /app && dotnet DiseaseMutationsAPI.dll"

#ENTRYPOINT ["dotnet", "DiseaseMutationsAPI.dll"]
