FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y python3 python3-pip build-essential
RUN dotnet workload install wasm-tools

COPY DiseaseMutationsApp.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish


FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# Copy compiled WASM output
COPY --from=build /app/publish/wwwroot ./

# Copy custom Nginx config
COPY nginx.conf /etc/nginx/nginx.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
