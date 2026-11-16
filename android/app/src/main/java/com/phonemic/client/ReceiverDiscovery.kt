package com.phonemic.client

import android.os.SystemClock
import org.json.JSONObject
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

internal data class LanReceiver(val name: String, val address: String)

internal object ReceiverDiscovery {
    private const val PORT = 8765
    private val query = "PhoneMic discover/1".toByteArray(Charsets.US_ASCII)

    fun scan(manualAddress: String?): List<LanReceiver> {
        val receivers = mutableMapOf<String, LanReceiver>()
        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                val destinations = mutableSetOf(InetAddress.getByName("255.255.255.255"))
                if (manualAddress != null) destinations += InetAddress.getByName(manualAddress)
                val interfaces = NetworkInterface.getNetworkInterfaces()
                while (interfaces != null && interfaces.hasMoreElements()) {
                    val network = interfaces.nextElement()
                    if (network.isUp && !network.isLoopback) {
                        for (address in network.interfaceAddresses) {
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
                    if (name.isNotEmpty()) receivers[ip] = LanReceiver(name, ip)
                }
            }
        } catch (_: IOException) {
            // The Wi-Fi interface can disappear during a scan.
        }
        return receivers.values.sortedWith(compareBy({ it.name.lowercase() }, { it.address }))
    }
}
