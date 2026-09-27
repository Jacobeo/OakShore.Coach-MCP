FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/OakShore.Coach.Domain/OakShore.Coach.Domain.csproj src/OakShore.Coach.Domain/
COPY src/OakShore.Coach.Infrastructure/OakShore.Coach.Infrastructure.csproj src/OakShore.Coach.Infrastructure/
COPY src/OakShore.Coach.Api/OakShore.Coach.Api.csproj src/OakShore.Coach.Api/
RUN dotnet restore src/OakShore.Coach.Api/OakShore.Coach.Api.csproj

COPY src/ src/
RUN dotnet publish src/OakShore.Coach.Api/OakShore.Coach.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "OakShore.Coach.Api.dll"]
