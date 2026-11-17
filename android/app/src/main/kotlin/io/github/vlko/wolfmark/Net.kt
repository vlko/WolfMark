package io.github.vlko.wolfmark

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities

/** What the phone is connected to right now. */
data class Net(val localNetwork: Boolean, val internet: Boolean) {
    companion object {
        fun of(context: Context): Net {
            val manager = context.getSystemService(ConnectivityManager::class.java)
            val capabilities = manager?.getNetworkCapabilities(manager.activeNetwork) ?: return Net(false, false)
            return Net(
                // Where a computer may be reachable directly: the home network, or a VPN that joins the devices.
                localNetwork = capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) ||
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN),
                internet = capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                    capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED),
            )
        }
    }
}
