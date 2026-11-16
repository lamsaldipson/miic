package com.phonemic.client

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

class MicService : Service() {
    data class State(val message: String, val active: Boolean = false, val connected: Boolean = false,
                     val muted: Boolean = false, val host: String? = null)

    companion object {
        const val STOP = "com.phonemic.client.STOP"
        const val HOST = "host"
        private const val CHANNEL = "microphone"
        private const val RATE = 16000

        var state = State("Disconnected")
            private set
        var observer: ((State) -> Unit)? = null
        private var activeService: MicService? = null

        fun toggleMute() {
            activeService?.let { service ->
                if (state.connected) {
                    service.muted = !service.muted
                    service.update(state.copy(
                        message = if (service.muted) "Muted" else "Streaming",
                        muted = service.muted
                    ))
                }
            }
        }
    }

    private val main = Handler(Looper.getMainLooper())
    private var socket: Socket? = null
    private var queuedHost: String? = null
    @Volatile private var muted = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) {
            stopSession()
            return START_NOT_STICKY
        }
        val host = intent?.getStringExtra(HOST)
        if (host == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        val current = socket
        if (current != null) {
            if (state.host != host) {
                queuedHost = host
                runCatching { current.close() }
                update(State("Switching receiver…", active = true, muted = muted, host = host))
            }
            return START_NOT_STICKY
        }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "PhoneMic", NotificationManager.IMPORTANCE_LOW))
        try {
            startForeground(1, notification("Connecting…"))
        } catch (error: SecurityException) {
            stopSession("Android denied microphone service access.")
            return START_NOT_STICKY
        }
        muted = false
        beginStream(host)
        return START_NOT_STICKY
    }

    private fun beginStream(host: String) {
        val connection = Socket()
        socket = connection
        activeService = this
        update(State("Connecting…", active = true, muted = muted, host = host))
        Thread({ stream(connection, host) }, "PhoneMic audio").start()
    }

    private fun notification(message: String): Notification {
        val flags = PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), flags)
        val stop = PendingIntent.getService(this, 0, Intent(this, MicService::class.java).setAction(STOP), flags)
        return Notification.Builder(this, CHANNEL)
            .setContentTitle("PhoneMic")
            .setContentText(message)
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setContentIntent(open)
            .addAction(Notification.Action.Builder(null, "Disconnect", stop).build())
            .setOngoing(true)
            .build()
    }

    private fun update(next: State) {
        state = next
        observer?.invoke(next)
        if (next.active) getSystemService(NotificationManager::class.java).notify(1, notification(next.message))
    }

    @Suppress("MissingPermission")
    private fun stream(connection: Socket, host: String) {
        var recorder: AudioRecord? = null
        var failure = "Disconnected"
        try {
            connection.connect(InetSocketAddress(host, 8766), 3000)
            connection.soTimeout = 3000
            connection.tcpNoDelay = true
            val output = connection.getOutputStream()
            output.write('P'.code)
            if (connection.getInputStream().read() != 6) throw IOException("Receiver handshake failed")
            val channel = AudioFormat.CHANNEL_IN_MONO
            val encoding = AudioFormat.ENCODING_PCM_16BIT
            val minimum = AudioRecord.getMinBufferSize(RATE, channel, encoding)
            check(minimum > 0)
            val audio = AudioRecord(MediaRecorder.AudioSource.MIC, RATE, channel, encoding, maxOf(minimum, 2560))
            recorder = audio
            check(audio.state == AudioRecord.STATE_INITIALIZED)
            audio.startRecording()
            check(audio.recordingState == AudioRecord.RECORDSTATE_RECORDING)
            main.post {
                if (socket === connection && queuedHost == null) {
                    update(State(if (muted) "Muted" else "Streaming",
                        active = true, connected = true, muted = muted, host = host))
                }
            }
            val samples = ShortArray(320)
            val bytes = ByteArray(samples.size * 2)
            while (!connection.isClosed) {
                val count = audio.read(samples, 0, samples.size)
                if (count < 0) error("Microphone read failed")
                val silence = muted
                for (index in 0 until count) {
                    val sample = if (silence) 0 else samples[index].toInt()
                    bytes[2 * index] = sample.toByte()
                    bytes[2 * index + 1] = (sample shr 8).toByte()
                }
                output.write(bytes, 0, count * 2)
            }
        } catch (error: IOException) {
            failure = "Audio receiver connection ended. Check port 8766."
        } catch (error: RuntimeException) {
            failure = "Microphone unavailable. Check permission and microphone access."
        } finally {
            recorder?.release()
            runCatching { connection.close() }
            main.post {
                if (socket !== connection) return@post
                val next = queuedHost
                queuedHost = null
                if (next == null) stopSession(failure) else beginStream(next)
            }
        }
    }

    private fun stopSession(message: String = "Disconnected") {
        closeSession()
        update(State(message))
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun closeSession() {
        val connection = socket
        socket = null
        queuedHost = null
        runCatching { connection?.close() }
        if (activeService === this) activeService = null
    }

    override fun onDestroy() {
        if (activeService === this) {
            closeSession()
            update(State("Disconnected"))
        }
        super.onDestroy()
    }
}
