#!/bin/bash
set -e

[ "$DEBUG" == "1" ] && set -x

cd /etc/openvpn

mkdir -p /dev/net
[ ! -c /dev/net/tun ] && mknod /dev/net/tun c 10 200

source /etc/openvpn/ovpn_env.sh

[ -z "$OVPN_NATDEVICE" ] && OVPN_NATDEVICE=$(ip route | grep default | awk '{print $5}')

function setupIptablesAndRouting {
    iptables -t nat -C POSTROUTING -s $OVPN_SERVER -o $OVPN_NATDEVICE -j MASQUERADE 2>/dev/null || \
        iptables -t nat -A POSTROUTING -s $OVPN_SERVER -o $OVPN_NATDEVICE -j MASQUERADE
    for i in "${OVPN_ROUTES[@]}"; do
        iptables -t nat -C POSTROUTING -s "$i" -o $OVPN_NATDEVICE -j MASQUERADE 2>/dev/null || \
            iptables -t nat -A POSTROUTING -s "$i" -o $OVPN_NATDEVICE -j MASQUERADE
    done
}

[ "$OVPN_DEFROUTE" != "0" ] || [ "$OVPN_NAT" == "1" ] && setupIptablesAndRouting

exec openvpn --config /etc/openvpn/server.conf
