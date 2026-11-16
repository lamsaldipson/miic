package com.phonemic.client

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import java.net.HttpURLConnection
import java.net.URL

class MainActivity : Activity() {
    private var pendingHost: String? = null

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, results: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, results)
        if (requestCode == 1 || requestCode == 2) {
            if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
                if (requestCode == 1) requestNotificationOrStart() else startMic()
            } else {
                pendingHost = null
                render(MicService.State("Microphone permission is required to connect."))
            }
        }
    }

    private fun requestNotificationOrStart() {
        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED &&
            !getPreferences(MODE_PRIVATE).getBoolean("notificationRequested", false)) {
            getPreferences(MODE_PRIVATE).edit().putBoolean("notificationRequested", true).apply()
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2)
        } else {
            startMic()
        }
    }

    private fun startMic() {
        val address = pendingHost ?: return
        pendingHost = null
        try {
            startForegroundService(Intent(this, MicService::class.java).putExtra(MicService.HOST, address))
        } catch (error: IllegalStateException) {
            render(MicService.State("Open PhoneMic and tap Connect again."))
        } catch (error: SecurityException) {
            render(MicService.State("Android denied microphone access."))
        }
    }

    private lateinit var render: (MicService.State) -> Unit

    override fun onStart() {
        super.onStart()
        MicService.observer = render
        render(MicService.state)
    }

    override fun onStop() {
        if (MicService.observer === render) MicService.observer = null
        super.onStop()
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pendingHost = savedInstanceState?.getString("pendingHost")
        val host = EditText(this).apply {
            hint = "Windows IPv4 address"
            isSingleLine = true
            setText(getPreferences(MODE_PRIVATE).getString("host", ""))
        }
        val status = TextView(this).apply { text = "Disconnected" }
        val button = Button(this).apply { text = "Check receiver" }
        val connect = Button(this).apply { text = "Connect" }
        val disconnect = Button(this).apply { text = "Disconnect" }
        val mute = Button(this).apply { text = "Mute" }
        var checking = false
        render = { state ->
            host.isEnabled = !state.active
            button.isEnabled = !state.active && !checking
            connect.isEnabled = !state.active && !checking
            disconnect.isEnabled = state.active
            mute.isEnabled = state.connected
            mute.text = if (state.muted) "Unmute" else "Mute"
            status.text = state.message
        }
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            addView(host)
            addView(button)
            addView(connect)
            addView(disconnect)
            addView(mute)
            addView(status)
            setOnApplyWindowInsetsListener { view, insets ->
                @Suppress("DEPRECATION")
                view.setPadding(
                    insets.systemWindowInsetLeft + 24, insets.systemWindowInsetTop + 24,
                    insets.systemWindowInsetRight + 24, insets.systemWindowInsetBottom + 24
                )
                insets
            }
        }
        setContentView(content)
        fun address(): String? {
            val address = host.text.toString().trim()
            val octets = address.split('.')
            if (octets.size != 4 || octets.any { part ->
                    part.length !in 1..3 || part.any { it !in '0'..'9' } ||
                        (part.toIntOrNull() ?: -1) !in 0..255
                }) {
                status.text = "Enter the Windows IPv4 address."
                return null
            }
            getPreferences(MODE_PRIVATE).edit().putString("host", address).apply()
            return address
        }
        connect.setOnClickListener {
            pendingHost = address() ?: return@setOnClickListener
            if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), 1)
            } else {
                requestNotificationOrStart()
            }
        }
        disconnect.setOnClickListener {
            startService(Intent(this, MicService::class.java).setAction(MicService.STOP))
        }
        mute.setOnClickListener { MicService.toggleMute() }
        button.setOnClickListener {
            val address = address() ?: return@setOnClickListener
            checking = true
            render(MicService.state)
            status.text = "Checking receiver…"
            Thread {
                val result = try {
                    val connection = URL("http://$address:8765/api/hello").openConnection() as HttpURLConnection
                    try {
                        connection.connectTimeout = 3000
                        connection.readTimeout = 3000
                        if (connection.responseCode == 200) "Receiver reached"
                        else "Receiver returned HTTP ${connection.responseCode}"
                    } finally {
                        connection.disconnect()
                    }
                } catch (error: java.io.IOException) {
                    "Receiver unreachable"
                }
                runOnUiThread {
                    if (!isDestroyed) {
                        checking = false
                        render(MicService.state.copy(message = result))
                    }
                }
            }.start()
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putString("pendingHost", pendingHost)
        super.onSaveInstanceState(outState)
    }
}
