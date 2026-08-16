using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Brawl
{
    /// <summary>
    /// 替换 Mirror 默认 IMGUI 联机条：未连接时显示复古电脑封面与左下角大厅卡片。
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class BrawlNetworkHud : MonoBehaviour
    {
        const float PanelWidth = 438f;
        const float ConnectedWidth = 360f;
        const float DisconnectedHeight = 452f;
        const float ConnectedHeight = 128f;
        const float ConnectingHeight = 164f;

        static readonly Color PanelColor = new Color(0.16f, 0.17f, 0.18f, 0.96f);
        static readonly Color PanelFrameColor = new Color(0.58f, 0.57f, 0.52f, 0.98f);
        static readonly Color TitleBarColor = new Color(0.035f, 0.055f, 0.30f, 1f);
        static readonly Color HostColor = new Color(0.76f, 0.60f, 0.02f, 1f);
        static readonly Color JoinColor = new Color(0.10f, 0.48f, 0.78f, 1f);
        static readonly Color ServerColor = new Color(0.30f, 0.31f, 0.31f, 1f);
        static readonly Color StopColor = new Color(0.82f, 0.24f, 0.22f, 0.96f);
        static readonly Color FieldColor = new Color(0.12f, 0.13f, 0.16f, 0.96f);
        static readonly Color HintColor = new Color(0.93f, 0.94f, 0.95f, 0.95f);
        static readonly Color IdleDot = new Color(0.62f, 0.64f, 0.68f, 1f);
        static readonly Color LiveDot = new Color(0.28f, 0.86f, 0.46f, 1f);
        static readonly Color WaitDot = new Color(1f, 0.78f, 0.22f, 1f);

        RectTransform panel;
        GameObject backgroundRoot;
        GameObject disconnectedRoot;
        GameObject connectingRoot;
        GameObject connectedRoot;
        Text hintText;
        Text statusText;
        Image statusDot;
        InputField nameField;
        InputField addressField;
        InputField portField;
        Text connectingLabel;
        Text emptyListText;
        RectTransform serverListContent;
        Button stopHostButton;
        Button stopClientButton;
        Font font;
        NetworkManager manager;
        BrawlServerDiscovery discovery;
        string lastListFingerprint;
        string transientHint;
        float transientHintUntil;

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
            if (panel == null) return;

            // 联机大厅只属于 Launcher：进入第一关后，规则、等待、对局和结算阶段均不再展示。
            bool showLobby = BrawlLevelCatalog.ActiveSceneIsLauncher();
            panel.gameObject.SetActive(showLobby);
            if (backgroundRoot != null && !showLobby)
                backgroundRoot.SetActive(false);
            if (!showLobby || manager == null)
                return;

            bool server = NetworkServer.active;
            bool client = NetworkClient.isConnected;
            bool connecting = NetworkClient.active && !client && !server;

            disconnectedRoot.SetActive(!server && !client && !connecting);
            connectingRoot.SetActive(connecting);
            connectedRoot.SetActive(server || client);
            if (backgroundRoot != null)
                backgroundRoot.SetActive(!server && !client);

            float height = connecting ? ConnectingHeight : server || client ? ConnectedHeight : DisconnectedHeight;
            float width = server || client ? ConnectedWidth : PanelWidth;
            panel.sizeDelta = new Vector2(width, height);

            if (addressField != null && addressField.isFocused)
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
                BindHeader("未连接", IdleDot, "同一局域网会自动列出房间，点一项即可加入");
                EnsureBrowsing();
                RefreshServerList();
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
            if (status == "未连接" && Time.unscaledTime < transientHintUntil && !string.IsNullOrEmpty(transientHint))
                hint = transientHint;
            else if (Time.unscaledTime >= transientHintUntil)
                transientHint = null;

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
            // Bot 已移动到准备区，创建房间时不再自动生成。
            if (manager is BrawlNetworkManager brawlManager)
                brawlManager.PendingBotCount = BrawlBotLobby.Instance != null ? BrawlBotLobby.Instance.BotCount : 0;
            manager.StartHost();
        }

        void OnJoin()
        {
            if (manager == null || addressField == null) return;
            string address = addressField.text.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                transientHint = "请输入房主的IP地址";
                transientHintUntil = Time.unscaledTime + 2.5f;
                BindHeader("未连接", WaitDot, transientHint);
                addressField.ActivateInputField();
                return;
            }

            transientHint = null;
            manager.networkAddress = address;
            if (discovery != null)
                discovery.StopDiscovery();
            manager.StartClient();
        }

        void OnJoinFound(BrawlFoundServer server)
        {
            if (manager == null || server == null) return;
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
            discovery.BeginBrowse();
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
            Button row = CreateButton(serverListContent, "Server_" + server.ServerId, "", JoinColor, 16);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.preferredHeight = 38f;
            row.onClick.AddListener(() => OnJoinFound(captured));

            Text name = row.GetComponentInChildren<Text>();
            if (name != null)
            {
                name.alignment = TextAnchor.MiddleLeft;
                name.rectTransform.offsetMin = new Vector2(10f, 0f);
                name.rectTransform.offsetMax = new Vector2(-88f, 0f);
                name.text = string.IsNullOrEmpty(server.ServerName) ? server.Address : server.ServerName;
            }

            Text meta = CreateText(row.transform, "Meta", 14, FontStyle.Normal, TextAnchor.MiddleRight, HintColor);
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

            backgroundRoot = CreateGroup(canvasObject.transform, "LobbyBackdrop");
            Image blackBackdrop = backgroundRoot.AddComponent<Image>();
            blackBackdrop.color = Color.black;
            blackBackdrop.raycastTarget = false;

            RectTransform artworkRect = CreateRect(backgroundRoot.transform, "Artwork", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));
            RawImage artwork = artworkRect.gameObject.AddComponent<RawImage>();
            artwork.texture = Resources.Load<Texture2D>("UI/NetworkLobby/MainMenuBackground");
            artwork.color = Color.white;
            artwork.raycastTarget = false;
            AspectRatioFitter artworkFitter = artworkRect.gameObject.AddComponent<AspectRatioFitter>();
            artworkFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            artworkFitter.aspectRatio = artwork.texture != null
                ? artwork.texture.width / (float)artwork.texture.height
                : 16f / 9f;

            Image artworkShade = CreateImage(backgroundRoot.transform, "ReadabilityShade", new Color(0f, 0f, 0f, 0.035f), false);
            Stretch(artworkShade.rectTransform);

            panel = CreateRect(canvasObject.transform, "Panel", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 22f), new Vector2(PanelWidth, DisconnectedHeight));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelFrameColor;
            panelImage.raycastTarget = true;

            Image panelSurface = CreateImage(panel, "Surface", PanelColor, false);
            Stretch(panelSurface.rectTransform);
            panelSurface.rectTransform.offsetMin = new Vector2(5f, 5f);
            panelSurface.rectTransform.offsetMax = new Vector2(-5f, -5f);
            AddBevel(panel, true);

            Image titleBar = CreateImage(panel, "TitleBar", TitleBarColor, false);
            SetRect(titleBar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -7f), new Vector2(-14f, 36f));
            AddBevel(titleBar.rectTransform, false);

            Text titleText = CreateText(panel, "Title", 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -10f), new Vector2(-170f, 28f));
            titleText.text = "联机大厅";

            statusDot = CreateImage(panel, "StatusDot", IdleDot, false);
            SetRect(statusDot.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-100f, -20f), new Vector2(10f, 10f));

            statusText = CreateText(panel, "Status", 17, FontStyle.Bold, TextAnchor.MiddleRight, HintColor);
            SetRect(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -12f), new Vector2(76f, 24f));
            statusText.text = "未连接";

            hintText = CreateText(panel, "Hint", 16, FontStyle.Normal, TextAnchor.UpperLeft, HintColor);
            SetRect(hintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -49f), new Vector2(-36f, 34f));
            hintText.text = "同一局域网会自动列出房间，点一项即可加入";
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText.verticalOverflow = VerticalWrapMode.Overflow;

            disconnectedRoot = CreateGroup(panel, "Disconnected");
            connectingRoot = CreateGroup(panel, "Connecting");
            connectedRoot = CreateGroup(panel, "Connected");
            connectingRoot.SetActive(false);
            connectedRoot.SetActive(false);

            Text nameLabel = CreateText(disconnectedRoot.transform, "NameLabel", 17, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(nameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -90f), new Vector2(58f, 36f));
            nameLabel.text = "名称";

            nameField = CreateField(disconnectedRoot.transform, "RoomName", BrawlServerDiscovery.DefaultServerName());
            nameField.characterLimit = 16;
            SetRect(nameField.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(38f, -90f), new Vector2(-94f, 36f));

            Button host = CreateButton(disconnectedRoot.transform, "Host", "创建房间", HostColor, 20);
            SetRect(host.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -132f), new Vector2(-36f, 40f));
            host.onClick.AddListener(OnHost);

            Text listLabel = CreateText(disconnectedRoot.transform, "ListLabel", 17, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(listLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -182f), new Vector2(-124f, 26f));
            listLabel.text = "局域网房间";

            Button refresh = CreateButton(disconnectedRoot.transform, "Refresh", "刷新", ServerColor, 16);
            SetRect(refresh.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -178f), new Vector2(70f, 28f));
            refresh.onClick.AddListener(OnRefreshList);

            RectTransform listRoot = CreateRect(disconnectedRoot.transform, "ServerList", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -208f), new Vector2(-36f, 150f));
            Image listBg = listRoot.gameObject.AddComponent<Image>();
            listBg.color = FieldColor;
            listBg.raycastTarget = true;
            AddBevel(listRoot, false);
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

            emptyListText = CreateText(listRoot, "Empty", 16, FontStyle.Normal, TextAnchor.MiddleCenter, HintColor);
            Stretch(emptyListText.rectTransform);
            emptyListText.text = "正在搜索局域网房间…";

            RectTransform joinRow = CreateRect(disconnectedRoot.transform, "JoinRow", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -370f), new Vector2(-36f, 40f));
            Button join = CreateButton(joinRow, "Join", "手动输入", HostColor, 17);
            SetRect(join.transform as RectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(100f, 0f));
            join.onClick.AddListener(OnJoin);

            addressField = CreateField(joinRow, "Address", "", "房主的IP地址");
            RectTransform addressRect = addressField.transform as RectTransform;
            addressRect.anchorMin = Vector2.zero;
            addressRect.anchorMax = Vector2.one;
            addressRect.pivot = new Vector2(0.5f, 0.5f);
            addressRect.offsetMin = new Vector2(106f, 0f);
            addressRect.offsetMax = new Vector2(-70f, 0f);

            portField = CreateField(joinRow, "Port", "7777");
            portField.contentType = InputField.ContentType.IntegerNumber;
            portField.characterLimit = 5;
            SetRect(portField.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(64f, 0f));

            Text footer = CreateText(disconnectedRoot.transform, "Footer", 15, FontStyle.Bold, TextAnchor.MiddleLeft, HintColor);
            SetRect(footer.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 12f), new Vector2(-36f, 24f));
            footer.text = "OFFICE LAN  ·  99？66？996！！！";

            connectingLabel = CreateText(connectingRoot.transform, "Label", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(connectingLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -86f), new Vector2(-32f, 24f));
            connectingLabel.text = "正在连接";

            Button cancel = CreateButton(connectingRoot.transform, "Cancel", "取消连接", StopColor, 17);
            SetRect(cancel.transform as RectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -118f), new Vector2(-32f, 36f));
            cancel.onClick.AddListener(OnCancel);

            stopHostButton = CreateButton(connectedRoot.transform, "StopHost", "停止主机", StopColor, 16);
            SetRect(stopHostButton.transform as RectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 14f), new Vector2(-8f, 32f));
            stopHostButton.onClick.AddListener(OnStopHost);

            stopClientButton = CreateButton(connectedRoot.transform, "StopClient", "退出房间", ServerColor, 16);
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
            AddBevel(rect, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Color labelColor = color == HostColor ? new Color(0.10f, 0.09f, 0.04f, 1f) : Color.white;
            bool darkLabel = color == HostColor;
            Text text = CreateText(rect, "Label", fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, labelColor, false);
            Stretch(text.rectTransform);
            text.text = label;
            text.raycastTarget = false;
            if (!darkLabel)
            {
                Outline outline = text.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.72f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            return button;
        }

        InputField CreateField(Transform parent, string name, string value, string placeholder = null)
        {
            RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = FieldColor;
            image.raycastTarget = true;
            AddBevel(rect, false);

            Text placeholderText = null;
            if (!string.IsNullOrEmpty(placeholder))
            {
                placeholderText = CreateText(rect, "Placeholder", 16, FontStyle.Normal, TextAnchor.MiddleLeft, HintColor);
                Stretch(placeholderText.rectTransform);
                placeholderText.rectTransform.offsetMin = new Vector2(9f, 0f);
                placeholderText.rectTransform.offsetMax = new Vector2(-7f, 0f);
                placeholderText.text = placeholder;
                placeholderText.fontStyle = FontStyle.Italic;
            }

            Text text = CreateText(rect, "Text", 16, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-6f, 0f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var field = rect.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.text = value;
            field.caretWidth = 2;
            field.selectionColor = new Color(0.28f, 0.62f, 0.92f, 0.35f);
            return field;
        }

        void AddBevel(RectTransform target, bool raised)
        {
            Color light = new Color(0.86f, 0.84f, 0.76f, 0.92f);
            Color dark = new Color(0.035f, 0.04f, 0.045f, 0.92f);
            Color topLeft = raised ? light : dark;
            Color bottomRight = raised ? dark : light;

            Image top = CreateImage(target, "BevelTop", topLeft, false);
            SetRect(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 2f));
            Image left = CreateImage(target, "BevelLeft", topLeft, false);
            SetRect(left.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(2f, 0f));
            Image bottom = CreateImage(target, "BevelBottom", bottomRight, false);
            SetRect(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 2f));
            Image right = CreateImage(target, "BevelRight", bottomRight, false);
            SetRect(right.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(2f, 0f));
        }

        Text CreateText(Transform parent, string name, int size, FontStyle style, TextAnchor align, Color color, bool addShadow = true)
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
            if (addShadow && color.r + color.g + color.b > 1.5f)
            {
                Shadow shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.78f);
                shadow.effectDistance = new Vector2(1f, -1f);
            }
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
