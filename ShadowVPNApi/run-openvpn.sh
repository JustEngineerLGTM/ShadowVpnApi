#!/bin/bash
set -e

OPENVPN=/etc/openvpn
OVPN_SERVER=${OVPN_SERVER:-10.8.0.0/24}
OVPN_ROUTES=${OVPN_ROUTES:-()}

OVPN_NATDEVICE=$(ip route get 8.8.8.8 | awk '{print $5; exit}')

iptables -t nat -C POSTROUTING -s $OVPN_SERVER -o $OVPN_NATDEVICE -j MASQUERADE 2>/dev/null || \
    iptables -t nat -A POSTROUTING -s $OVPN_SERVER -o $OVPN_NATDEVICE -j MASQUERADE

exec /usr/sbin/openvpn --config "$OPENVPN/server/server.conf"
