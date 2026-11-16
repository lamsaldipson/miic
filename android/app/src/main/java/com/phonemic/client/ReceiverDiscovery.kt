package com.phonemic.client

import android.os.SystemClock
import org.json.JSONObject
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InterfaceAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

internal data class LanReceiver(val name: String, val address: String)

internal object ReceiverDiscovery {
    private const val PORT = 8765
    private val query = "PhoneMic discover/1".toByteArray(Charsets.US_ASCII)

    fun scan(manualAddress: String?): List<LanReceiver> {
        val receivers = mutableMapOf<String, Pair<LanReceiver, Int>>()
        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                val destinations = mutableSetOf(InetAddress.getByName("255.255.255.255"))
                if (manualAddress != null) destinations += InetAddress.getByName(manualAddress)
                val localAddresses = mutableListOf<InterfaceAddress>()
                val interfaces = NetworkInterface.getNetworkInterfaces()
                while (interfaces != null && interfaces.hasMoreElements()) {
                    val network = interfaces.nextElement()
                    if (network.isUp && !network.isLoopback) {
                        for (address in network.interfaceAddresses) {
                            if (address.address is Inet4Address) localAddresses += address
                            address.broadcast?.let { destinations += it }
                        }
                    }
                }
                for (destination in destinations) {
                    try {
                        socket.send(DatagramPacket(query, query.size, destination, PORT))
                    } catch (_: IOException) {
                        // One unavailable interface can coexist with a working Wi-Fi interface.
                    }
                }
                val deadline = SystemClock.elapsedRealtime() + 850
                val response = DatagramPacket(ByteArray(1024), 1024)
                while (true) {
                    val remaining = deadline - SystemClock.elapsedRealtime()
                    if (remaining <= 0) break
                    socket.soTimeout = remaining.toInt()
                    response.length = response.data.size
                    try {
                        socket.receive(response)
                    } catch (_: SocketTimeoutException) {
                        break
                    }
                    val address = response.address
                    if (response.port != PORT || address !is Inet4Address || address.isLoopbackAddress) continue
                    val info = runCatching {
                        JSONObject(String(response.data, response.offset, response.length, Charsets.UTF_8))
                    }.getOrNull() ?: continue
                    if (info.optString("protocol") != "PhoneMic/1") continue
                    val name = info.optString("name").trim().take(64)
                    val ip = address.hostAddress ?: continue
                    if (name.isEmpty()) continue
                    val id = info.optString("id").takeIf { it.length == 32 } ?: ip
                    val priority = when {
                        ip == manualAddress -> 2
                        localAddresses.any { matchesSubnet(address, it) } -> 1
                        else -> 0
                    }
                    val previous = receivers[id]
                    if (previous == null || priority > previous.second ||
                        (priority == previous.second && ip < previous.first.address)) {
                        receivers[id] = LanReceiver(name, ip) to priority
                    }
                }
            }
        } catch (_: IOException) {
            // The Wi-Fi interface can disappear during a scan.
        }
        return receivers.values.map { it.first }
            .sortedWith(compareBy({ it.name.lowercase() }, { it.address }))
    }

    private fun matchesSubnet(address: Inet4Address, local: InterfaceAddress): Boolean {
        val prefix = local.networkPrefixLength.toInt()
        if (prefix !in 1..32) return false
        val remoteBytes = address.address
        val localBytes = local.address.address
        for (index in 0 until 4) {
            val bits = (prefix - 8 * index).coerceIn(0, 8)
            val mask = 255 and (255 shl (8 - bits))
            if ((remoteBytes[index].toInt() and mask) != (localBytes[index].toInt() and mask)) return false
        }
        return true
    }
}
