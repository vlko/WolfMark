package io.github.vlko.wolfmark

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import io.github.vlko.wolfmark.ui.WolfMarkScreen

class MainActivity : ComponentActivity() {
    private val model: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { WolfMarkScreen(model, onSignIn = { model.signIn(this) }) }
    }

    override fun onResume() {
        super.onResume()
        // Coming back from another app (often right after sharing from it): see what is new.
        model.onForeground()
    }
}
