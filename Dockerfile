# Multi-stage build for TimChuyenDi ASP.NET Core app (.NET 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers for better cache reuse
COPY TimChuyenDi/TimChuyenDi.csproj TimChuyenDi/
RUN dotnet restore TimChuyenDi/TimChuyenDi.csproj

# Copy everything else and publish
COPY . .
RUN dotnet publish TimChuyenDi/TimChuyenDi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Kestrel listens on 8080 inside container
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TimChuyenDi.dll"]
