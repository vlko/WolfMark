package io.github.vlko.wolfmark.core

import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import org.json.JSONArray
import org.json.JSONObject
import java.net.InetSocketAddress
import java.net.URLDecoder

/**
 * The slice of Microsoft Graph the app uses, in memory: upload by path (refusing taken names), folders, listing,
 * download behind a redirect, and the change feed. [autoCreateFolders] = false behaves like a service that wants
 * the folders made first.
 */
class FakeOneDrive(private val autoCreateFolders: Boolean = true) : AutoCloseable {
    private class Node(val id: String, var path: String, val folder: Boolean, var data: ByteArray = ByteArray(0), var version: Int = 1)

    private val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
    private val nodes = LinkedHashMap<String, Node>() // by lower-case path
    private val changes = ArrayList<Pair<Node, Boolean>>() // node, deleted
    private var nextId = 1
    val requests = ArrayList<String>()
    var failWith: Int? = null
    var expireDelta = false
    var pageSize = 100

    val url: String get() = "http://127.0.0.1:${server.address.port}/v1.0"
    fun client(): GraphClient = GraphClient({ "token" }, url)

    init {
        server.createContext("/") { x ->
            try {
                handle(x)
            } catch (e: Exception) {
                respond(x, 500, e.toString())
            }
        }
        server.start()
    }

    override fun close() = server.stop(0)

    // What the desktop (or the test) does to the drive directly.
    @Synchronized
    fun put(path: String, text: String) = put(path, text.toByteArray())

    @Synchronized
    fun put(path: String, data: ByteArray) {
        mkdirs(path.substringBeforeLast('/', ""))
        val node = nodes[path.lowercase()]
        if (node != null) {
            node.data = data
            node.version++
            changes += node to false
        } else {
            add(Node("id${nextId++}", path, false, data))
        }
    }

    @Synchronized
    fun delete(path: String) {
        val node = nodes.remove(path.lowercase()) ?: return
        nodes.keys.filter { it.startsWith(path.lowercase() + "/") }.forEach(nodes::remove)
        changes += node to true
    }

    @Synchronized
    fun rename(from: String, to: String) {
        val node = nodes.remove(from.lowercase()) ?: return
        node.path = to
        nodes[to.lowercase()] = node
        changes += node to false
    }

    @Synchronized
    fun text(path: String): String? = nodes[path.lowercase()]?.takeIf { !it.folder }?.data?.toString(Charsets.UTF_8)

    @Synchronized
    fun files(): List<String> = nodes.values.filter { !it.folder }.map { it.path }.sorted()

    private fun add(node: Node) {
        nodes[node.path.lowercase()] = node
        changes += node to false
    }

    private fun mkdirs(path: String) {
        var parent = ""
        for (segment in path.split('/').filter { it.isNotEmpty() }) {
            parent = if (parent.isEmpty()) segment else "$parent/$segment"
            if (!nodes.containsKey(parent.lowercase())) add(Node("id${nextId++}", parent, true))
        }
    }

    @Synchronized
    private fun handle(x: HttpExchange) {
        val raw = x.requestURI.rawPath
        val query = x.requestURI.rawQuery.orEmpty()
        requests += "${x.requestMethod} $raw"
        if (raw.startsWith("/raw/")) {
            // The pre-signed download address: reached without our token.
            if (x.requestHeaders.containsKey("Authorization")) return respond(x, 400, "token leaked")
            val node = nodes[decode(raw.removePrefix("/raw/")).lowercase()] ?: return respond(x, 404, "")
            return respond(x, 200, node.data)
        }
        if (x.requestHeaders.getFirst("Authorization") != "Bearer token") return respond(x, 401, "")
        failWith?.let { return respond(x, it, "") }

        val rest = raw.removePrefix("/v1.0/me/drive/")
        if (rest == "root/children") return children(x, "")
        if (!rest.startsWith("root:/")) return respond(x, 400, "unsupported $raw")
        val path = decode(rest.removePrefix("root:/").substringBeforeLast(":/"))
        when (val action = rest.substringAfterLast(":/")) {
            "content" -> if (x.requestMethod == "PUT") {
                val parent = path.substringBeforeLast('/', "")
                if (parent.isNotEmpty() && !nodes.containsKey(parent.lowercase())) {
                    if (!autoCreateFolders) return respond(x, 404, "")
                    mkdirs(parent)
                }
                if (nodes.containsKey(path.lowercase())) return respond(x, 409, """{"error":{"code":"nameAlreadyExists"}}""")
                if (!query.contains("conflictBehavior=fail")) return respond(x, 400, "would replace")
                add(Node("id${nextId++}", path, false, x.requestBody.readBytes()))
                respond(x, 201, "{}")
            } else {
                val node = nodes[path.lowercase()] ?: return respond(x, 404, "")
                x.responseHeaders.add("Location", "http://127.0.0.1:${server.address.port}/raw/${GraphClient.encodePath(node.path)}")
                respond(x, 302, "")
            }
            "children" -> if (x.requestMethod == "POST") {
                val name = JSONObject(x.requestBody.readBytes().toString(Charsets.UTF_8)).getString("name")
                if (!nodes.containsKey(path.lowercase())) return respond(x, 404, "")
                if (nodes.containsKey("$path/$name".lowercase())) return respond(x, 409, "")
                add(Node("id${nextId++}", "$path/$name", true))
                respond(x, 201, "{}")
            } else {
                children(x, path)
            }
            "delta" -> delta(x, path, query)
            else -> respond(x, 400, "unsupported $action")
        }
    }

    private fun children(x: HttpExchange, path: String) {
        if (x.requestMethod == "POST") {
            val name = JSONObject(x.requestBody.readBytes().toString(Charsets.UTF_8)).getString("name")
            if (nodes.containsKey(name.lowercase())) return respond(x, 409, "")
            add(Node("id${nextId++}", name, true))
            return respond(x, 201, "{}")
        }
        if (path.isNotEmpty() && nodes[path.lowercase()]?.folder != true) return respond(x, 404, "")
        val value = JSONArray()
        nodes.values.filter { it.path.substringBeforeLast('/', "").equals(path, ignoreCase = true) }.forEach { value.put(json(it, false)) }
        respond(x, 200, JSONObject().put("value", value).toString())
    }

    private fun delta(x: HttpExchange, path: String, query: String) {
        if (expireDelta && query.contains("token=")) {
            expireDelta = false
            return respond(x, 410, "")
        }
        val from = Regex("token=(\\d+)").find(query)?.groupValues?.get(1)?.toInt() ?: -1
        // The first listing is the current state; later ones are the changes since the token.
        val all = if (from < 0) {
            nodes.values.map { it to false }
        } else {
            changes.drop(from).distinctBy { it.first.id to it.second }
        }.filter { (n, deleted) -> deleted || n.path.equals(path, true) || n.path.startsWith("$path/", true) }
        val skip = Regex("skip=(\\d+)").find(query)?.groupValues?.get(1)?.toInt() ?: 0
        val value = JSONArray()
        all.drop(skip).take(pageSize).forEach { (n, deleted) -> value.put(json(n, deleted)) }
        val o = JSONObject().put("value", value)
        val base = "$url/me/drive/root:/${GraphClient.encodePath(path)}:/delta"
        if (skip + pageSize < all.size) {
            o.put("@odata.nextLink", "$base?${if (from >= 0) "token=$from&" else ""}skip=${skip + pageSize}")
        } else {
            o.put("@odata.deltaLink", "$base?token=${changes.size}")
        }
        respond(x, 200, o.toString())
    }

    private fun json(n: Node, deleted: Boolean): JSONObject {
        val o = JSONObject().put("id", n.id)
        if (deleted) return o.put("deleted", JSONObject().put("state", "deleted"))
        val parent = n.path.substringBeforeLast('/', "")
        o.put("name", n.path.substringAfterLast('/'))
        o.put("parentReference", JSONObject().put("path", "/drive/root:" + if (parent.isEmpty()) "" else "/" + GraphClient.encodePath(parent)))
        o.put("size", n.data.size).put("lastModifiedDateTime", "2026-11-17T13:00:00Z").put("cTag", "c:${n.id}:${n.version}")
        o.put(if (n.folder) "folder" else "file", JSONObject())
        return o
    }

    private fun decode(path: String) = URLDecoder.decode(path.replace("+", "%2B"), "UTF-8")

    private fun respond(x: HttpExchange, status: Int, body: String) = respond(x, status, body.toByteArray())

    private fun respond(x: HttpExchange, status: Int, body: ByteArray) {
        x.requestBody.readBytes()
        if (body.isEmpty()) {
            x.sendResponseHeaders(status, -1)
        } else {
            x.sendResponseHeaders(status, body.size.toLong())
            x.responseBody.write(body)
        }
        x.close()
    }
}
