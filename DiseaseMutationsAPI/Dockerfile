# Stage 1: Build the .NET app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y wget openssh-server python3-venv python3-distutils && rm -rf /var/lib/apt/lists/*  && mkdir /var/run/sshd

RUN echo "root:Docker!" | chpasswd \
    && sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config \
    && sed -i 's@session\s*required\s*pam_loginuid.so@session optional pam_loginuid.so@g' /etc/pam.d/sshd

# Copy the published .NET app
COPY --from=build /app/publish .

# Copy the bowtie file from the build context root to /app/bowtie
COPY bowtie /app/bowtie

# Ensure bowtie has executable permissions
RUN chmod +x /app/bowtie

EXPOSE 8080
EXPOSE 2222

CMD service ssh start

ENTRYPOINT ["dotnet", "DiseaseMutationsAPI.dll"]
