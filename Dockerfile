FROM alpine:latest as base

ENV \
    APP_UID=1654 \
    ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
EXPOSE 5000
EXPOSE 1194/udp
USER root
WORKDIR /app
RUN apk update && apk  add openvpn supervisor iptables iproute2 libc6-compat

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPNApi/ShadowVPNApi.csproj", "ShadowVPNApi/"]
RUN dotnet restore "ShadowVPNApi/ShadowVPNApi.csproj"
COPY . .
WORKDIR "/src/ShadowVPNApi"
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev
RUN dotnet build "./ShadowVPNApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShadowVPNApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY ShadowVPNApi/supervisord.conf /etc/supervisor/conf.d/supervisord.conf
COPY ShadowVPNApi/run-openvpn.sh /app/run-openvpn.sh
RUN chmod +x /app/run-openvpn.sh

CMD ["/usr/bin/supervisord", "--nodaemon", "--configuration", "/etc/supervisor/conf.d/supervisord.conf"]