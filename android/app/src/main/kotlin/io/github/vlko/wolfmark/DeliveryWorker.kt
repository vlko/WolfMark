package io.github.vlko.wolfmark

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import androidx.core.app.NotificationCompat
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.ForegroundInfo
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.OutOfQuotaPolicy
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import io.github.vlko.wolfmark.core.ShareDelivery
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit

/**
 * Sends everything in the queue: to a computer on the network or to OneDrive (`ShareDelivery`). Runs as soon as
 * there is a connection and again, with growing pauses, while something is left.
 */
class DeliveryWorker(context: Context, parameters: WorkerParameters) : CoroutineWorker(context, parameters) {

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val app = applicationContext.wolfMark
        var left = false
        var usedOneDrive = false
        for (share in app.queue.pending()) {
            val net = Net.of(applicationContext)
            when (app.delivery.deliver(share, net.localNetwork, net.internet)) {
                ShareDelivery.Outcome.DELIVERED_TO_COMPUTER -> Unit
                ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE -> usedOneDrive = true
                ShareDelivery.Outcome.RETRY_LATER -> left = true
            }
        }

        // The computers' addresses change (another network, a new key): look again now and then, while online.
        if ((usedOneDrive || left) && System.currentTimeMillis() - app.devicesRefreshedAt > DEVICES_MAX_AGE_MS) {
            runCatching {
                app.graphOrNull()?.let { graph ->
                    app.devices.refresh(graph, app.vault)
                    app.devicesRefreshedAt = System.currentTimeMillis()
                }
            }
        }
        if (left) Result.retry() else Result.success()
    }

    /** Only asked for before Android 12, where urgent work runs as a foreground service. */
    override suspend fun getForegroundInfo(): ForegroundInfo {
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, applicationContext.getString(R.string.delivery_channel), NotificationManager.IMPORTANCE_LOW),
        )
        val notification = NotificationCompat.Builder(applicationContext, CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(applicationContext.getString(R.string.delivery_notification))
            .setOngoing(true)
            .build()
        return ForegroundInfo(1, notification)
    }

    companion object {
        private const val CHANNEL = "delivery"
        const val WORK = "deliver"
        private const val DEVICES_MAX_AGE_MS = 12 * 3600 * 1000L

        /** Asks for a delivery run; one that is already waiting or running is followed by another. */
        fun schedule(context: Context) {
            val request = OneTimeWorkRequestBuilder<DeliveryWorker>()
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST)
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
                .build()
            WorkManager.getInstance(context).enqueueUniqueWork(WORK, ExistingWorkPolicy.APPEND_OR_REPLACE, request)
        }
    }
}
