# Một image cho cả api và worker (chọn bằng ASPNETCORE_ENVIRONMENT=api|worker, = SPRING_PROFILES_ACTIVE).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props PortFlow.slnx ./
COPY src/ src/
RUN dotnet publish src/PortFlow.Host/PortFlow.Host.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql cần thư viện GSSAPI (nếu thiếu sẽ log lỗi lúc kết nối)
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
USER app
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "PortFlow.Host.dll"]
