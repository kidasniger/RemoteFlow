package com.example.sensors

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class GyroscopeManager(context: Context) : SensorEventListener {

    private val sensorManager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyroSensor: Sensor? = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)

    val isSupported: Boolean = gyroSensor != null

    private val _isActive = MutableStateFlow(false)
    val isActive: StateFlow<Boolean> = _isActive.asStateFlow()

    private val _deltaMovement = MutableStateFlow(Pair(0f, 0f))
    val deltaMovement: StateFlow<Pair<Float, Float>> = _deltaMovement.asStateFlow()

    private val _sensitivity = MutableStateFlow(0.75f)
    val sensitivity: StateFlow<Float> = _sensitivity.asStateFlow()

    private var offsetX = 0f
    private var offsetY = 0f

    fun setSensitivity(value: Float) {
        _sensitivity.value = value.coerceIn(0.1f, 2.0f)
    }

    fun start(): Boolean {
        if (!isSupported) return false
        val registered = sensorManager.registerListener(
            this,
            gyroSensor,
            SensorManager.SENSOR_DELAY_GAME
        )
        if (registered) {
            _isActive.value = true
        }
        return registered
    }

    fun stop() {
        if (_isActive.value) {
            sensorManager.unregisterListener(this)
            _isActive.value = false
            _deltaMovement.value = Pair(0f, 0f)
        }
    }

    fun calibrate() {
        offsetX = lastRawX
        offsetY = lastRawY
        _deltaMovement.value = Pair(0f, 0f)
    }

    private var lastRawX = 0f
    private var lastRawY = 0f

    override fun onSensorChanged(event: SensorEvent?) {
        if (event == null || !_isActive.value) return
        if (event.sensor.type == Sensor.TYPE_GYROSCOPE) {
            // Values: rotation speed around x, y, z in rad/s
            val rawX = event.values[0]
            val rawY = event.values[1]
            lastRawX = rawX
            lastRawY = rawY

            val sens = _sensitivity.value
            val dx = -(rawY - offsetY) * 15f * sens
            val dy = -(rawX - offsetX) * 15f * sens

            _deltaMovement.value = Pair(dx, dy)
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
}
