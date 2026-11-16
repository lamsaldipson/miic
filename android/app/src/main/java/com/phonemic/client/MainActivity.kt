package com.phonemic.client

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

class MainActivity : Activity() {
    private val main = Handler(Looper.getMainLooper())
    private var pendingHost: String? = null
    private var scanGeneration = 0
    private var scanRunning = false
    private var visible = false
    private var manualScan = false
    private var found = emptyList<LanReceiver>()
    private lateinit var host: EditText
    private lateinit var status: TextView
    private lateinit var checkReceiver: Button
    private lateinit var connect: Button
    private lateinit var disconnect: Button
    private lateinit var mute: Button
    private lateinit var rows: LinearLayout

    private val nextScan = Runnable { scan() }
    private val observer: (MicService.State) -> Unit = { render(it) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pendingHost = savedInstanceState?.getString("pendingHost")
        val previousHost = getPreferences(MODE_PRIVATE).getString("host", null)
        host = EditText(this).apply {
            hint = "Windows IPv4 address (optional)"
            isSingleLine = true
            setText(previousHost.orEmpty())
        }
        status = TextView(this)
        checkReceiver = Button(this).apply {
            text = "Check receiver"
            setOnClickListener {
                val entered = host.text.toString().trim()
                if (entered.isNotEmpty() && !validAddress(entered)) {
                    status.text = "Enter a valid Windows IPv4 address or leave it blank."
                    return@setOnClickListener
                }
                manualScan = true
                if (!MicService.state.active) status.text = "Checking receivers…"
                scan()
            }
        }
        connect = Button(this).apply {
            text = "Connect by IP"
            setOnClickListener {
                val entered = host.text.toString().trim()
                if (validAddress(entered)) requestConnect(entered)
                else status.text = "Enter the Windows IPv4 address."
            }
        }
        disconnect = Button(this).apply {
            text = "Disconnect"
            setOnClickListener { startService(Intent(this@MainActivity, MicService::class.java).setAction(MicService.STOP)) }
        }
        mute = Button(this).apply {
            text = "Mute"
            setOnClickListener { MicService.toggleMute() }
        }
        rows = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            for (view in listOf(host, checkReceiver, connect, disconnect, mute, status, rows)) addView(view)
        }
        val scroll = ScrollView(this).apply {
            addView(content)
            setOnApplyWindowInsetsListener { view, insets ->
                @Suppress("DEPRECATION")
                view.setPadding(
                    insets.systemWindowInsetLeft + dp(16), insets.systemWindowInsetTop + dp(16),
                    insets.systemWindowInsetRight + dp(16), insets.systemWindowInsetBottom + dp(16)
                )
                insets
            }
        }
        setContentView(scroll)
    }

    override fun onStart() {
        super.onStart()
        visible = true
        MicService.observer = observer
        render(MicService.state)
        scan()
    }

    override fun onStop() {
        visible = false
        scanGeneration++
        scanRunning = false
        manualScan = false
        main.removeCallbacks(nextScan)
        if (MicService.observer === observer) MicService.observer = null
        super.onStop()
    }

    private fun scan() {
        if (!visible || scanRunning) return
        scanRunning = true
        main.removeCallbacks(nextScan)
        val generation = scanGeneration
        val address = host.text.toString().trim().takeIf(::validAddress)
        Thread({
            val result = ReceiverDiscovery.scan(address)
            main.post {
                if (!visible || generation != scanGeneration) return@post
                scanRunning = false
                if (found != result) {
                    found = result
                    renderRows()
                }
                if (manualScan && !MicService.state.active) {
                    status.text = if (found.isEmpty()) "No receivers found." else "Tap a receiver to connect."
                }
                manualScan = false
                main.postDelayed(nextScan, 3000)
            }
        }, "PhoneMic receiver discovery").start()
    }

    private fun render(state: MicService.State) {
        host.isEnabled = !state.active
        connect.isEnabled = !state.active
        disconnect.isEnabled = state.active
        mute.isEnabled = state.connected
        mute.text = if (state.muted) "Unmute" else "Mute"
        status.text = state.message
        renderRows()
    }

    private fun renderRows() {
        rows.removeAllViews()
        for (receiver in found) {
            val selected = receiver.address == MicService.state.host
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(12), dp(10), dp(12), dp(10))
                val attr = android.util.TypedValue()
                theme.resolveAttribute(android.R.attr.selectableItemBackground, attr, true)
                setBackgroundResource(attr.resourceId)
                contentDescription = "${receiver.name}, ${receiver.address}" + if (selected) ", selected" else ""
                isFocusable = true
                setOnClickListener {
                    if (!selected) requestConnect(receiver.address)
                }
                addView(TextView(this@MainActivity).apply {
                    text = receiver.name + if (selected) "  ✓" else ""
                    textSize = 19f
                })
                addView(TextView(this@MainActivity).apply {
                    text = receiver.address
                    textSize = 13f
                })
            }
            rows.addView(row)
        }
    }

    private fun validAddress(address: String): Boolean {
        val octets = address.split('.')
        return octets.size == 4 && octets.all { part ->
            part.length in 1..3 && part.all { it in '0'..'9' } &&
                (part.toIntOrNull() ?: -1) in 0..255
        }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun requestConnect(address: String) {
        pendingHost = address
        host.setText(address)
        getPreferences(MODE_PRIVATE).edit().putString("host", address).apply()
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), 1)
        } else {
            requestNotificationOrStart()
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, results: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, results)
        if (requestCode != 1 && requestCode != 2) return
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
            if (requestCode == 1) requestNotificationOrStart() else startMic()
        } else {
            pendingHost = null
            render(MicService.State("Microphone permission is required to connect."))
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
        } catch (_: IllegalStateException) {
            render(MicService.State("Open PhoneMic and select the receiver again."))
        } catch (_: SecurityException) {
            render(MicService.State("Android denied microphone access."))
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putString("pendingHost", pendingHost)
        super.onSaveInstanceState(outState)
    }
}
