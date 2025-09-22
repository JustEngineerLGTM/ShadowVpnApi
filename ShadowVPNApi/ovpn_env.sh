#!/bin/bash
# VPN сеть
OVPN_SERVER="10.8.0.0/24"

# NAT интерфейс (если пустой — подставится автоматически)
OVPN_NATDEVICE=""

# Добавочные маршруты (если есть)
OVPN_ROUTES=()

# Флаг добавления NAT
OVPN_NAT=1

# Флаг дефолтного маршрута
OVPN_DEFROUTE=1

# OpenVPN конфиг и PKI
OPENVPN="/etc/openvpn"

