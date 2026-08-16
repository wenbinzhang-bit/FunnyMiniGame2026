using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Brawl
{
    /// <summary>
    /// 替换 Mirror 默认 IMGUI 联机条：左下角卡片，按钮和提示与对局 HUD 同一套风格。
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class BrawlNetworkHud : MonoBehaviour
    {
        const float PanelWidth = 372f;
        const float DisconnectedHeight = 428f;
        const float ConnectedHeight = 124f;
        const float ConnectingHeight = 132f;

        static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.88f);
        static readonly Color AccentColor = new Color(1f, 0.84f, 0.28f, 1f);
        static readonly Color HostColor = new Color(0.16f, 0.72f, 0.38f, 0.96f);
        static readonly Color JoinColor = new Color(0.28f, 0.62f, 0.92f, 0.96f);
        static readonly Color ServerColor = new Color(0.28f, 0.30f, 0.34f, 0.96f);
        static readonly Color StopColor = new Color(0.82f, 0.24f, 0.22f, 0.96f);
        static readonly Color FieldColor = new Color(0.12f, 0.13f, 0.16f, 0.96f);
        static readonly Color HintColor = new Color(0.86f, 0.88f, 0.90f, 0.78f);
        static readonly Color IdleDot = new Color(0.62f, 0.64f, 0.68f, 1f);
        static readonly Color LiveDot = new Color(0.28f, 0.86f, 0.46f, 1f);
        static readonly Color WaitDot = new Color(1f, 0.78f, 0.22f, 1f);

        RectTransform panel;
        GameObject disconnectedRoot;
        GameObject connectingRoot;
        GameObject connectedRoot;
        Text hintText;
        Text statusText;
        Image statusDot;
        InputField nameField;
        InputField addressField;
        InputField portField;
        Text botValue;
        Text connectingLabel;
        Text emptyListText;
        RectTransform serverListContent;
        Button stopHostButton;
        Button stopClientButton;
        Font font;
        NetworkManager manager;
        BrawlServerDiscovery discovery;
        string lastListFingerprint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<BrawlNetworkHud>() != null) return;
            var go = new GameObject("BrawlNetworkHud");
            if (BrawlSession.Instance != null)
                go.transform.SetParent(BrawlSession.Instance.transform, false);
            else
                DontDestroyOnLoad(go);
            go.AddComponent<BrawlNetworkHud>();
        }

        void Awake()
        {
            HideDefaultMirrorHud();
            font = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft YaHei",
                "微软雅黑",
                "PingFang SC",
                "SimHei",
                "Arial"
            }, 18);
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            BuildCanvas();
        }

        void LateUpdate()
        {
            HideDefaultMirrorHud();
            manager = NetworkManager.singleton;
            if (manager == null || panel == null) return;

            bool server = NetworkServer.active;
            bool client = NetworkClient.isConnected;
            bool connecting = NetworkClient.active && !client && !server;

            disconnectedRoot.SetActive(!server && !client && !connecting);
            connectingRoot.SetActive(connecting);
            connectedRoot.SetActive(server || client);

            float height = connecting ? ConnectingHeight : server || client ? ConnectedHeight : DisconnectedHeight;
            panel.sizeDelta = new Vector2(PanelWidth, height);

            if (addressField != null && !addressField.isFocused)
                addressField.text = string.IsNullOrEmpty(manager.networkAddress) ? "localhost" : manager.networkAddress;
            else if (addressField != null)
                manager.networkAddress = addressField.text.Trim();

            if (Transport.active is PortTransport portTransport)
            {
                if (portField != null && !portField.isFocused)
                    portField.text = portTransport.Port.ToString();
                else if (portField != null && ushort.TryParse(portField.text, out ushort port))
                    portTransport.Port = port;
            }

            if (connecting)
            {
                BindHeader("连接中", WaitDot, "正在联系主机，可随时取消");
                connectingLabel.text = $"正在连接  {manager.networkAddress}";
            }
            else if (server && client)
            {
                string room = discovery != null ? discovery.ServerName : "房间";
                int bots = BrawlBotBrain.AliveCount;
                BindHeader("主机中", LiveDot, bots > 0
                    ? $"{room} 已开启  ·  {TransportName()}  ·  Bot {bots}"
                    : $"{room} 已开启，同伴可在列表里点进来  ·  {TransportName()}");
            }
            else if (server)
            {
                string room = discovery != null ? discovery.ServerName : "房间";
                BindHeader("服务器", LiveDot, $"{room}  ·  仅服务器  ·  {TransportName()}");
            }
            else if (client)
            {
                BindHeader("已加入", LiveDot, $"已连上 {manager.networkAddress}，Esc 可点按钮");
            }
            else
            {
                BindHeader("未连接", IdleDot, "同网段会自动列出；跨网段请填主机 IP 后刷新或加入");
                EnsureBrowsing();
                RefreshServerList();
            }

            if (botValue != null)
            {
                botValue.text = BrawlBotLobby.Instance != null
                    ? BrawlBotLobby.Instance.BotCount.ToString()
                    : "0";
            }

            if (stopHostButton != null)
                stopHostButton.gameObject.SetActive(server && client);
            if (stopClientButton != null)
            {
                Text stopLabel = stopClientButton.GetComponentInChildren<Text>();
                if (server && client)
                {
                    if (stopLabel != null) stopLabel.text = "退出自己";
                    LayoutStopPair();
                }
                else if (client)
                {
                    if (stopLabel != null) stopLabel.text = "退出房间";
                    LayoutStopSingle();
                }
                else if (server)
                {
                    if (stopLabel != null) stopLabel.text = "停止服务器";
                    LayoutStopSingle();
                }
            }
        }

        void BindHeader(string status, Color dot, string hint)
        {
            statusDot.color = dot;
            statusText.text = status;
            statusText.color = status == "未连接" ? HintColor : dot;
            hintText.text = hint;
        }

        void LayoutStopPair()
        {
            SetRect(stopClientButton.transform as RectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 14f), new Vector2(-8f, 32f));
        }

        void LayoutStopSingle()
        {
            SetRect(stopClientButton.transform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(-32f, 32f));
        }

        void OnHost()
        {
            if (manager == null) return;
            ApplyRoomName();
            manager.StartHost();
        }

        void OnJoin()
        {
            if (manager == null || addressField == null) return;
            manager.networkAddress = addressField.text.Trim();
            RememberJoinAddress(manager.networkAddress);
            if (discovery != null)
                discovery.StopDiscovery();
            manager.StartClient();
        }

        void OnJoinFound(BrawlFoundServer server)
        {
            if (manager == null || server == null) return;
            RememberJoinAddress(server.Address);
            if (discovery != null)
                discovery.StopDiscovery();
            if (server.Uri != null)
            {
                manager.StartClient(server.Uri);
                return;
            }

            manager.networkAddress = server.Address;
            if (Transport.active is PortTransport portTransport && server.Port > 0)
                portTransport.Port = server.Port;
            manager.StartClient();
        }

        void OnRefreshList()
        {
            discovery = BrawlServerDiscovery.Ensure(manager);
            if (discovery == null) return;
            RememberJoinAddress(CurrentJoinAddress());
            discovery.ClearFound();
            lastListFingerprint = null;
            if (discovery.IsSearching)
                discovery.BroadcastDiscoveryRequest();
            else
                discovery.BeginBrowse();
            RefreshServerList();
        }

        void OnServerOnly()
        {
            if (manager == null) return;
            ApplyRoomName();
            manager.StartServer();
        }

        void ApplyRoomName()
        {
            discovery = BrawlServerDiscovery.Ensure(manager);
            string room = nameField != null ? nameField.text.Trim() : "";
            if (string.IsNullOrEmpty(room))
                room = BrawlServerDiscovery.DefaultServerName();
            if (nameField != null)
                nameField.text = room;
            if (discovery != null)
            {
                discovery.ServerName = room;
                BrawlServerDiscovery.RememberServerName(room);
            }
        }

        void EnsureBrowsing()
        {
            discovery = BrawlServerDiscovery.Ensure(manager);
            if (discovery == null) return;
            if (nameField != null && !nameField.isFocused && string.IsNullOrWhiteSpace(nameField.text))
                nameField.text = BrawlServerDiscovery.DefaultServerName();
            RememberJoinAddress(CurrentJoinAddress());
            discovery.BeginBrowse();
        }

        string CurrentJoinAddress()
        {
            if (addressField != null && !string.IsNullOrWhiteSpace(addressField.text))
                return addressField.text.Trim();
            return manager != null ? manager.networkAddress : "";
        }

        void RememberJoinAddress(string address)
        {
            discovery = discovery != null ? discovery : BrawlServerDiscovery.Ensure(manager);
            if (discovery != null)
                discovery.AddUnicastTarget(address);
        }

        void RefreshServerList()
        {
            if (serverListContent == null || discovery == null) return;
            var servers = discovery.CopyFoundServers();
            if (emptyListText != null)
            {
                emptyListText.gameObject.SetActive(servers.Count == 0);
                emptyListText.text = discovery.BrowseHint;
            }

            string fingerprint = discovery.ListFingerprint();
            if (fingerprint == lastListFingerprint) return;
            lastListFingerprint = fingerprint;

            for (int i = serverListContent.childCount - 1; i >= 0; i--)
                Destroy(serverListContent.GetChild(i).gameObject);

            for (int i = 0; i < servers.Count; i++)
                CreateServerRow(servers[i]);
        }

        void CreateServerRow(BrawlFoundServer server)
        {
            BrawlFoundServer captured = server;
            Button row = CreateButton(serverListContent, "Server_" + server.ServerId, "", JoinColor, 15);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
            row.onClick.AddListener(() => OnJoinFound(captured));

            Text name = row.GetComponentInChildren<Text>();
            if (name != null)
            {
                name.alignment = TextAnchor.MiddleLeft;
                name.rectTransform.offsetMin = new Vector2(10f, 0f);
                name.rectTransform.offsetMax = new Vector2(-88f, 0f);
                name.text = string.IsNullOrEmpty(server.ServerName) ? server.Address : server.ServerName;
            }

            Text meta = CreateText(row.transform, "Meta", 12, FontStyle.Normal, TextAnchor.MiddleRight, HintColor);
            Stretch(meta.rectTransform);
            meta.rectTransform.offsetMin = new Vector2(0f, 0f);
            meta.rectTransform.offsetMax = new Vector2(-10f, 0f);
            int players = Mathf.Max(1, server.PlayerCount);
            meta.text = players + "人";
        }

        void OnStopHost()
        {
            if (manager != null) manager.StopHost();
        }

        void OnStopClient()
        {
            if (manager != null) manager.StopClient();
        }

        void OnStopServer()
        {
            if (manager != null) manager.StopServer();
        }

        void OnCancel()
        {
            if (manager != null) manager.StopClient();
        }

        void OnBotMinus()
        {
            if (BrawlBotLobby.Instance != null)
                BrawlBotLobby.Instance.Adjust(-1);
        }

        void OnBotPlus()
        {
            if (BrawlBotLobby.Instance != null)
                BrawlBotLobby.Instance.Adjust(1);
        }

        static void HideDefaultMirrorHud()
        {
            NetworkManagerHUD hud = FindObjectOfType<NetworkManagerHUD>();
            if (hud != null && hud.enabled)
                hud.enabled = false;
        }

        static string TransportName()
        {
            if (Transport.active == null) return "本地";
            string name = Transport.active.GetType().Name;
            return name.EndsWith("Transport") ? name.Substring(0, name.Length - "Transport".Length) : name;
        }

        void BuildCanvas()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            panel = CreateRect(canvasObject.transform, "Panel", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 18f), new Vector2(PanelWidth, DisconnectedHeight));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;

            Image accent = CreateImage(panel, "Accent", AccentColor, false);
            SetRect(accent.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 4f));

            Text titleText = CreateText(panel, "Title", 22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -12f), new Vector2(-150f, 28f));
            titleText.text = "联机大厅";

            statusDot = CreateImage(panel, "StatusDot", IdleDot, false);
            SetRect(statusDot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-86f, -18f), new Vector2(10f, 10f));

            statusText = CreateText(panel, "Status", 15, FontStyle.Bold, TextAnchor.MiddleRight, HintColor);
            SetRect(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -12f), new Vector2(66f, 24f));
            statusText.text = "未连接";

            hintText = CreateText(panel, "Hint", 14, FontStyle.Normal, TextAnchor.UpperLeft, HintColor);
            SetRect(hintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -42f), new Vector2(-32f, 36f));
            hintText.text = "同网段会自动列出；跨网段请填主机 IP 后刷新或加入";
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.verticalOverflow = VerticalWrapMode.Overflow;

            disconnectedRoot = CreateGroup(panel, "Disconnected");
            connectingRoot = CreateGroup(panel, "Connecting");
            connectedRoot = CreateGroup(panel, "Connected");
            connectingRoot.SetActive(false);
            connectedRoot.SetActive(false);

            Text nameLabel = CreateText(disconnectedRoot.transform, "NameLabel", 15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(nameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -86f), new Vector2(56f, 36f));
            nameLabel.text = "名称";

            nameField = CreateField(disconnectedRoot.transform, "RoomName", BrawlServerDiscovery.DefaultServerName());
            nameField.characterLimit = 16;
            SetRect(nameField.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(36f, -86f), new Vector2(-88f, 36f));

            Button host = CreateButton(disconnectedRoot.transform, "Host", "开房间", HostColor, 18);
            SetRect(host.transform as RectTransform, new Vector2(0f, 1f), new Vector2(0.68f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -128f), new Vector2(-8f, 38f));
            host.onClick.AddListener(OnHost);

            Button server = CreateButton(disconnectedRoot.transform, "Server", "仅服务器", ServerColor, 14);
            SetRect(server.transform as RectTransform, new Vector2(0.68f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -128f), new Vector2(0f, 38f));
            server.onClick.AddListener(OnServerOnly);

            Text listLabel = CreateText(disconnectedRoot.transform, "ListLabel", 15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(listLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -172f), new Vector2(-110f, 24f));
            listLabel.text = "局域网房间";

            Button refresh = CreateButton(disconnectedRoot.transform, "Refresh", "刷新", ServerColor, 14);
            SetRect(refresh.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -170f), new Vector2(64f, 26f));
            refresh.onClick.AddListener(OnRefreshList);

            RectTransform listRoot = CreateRect(disconnectedRoot.transform, "ServerList", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -198f), new Vector2(-32f, 148f));
            Image listBg = listRoot.gameObject.AddComponent<Image>();
            listBg.color = FieldColor;
            listBg.raycastTarget = true;
            listRoot.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = listRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.viewport = listRoot;

            serverListContent = CreateRect(listRoot, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var listLayout = serverListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.childAlignment = TextAnchor.UpperCenter;
            listLayout.childControlHeight = true;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;
            listLayout.spacing = 4f;
            listLayout.padding = new RectOffset(6, 6, 6, 6);
            var fitter = serverListContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = serverListContent;

            emptyListText = CreateText(listRoot, "Empty", 14, FontStyle.Normal, TextAnchor.MiddleCenter, HintColor);
            Stretch(emptyListText.rectTransform);
            emptyListText.text = "正在搜索房间…";

            RectTransform joinRow = CreateRect(disconnectedRoot.transform, "JoinRow", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -354f), new Vector2(-32f, 36f));
            Button join = CreateButton(joinRow, "Join", "手动加入", JoinColor, 14);
            SetRect(join.transform as RectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(88f, 0f));
            join.onClick.AddListener(OnJoin);

            addressField = CreateField(joinRow, "Address", "localhost");
            RectTransform addressRect = addressField.transform as RectTransform;
            addressRect.anchorMin = Vector2.zero;
            addressRect.anchorMax = Vector2.one;
            addressRect.pivot = new Vector2(0.5f, 0.5f);
            addressRect.offsetMin = new Vector2(94f, 0f);
            addressRect.offsetMax = new Vector2(-62f, 0f);

            portField = CreateField(joinRow, "Port", "7777");
            portField.contentType = InputField.ContentType.IntegerNumber;
            portField.characterLimit = 5;
            SetRect(portField.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(56f, 0f));

            Image divider = CreateImage(disconnectedRoot.transform, "Divider", new Color(1f, 1f, 1f, 0.08f), false);
            SetRect(divider.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -398f), new Vector2(-32f, 1f));

            Text botLabel = CreateText(disconnectedRoot.transform, "BotLabel", 15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(botLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -406f), new Vector2(120f, 32f));
            botLabel.text = "开房 Bot";

            Button minus = CreateButton(disconnectedRoot.transform, "BotMinus", "-", ServerColor, 20);
            SetRect(minus.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-118f, -406f), new Vector2(32f, 32f));
            minus.onClick.AddListener(OnBotMinus);

            botValue = CreateText(disconnectedRoot.transform, "BotValue", 18, FontStyle.Bold, TextAnchor.MiddleCenter, AccentColor);
            SetRect(botValue.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-72f, -406f), new Vector2(40f, 32f));
            botValue.text = "1";

            Button plus = CreateButton(disconnectedRoot.transform, "BotPlus", "+", ServerColor, 20);
            SetRect(plus.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -406f), new Vector2(32f, 32f));
            plus.onClick.AddListener(OnBotPlus);

            connectingLabel = CreateText(connectingRoot.transform, "Label", 16, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(connectingLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -86f), new Vector2(-32f, 24f));
            connectingLabel.text = "正在连接";

            Button cancel = CreateButton(connectingRoot.transform, "Cancel", "取消连接", StopColor, 16);
            SetRect(cancel.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -118f), new Vector2(-32f, 36f));
            cancel.onClick.AddListener(OnCancel);

            stopHostButton = CreateButton(connectedRoot.transform, "StopHost", "停止主机", StopColor, 15);
            SetRect(stopHostButton.transform as RectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 14f), new Vector2(-8f, 32f));
            stopHostButton.onClick.AddListener(OnStopHost);

            stopClientButton = CreateButton(connectedRoot.transform, "StopClient", "退出房间", ServerColor, 15);
            SetRect(stopClientButton.transform as RectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 14f), new Vector2(-8f, 32f));
            stopClientButton.onClick.AddListener(() =>
            {
                if (NetworkClient.isConnected)
                    OnStopClient();
                else
                    OnStopServer();
            });
        }

        GameObject CreateGroup(Transform parent, string name)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return rect.gameObject;
        }

        Button CreateButton(Transform parent, string name, string label, Color color, int fontSize)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text text = CreateText(rect, "Label", fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            text.text = label;
            text.raycastTarget = false;
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);
            return button;
        }

        InputField CreateField(Transform parent, string name, string value)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = FieldColor;
            image.raycastTarget = true;

            Text text = CreateText(rect, "Text", 14, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-6f, 0f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var field = rect.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            field.caretWidth = 2;
            field.selectionColor = new Color(0.28f, 0.62f, 0.92f, 0.35f);
            return field;
        }

        Text CreateText(Transform parent, string name, int size, FontStyle style, TextAnchor align, Color color)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        Image CreateImage(Transform parent, string name, Color color, bool raycast)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetRect(rect, anchorMin, anchorMax, pivot, pos, size);
            return rect;
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
