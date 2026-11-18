package io.github.vlko.wolfmark

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.runtime.getValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import io.github.vlko.wolfmark.ui.WolfMarkScreen

class MainActivity : ComponentActivity() {
    private val model: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        // Everything the screens ask for goes to the view model; signing in also needs this window.
        val actions = object : Actions by model {
            override fun signIn() = model.signIn(this@MainActivity)
        }
        setContent {
            val state by model.state.collectAsStateWithLifecycle()
            WolfMarkScreen(state, actions)
        }
    }

    override fun onResume() {
        super.onResume()
        // Coming back from another app (often right after sharing from it): see what is new.
        model.onForeground()
    }
}
