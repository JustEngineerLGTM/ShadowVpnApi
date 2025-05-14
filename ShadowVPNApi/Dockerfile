FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPNApi/ShadowVPNApi.csproj", "ShadowVPNApi/"]
RUN dotnet restore "ShadowVPNApi/ShadowVPNApi.csproj"
COPY . .
WORKDIR "/src/ShadowVPNApi"
RUN dotnet build "ShadowVPNApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ShadowVPNApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://109.120.132.39:5000
ENTRYPOINT ["dotnet", "ShadowVPNApi.dll"]
