using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Mirror;
using UnityEngine;

namespace Brawl
{
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
    /// 房间发现：主机定时宣告 + 客户端询问。
    /// 除本网卡子网广播外，再发同 B 段（第三段不同）广播、组播，以及对已知 IP 单播，
    /// 避免公司里 10.21.26.x 和 10.21.139.x 这种跨网段刷不到。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrawlServerDiscovery : MonoBehaviour
    {
        public const string NamePrefsKey = "BrawlServerName";
        const string RecentPrefsKey = "BrawlRecentServers";
        const long Handshake = 0x46554E4E594D4732L;
        const int QueryPort = 47777;
        const int AnnouncePort = 47778;
        const float PulseInterval = 1.25f;
        const float StaleSeconds = 8f;
        const byte TypeQuery = 1;
        const byte TypeAnnounce = 2;
        const int MulticastTtl = 32;
        const int MaxUnicastTargets = 8;
        static readonly IPAddress MulticastGroup = IPAddress.Parse("239.55.77.78");

        public static BrawlServerDiscovery Instance { get; private set; }

        public string ServerName = "新房间";
        public bool IsSearching { get; private set; }
        public bool IsAdvertising { get; private set; }
        public string BrowseHint { get; private set; } = "正在搜索房间…";

        long serverId;
        UdpClient queryListener;
        UdpClient announceListener;
        UdpClient querySender;
        float nextPulse;
        float browsingSince;

        readonly Dictionary<long, BrawlFoundServer> found = new Dictionary<long, BrawlFoundServer>();
        readonly List<BrawlFoundServer> snapshot = new List<BrawlFoundServer>();
        readonly Queue<PendingPacket> pending = new Queue<PendingPacket>();
        readonly List<string> unicastTargets = new List<string>();
        readonly object gate = new object();

        struct PendingPacket
        {
            public byte Type;
            public long ServerId;
            public ushort Port;
            public int PlayerCount;
            public string ServerName;
            public IPEndPoint EndPoint;
        }

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

        public static string LocalIpv4Display()
        {
            List<LanNic> locals = CollectLocalIpv4();
            if (locals.Count == 0)
                return "未知";

            var ips = new List<string>(locals.Count);
            string preferred = TryOutboundIpv4();
            if (!string.IsNullOrEmpty(preferred))
                ips.Add(preferred);

            for (int i = 0; i < locals.Count; i++)
            {
                string ip = locals[i].Address.ToString();
                if (!ips.Contains(ip))
                    ips.Add(ip);
            }

            if (ips.Count == 1)
                return ips[0];
            return ips[0] + "  /  " + ips[1];
        }

        static string TryOutboundIpv4()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint
                        && endPoint.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(endPoint.Address)
                        && !IsLinkLocal(endPoint.Address.ToString()))
                        return endPoint.Address.ToString();
                }
            }
            catch
            {
            }

            return "";
        }

        public void AddUnicastTarget(string address)
        {
            if (!TryNormalizeIpv4(address, out string ip)) return;
            if (IsLoopback(ip) || IsLinkLocal(ip)) return;

            lock (gate)
            {
                if (unicastTargets.Count > 0 && unicastTargets[0] == ip)
                    return;
                unicastTargets.Remove(ip);
                unicastTargets.Insert(0, ip);
                while (unicastTargets.Count > MaxUnicastTargets)
                    unicastTargets.RemoveAt(unicastTargets.Count - 1);
                SaveRecentUnlocked();
            }
        }

        public static BrawlServerDiscovery Ensure(NetworkManager manager)
        {
            if (manager == null) return Instance;
            BrawlServerDiscovery discovery = manager.GetComponent<BrawlServerDiscovery>();
            if (discovery == null)
                discovery = manager.gameObject.AddComponent<BrawlServerDiscovery>();
            Instance = discovery;
            return discovery;
        }

        public IReadOnlyList<BrawlFoundServer> CopyFoundServers()
        {
            PruneStale();
            snapshot.Clear();
            lock (gate)
            {
                foreach (BrawlFoundServer server in found.Values)
                    snapshot.Add(server);
            }

            snapshot.Sort((a, b) => string.CompareOrdinal(a.ServerName, b.ServerName));
            return snapshot;
        }

        public void ClearFound()
        {
            lock (gate)
                found.Clear();
            BrowseHint = "正在搜索房间…";
        }

        public void BeginBrowse()
        {
            if (NetworkServer.active || NetworkClient.active) return;
            if (IsSearching) return;

            StopSockets();
            ClearFound();
            browsingSince = Time.unscaledTime;
            BrowseHint = "正在搜索房间…";
            announceListener = BindSocket(AnnouncePort, true);
            querySender = BindSocket(0, true);
            JoinMulticast(announceListener);
            JoinMulticast(querySender);
            if (announceListener == null && querySender == null)
            {
                BrowseHint = "无法监听局域网，请检查防火墙后点刷新";
                return;
            }

            IsSearching = true;
            Listen(announceListener);
            Listen(querySender);
            nextPulse = 0f;
            BroadcastDiscoveryRequest();
        }

        public void Advertise(string roomName)
        {
            ServerName = string.IsNullOrWhiteSpace(roomName) ? DefaultServerName() : roomName.Trim();
            RememberServerName(ServerName);
            if (serverId == 0)
                serverId = NewServerId();

            StopSockets();
            queryListener = BindSocket(QueryPort, true);
            JoinMulticast(queryListener);
            IsAdvertising = queryListener != null;
            IsSearching = false;
            if (queryListener != null)
                Listen(queryListener);
            nextPulse = 0f;
            PulseAdvertise();
        }

        public void StopDiscovery()
        {
            IsSearching = false;
            IsAdvertising = false;
            StopSockets();
        }

        public void BroadcastDiscoveryRequest()
        {
            if (IsAdvertising)
            {
                PulseAdvertise();
                return;
            }

            if (!IsSearching)
                BeginBrowse();
            if (querySender == null && announceListener == null)
                return;

            byte[] query = WritePacket(TypeQuery, 0, 0, 0, "");
            SendToLan(querySender, query, QueryPort);
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

        void Awake()
        {
            Instance = this;
            serverId = NewServerId();
            if (string.IsNullOrWhiteSpace(ServerName))
                ServerName = DefaultServerName();
            LoadRecent();
        }

        void OnEnable()
        {
            Instance = this;
        }

        void OnDisable()
        {
            StopDiscovery();
        }

        void OnDestroy()
        {
            StopDiscovery();
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            lock (gate)
            {
                while (pending.Count > 0)
                    HandlePacket(pending.Dequeue());
            }

            PruneStale();
            UpdateBrowseHint();

            if (Time.unscaledTime < nextPulse) return;
            nextPulse = Time.unscaledTime + PulseInterval;
            if (IsAdvertising)
                PulseAdvertise();
            else if (IsSearching)
                BroadcastDiscoveryRequest();
        }

        void UpdateBrowseHint()
        {
            if (!IsSearching) return;
            if (found.Count > 0) return;
            BrowseHint = Time.unscaledTime - browsingSince > 4f
                ? "未发现房间。跨网段请在下方填主机 IP 后点刷新，或直接加入"
                : "正在搜索房间…";
        }

        void PulseAdvertise()
        {
            if (!NetworkServer.active && !IsAdvertising) return;
            byte[] announce = WriteAnnounce();
            SendToLan(queryListener, announce, AnnouncePort);
            SendToLan(queryListener, announce, QueryPort);
        }

        void HandlePacket(PendingPacket packet)
        {
            if (packet.Type == TypeQuery)
            {
                if (!IsAdvertising || packet.EndPoint == null) return;
                byte[] reply = WriteAnnounce();
                TrySend(queryListener, reply, packet.EndPoint);
                return;
            }

            if (packet.Type != TypeAnnounce) return;
            if (packet.ServerId == 0 || packet.ServerId == serverId) return;
            if (IsAdvertising) return;

            string address = packet.EndPoint != null ? packet.EndPoint.Address.ToString() : "";
            if (IsLoopback(address))
                address = "127.0.0.1";

            ushort port = packet.Port > 0 ? packet.Port : (ushort)7777;
            if (!found.TryGetValue(packet.ServerId, out BrawlFoundServer server))
            {
                server = new BrawlFoundServer { ServerId = packet.ServerId };
                found[packet.ServerId] = server;
            }

            server.ServerName = string.IsNullOrWhiteSpace(packet.ServerName) ? address : packet.ServerName;
            server.Address = address;
            server.Port = port;
            server.PlayerCount = Mathf.Max(1, packet.PlayerCount);
            server.Uri = new Uri("kcp://" + address + ":" + port);
            server.LastSeen = Time.unscaledTime;
            AddUnicastTarget(address);
        }

        void PruneStale()
        {
            lock (gate)
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

        byte[] WriteAnnounce()
        {
            ushort port = 7777;
            if (Transport.active is PortTransport portTransport)
                port = portTransport.Port;
            int players = NetworkServer.connections != null ? NetworkServer.connections.Count : 1;
            return WritePacket(TypeAnnounce, serverId, port, players, ServerName);
        }

        static byte[] WritePacket(byte type, long id, ushort port, int players, string name)
        {
            using (var stream = new MemoryStream(64))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Handshake);
                writer.Write(type);
                writer.Write(id);
                writer.Write(port);
                writer.Write(players);
                writer.Write(name ?? "");
                return stream.ToArray();
            }
        }

        static bool TryReadPacket(byte[] data, IPEndPoint endpoint, out PendingPacket packet)
        {
            packet = default;
            if (data == null || data.Length < 14) return false;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt64() != Handshake) return false;
                    packet.Type = reader.ReadByte();
                    packet.ServerId = reader.ReadInt64();
                    packet.Port = reader.ReadUInt16();
                    packet.PlayerCount = reader.ReadInt32();
                    packet.ServerName = reader.ReadString();
                    packet.EndPoint = endpoint;
                    return packet.Type == TypeQuery || packet.Type == TypeAnnounce;
                }
            }
            catch
            {
                return false;
            }
        }

        void Listen(UdpClient client)
        {
            if (client == null) return;
            ReceiveLoop(client);
        }

        async void ReceiveLoop(UdpClient client)
        {
            while (client != null)
            {
                try
                {
                    UdpReceiveResult result = await client.ReceiveAsync();
                    if (!TryReadPacket(result.Buffer, result.RemoteEndPoint, out PendingPacket packet))
                        continue;
                    lock (gate)
                        pending.Enqueue(packet);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (client == null) break;
                }
                catch (Exception)
                {
                    if (client == null) break;
                }
            }
        }

        void SendToLan(UdpClient preferred, byte[] data, int destPort)
        {
            List<IPEndPoint> destinations = CollectDestinations(destPort);
            for (int i = 0; i < destinations.Count; i++)
                TrySend(preferred, data, destinations[i]);

            List<LanNic> locals = CollectLocalIpv4();
            for (int i = 0; i < locals.Count; i++)
            {
                IPAddress local = locals[i].Address;
                for (int j = 0; j < destinations.Count; j++)
                    TrySendFrom(local, data, destinations[j]);
            }
        }

        List<IPEndPoint> CollectDestinations(int destPort)
        {
            var seen = new HashSet<string>();
            var list = new List<IPEndPoint>();
            AddDest(list, seen, IPAddress.Broadcast, destPort);
            AddDest(list, seen, IPAddress.Loopback, destPort);
            AddDest(list, seen, MulticastGroup, destPort);

            List<LanNic> locals = CollectLocalIpv4();
            for (int i = 0; i < locals.Count; i++)
            {
                LanNic nic = locals[i];
                AddDest(list, seen, ToBroadcast(nic.Address, nic.Mask), destPort);
                AddWideBroadcasts(list, seen, nic.Address, destPort);
            }

            lock (gate)
            {
                for (int i = 0; i < unicastTargets.Count; i++)
                {
                    if (!IPAddress.TryParse(unicastTargets[i], out IPAddress ip)) continue;
                    AddDest(list, seen, ip, destPort);
                }
            }

            return list;
        }

        static void AddWideBroadcasts(List<IPEndPoint> list, HashSet<string> seen, IPAddress address, int destPort)
        {
            if (address == null) return;
            byte[] ip = address.GetAddressBytes();
            if (ip.Length != 4) return;

            // 10.21.26.x 与 10.21.139.x 同属 10.21.0.0/16，补发第三段通配广播。
            if (ip[0] == 10)
                AddDest(list, seen, new IPAddress(new byte[] { 10, ip[1], 255, 255 }), destPort);
            else if (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31)
                AddDest(list, seen, new IPAddress(new byte[] { 172, ip[1], 255, 255 }), destPort);
        }

        static void AddDest(List<IPEndPoint> list, HashSet<string> seen, IPAddress address, int destPort)
        {
            if (address == null) return;
            string key = address + ":" + destPort;
            if (!seen.Add(key)) return;
            list.Add(new IPEndPoint(address, destPort));
        }

        static void TrySend(UdpClient client, byte[] data, IPEndPoint dest)
        {
            if (client == null || data == null || dest == null) return;
            try
            {
                client.Send(data, data.Length, dest);
            }
            catch
            {
            }
        }

        static void TrySendFrom(IPAddress local, byte[] data, IPEndPoint dest)
        {
            if (local == null || data == null || dest == null) return;
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(new IPEndPoint(local, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                udp.EnableBroadcast = true;
                PrepareMulticast(udp);
                udp.Send(data, data.Length, dest);
            }
            catch
            {
            }
            finally
            {
                try { udp?.Close(); } catch { }
            }
        }

        static UdpClient BindSocket(int port, bool broadcast)
        {
            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (broadcast)
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                udp.EnableBroadcast = broadcast;
                udp.MulticastLoopback = true;
                PrepareMulticast(udp);
                return udp;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("BrawlServerDiscovery: 绑定 UDP " + port + " 失败: " + ex.Message);
                return null;
            }
        }

        static void PrepareMulticast(UdpClient udp)
        {
            if (udp == null) return;
            try
            {
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, MulticastTtl);
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            }
            catch
            {
            }
        }

        static void JoinMulticast(UdpClient udp)
        {
            if (udp == null) return;
            PrepareMulticast(udp);
            try
            {
                udp.JoinMulticastGroup(MulticastGroup);
            }
            catch
            {
            }

            List<LanNic> locals = CollectLocalIpv4();
            for (int i = 0; i < locals.Count; i++)
            {
                try
                {
                    udp.JoinMulticastGroup(MulticastGroup, locals[i].Address);
                }
                catch
                {
                }
            }
        }

        struct LanNic
        {
            public IPAddress Address;
            public IPAddress Mask;
        }

        static List<LanNic> CollectLocalIpv4()
        {
            var list = new List<LanNic>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(addr.Address)) continue;
                        if (IsLinkLocal(addr.Address.ToString())) continue;
                        list.Add(new LanNic
                        {
                            Address = addr.Address,
                            Mask = TryGetMask(addr)
                        });
                    }
                }
            }
            catch
            {
            }

            return list;
        }

        static IPAddress TryGetMask(UnicastIPAddressInformation info)
        {
            try
            {
                int prefix = info.PrefixLength;
                if (prefix > 0 && prefix <= 32)
                    return PrefixToMask(prefix);
            }
            catch
            {
            }

            try
            {
                if (info.IPv4Mask != null)
                    return info.IPv4Mask;
            }
            catch
            {
            }

            return IPAddress.Parse("255.255.255.0");
        }

        static IPAddress PrefixToMask(int prefix)
        {
            uint bits = prefix >= 32 ? uint.MaxValue : prefix <= 0 ? 0 : uint.MaxValue << (32 - prefix);
            byte[] raw = BitConverter.GetBytes(bits);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(raw);
            return new IPAddress(raw);
        }

        static IPAddress ToBroadcast(IPAddress address, IPAddress mask)
        {
            if (address == null) return IPAddress.Broadcast;
            byte[] ip = address.GetAddressBytes();
            byte[] netmask = (mask ?? IPAddress.Parse("255.255.255.0")).GetAddressBytes();
            if (ip.Length != 4 || netmask.Length != 4) return IPAddress.Broadcast;
            byte[] broadcast = new byte[4];
            for (int i = 0; i < 4; i++)
                broadcast[i] = (byte)(ip[i] | ~netmask[i]);
            return new IPAddress(broadcast);
        }

        static bool IsLoopback(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            string trimmed = address.Trim();
            return trimmed == "127.0.0.1" || trimmed == "::1" || trimmed == "localhost";
        }

        static bool IsLinkLocal(string address)
        {
            return !string.IsNullOrWhiteSpace(address) && address.StartsWith("169.254.");
        }

        static bool TryNormalizeIpv4(string address, out string ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(address)) return false;
            string trimmed = address.Trim();
            int colon = trimmed.IndexOf(':');
            if (colon > 0 && trimmed.IndexOf('.') > 0)
                trimmed = trimmed.Substring(0, colon);
            if (!IPAddress.TryParse(trimmed, out IPAddress parsed)) return false;
            if (parsed.AddressFamily != AddressFamily.InterNetwork) return false;
            ip = parsed.ToString();
            return true;
        }

        void LoadRecent()
        {
            string raw = PlayerPrefs.GetString(RecentPrefsKey, "");
            if (string.IsNullOrWhiteSpace(raw)) return;
            string[] parts = raw.Split(',');
            lock (gate)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!TryNormalizeIpv4(parts[i], out string ip)) continue;
                    if (IsLoopback(ip) || IsLinkLocal(ip)) continue;
                    if (unicastTargets.Contains(ip)) continue;
                    unicastTargets.Add(ip);
                    if (unicastTargets.Count >= MaxUnicastTargets) break;
                }
            }
        }

        void SaveRecentUnlocked()
        {
            PlayerPrefs.SetString(RecentPrefsKey, string.Join(",", unicastTargets.ToArray()));
        }

        static long NewServerId()
        {
            int a = UnityEngine.Random.Range(1, int.MaxValue);
            int b = UnityEngine.Random.Range(1, int.MaxValue);
            return a + ((long)b << 32);
        }

        void StopSockets()
        {
            CloseSocket(ref queryListener);
            CloseSocket(ref announceListener);
            CloseSocket(ref querySender);
        }

        static void CloseSocket(ref UdpClient client)
        {
            if (client == null) return;
            UdpClient closing = client;
            client = null;
            try { closing.Close(); } catch { }
        }
    }
}
