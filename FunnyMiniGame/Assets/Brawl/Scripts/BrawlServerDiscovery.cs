using System;
using System.Collections.Generic;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

namespace Brawl
{
    public struct BrawlServerRequest : NetworkMessage { }

    public struct BrawlServerResponse : NetworkMessage
    {
        public IPEndPoint EndPoint { get; set; }
        public Uri uri;
        public long serverId;
        public string serverName;
        public ushort port;
        public int playerCount;
    }

    public sealed class BrawlFoundServer
    {
        public long ServerId;
        public string ServerName;
        public string Address;
        public Uri Uri;
        public ushort Port;
        public int PlayerCount;
        public float LastSeen;
    }

    /// <summary>
    /// 局域网房间广播。开房后应答名称，未连接时搜索并列出房间。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlServerDiscovery : NetworkDiscoveryBase<BrawlServerRequest, BrawlServerResponse>
    {
        public const string NamePrefsKey = "BrawlServerName";
        const long Handshake = 0x46554E4E594D4732L;
        const float StaleSeconds = 8f;

        public static BrawlServerDiscovery Instance { get; private set; }

        public string ServerName = "新房间";

        readonly Dictionary<long, BrawlFoundServer> found = new Dictionary<long, BrawlFoundServer>();
        readonly List<BrawlFoundServer> snapshot = new List<BrawlFoundServer>();
        readonly Queue<BrawlServerResponse> pending = new Queue<BrawlServerResponse>();
        readonly object foundLock = new object();

        public bool IsSearching => clientUdpClient != null;
        public bool IsAdvertising => serverUdpClient != null;

        public static string DefaultServerName()
        {
            string saved = PlayerPrefs.GetString(NamePrefsKey, "");
            if (!string.IsNullOrWhiteSpace(saved))
                return saved.Trim();

            string user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user))
                user = "玩家";
            return user + "的房间";
        }

        public static void RememberServerName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                PlayerPrefs.SetString(NamePrefsKey, name.Trim());
        }

        public static BrawlServerDiscovery Ensure(NetworkManager manager)
        {
            if (manager == null) return Instance;
            BrawlServerDiscovery discovery = manager.GetComponent<BrawlServerDiscovery>();
            if (discovery == null)
                discovery = manager.gameObject.AddComponent<BrawlServerDiscovery>();
            discovery.secretHandshake = Handshake;
            if (discovery.transport == null)
                discovery.transport = manager.transport != null ? manager.transport : Transport.active;
            Instance = discovery;
            return discovery;
        }

        public IReadOnlyList<BrawlFoundServer> CopyFoundServers()
        {
            PruneStale();
            snapshot.Clear();
            lock (foundLock)
            {
                foreach (BrawlFoundServer server in found.Values)
                    snapshot.Add(server);
            }

            snapshot.Sort((a, b) => string.CompareOrdinal(a.ServerName, b.ServerName));
            return snapshot;
        }

        public void ClearFound()
        {
            lock (foundLock)
                found.Clear();
        }

        public void BeginBrowse()
        {
            if (!SupportedOnThisPlatform) return;
            if (NetworkServer.active || NetworkClient.active) return;
            if (IsSearching) return;
            ClearFound();
            secretHandshake = Handshake;
            StartDiscovery();
        }

        public void Advertise(string roomName)
        {
            ServerName = string.IsNullOrWhiteSpace(roomName) ? DefaultServerName() : roomName.Trim();
            RememberServerName(ServerName);
            secretHandshake = Handshake;
            if (transport == null)
                transport = Transport.active;
            AdvertiseServer();
        }

        public string ListFingerprint()
        {
            IReadOnlyList<BrawlFoundServer> servers = CopyFoundServers();
            int hash = servers.Count;
            for (int i = 0; i < servers.Count; i++)
            {
                BrawlFoundServer server = servers[i];
                hash = (hash * 397) ^ server.ServerId.GetHashCode();
                hash = (hash * 397) ^ (server.ServerName != null ? server.ServerName.GetHashCode() : 0);
                hash = (hash * 397) ^ server.PlayerCount;
            }

            return hash.ToString();
        }

#if UNITY_EDITOR
        public override void OnValidate()
        {
            secretHandshake = Handshake;
            base.OnValidate();
            secretHandshake = Handshake;
        }
#endif

        void Awake()
        {
            secretHandshake = Handshake;
            Instance = this;
            if (string.IsNullOrWhiteSpace(ServerName))
                ServerName = DefaultServerName();
        }

        void OnEnable()
        {
            Instance = this;
            secretHandshake = Handshake;
        }

        void Update()
        {
            lock (foundLock)
            {
                while (pending.Count > 0)
                    ApplyFound(pending.Dequeue());
            }

            PruneStale();
        }

        void PruneStale()
        {
            lock (foundLock)
            {
                if (found.Count == 0) return;
                List<long> stale = null;
                foreach (KeyValuePair<long, BrawlFoundServer> pair in found)
                {
                    if (Time.unscaledTime - pair.Value.LastSeen <= StaleSeconds) continue;
                    if (stale == null) stale = new List<long>();
                    stale.Add(pair.Key);
                }

                if (stale == null) return;
                for (int i = 0; i < stale.Count; i++)
                    found.Remove(stale[i]);
            }
        }

        protected override BrawlServerResponse ProcessRequest(BrawlServerRequest request, IPEndPoint endpoint)
        {
            if (transport == null)
                transport = Transport.active;
            if (transport == null)
                return default;

            ushort port = 7777;
            if (transport is PortTransport portTransport)
                port = portTransport.Port;

            return new BrawlServerResponse
            {
                serverId = ServerId,
                uri = transport.ServerUri(),
                serverName = string.IsNullOrWhiteSpace(ServerName) ? DefaultServerName() : ServerName,
                port = port,
                playerCount = NetworkServer.connections != null ? NetworkServer.connections.Count : 0
            };
        }

        protected override BrawlServerRequest GetRequest() => new BrawlServerRequest();

        protected override void ProcessResponse(BrawlServerResponse response, IPEndPoint endpoint)
        {
            response.EndPoint = endpoint;
            if (response.uri != null)
            {
                var realUri = new UriBuilder(response.uri)
                {
                    Host = endpoint.Address.ToString()
                };
                response.uri = realUri.Uri;
            }

            lock (foundLock)
                pending.Enqueue(response);
        }

        void ApplyFound(BrawlServerResponse response)
        {
            string address = response.EndPoint != null ? response.EndPoint.Address.ToString() : "";
            if (response.uri != null && string.IsNullOrEmpty(address))
                address = response.uri.Host;
            string name = string.IsNullOrWhiteSpace(response.serverName) ? address : response.serverName;
            if (!found.TryGetValue(response.serverId, out BrawlFoundServer server))
            {
                server = new BrawlFoundServer { ServerId = response.serverId };
                found[response.serverId] = server;
            }

            server.ServerName = name;
            server.Address = address;
            server.Uri = response.uri;
            server.Port = response.port;
            server.PlayerCount = response.playerCount;
            server.LastSeen = Time.unscaledTime;
        }
    }
}
