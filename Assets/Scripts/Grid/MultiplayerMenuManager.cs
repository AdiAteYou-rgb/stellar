using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

[ExecuteAlways]
public class MultiplayerMenuManager : MonoBehaviour
{
    public enum MenuState
    {
        MainMenu,
        HostOrJoinSelect,
        HostingWaitingForPlayer,
        JoiningEnterCode,
        TransportingToGameWall,
        RoleSelection,
        Gameplay
    }

    public enum PlayerRole
    {
        None,
        Plant,
        Zombie
    }

    [Header("Scene References")]
    public Camera mainCamera;
    public Transform menuWallTransform;
    public GridManager gameplayWallGrid;

    [Header("Wall Dimensions (16:9)")]
    public float wallWidth = 16.0f;
    public float wallHeight = 9.0f;
    public float wallThickness = 0.2f;
    public float cameraDistance = 10.0f;
    public float cameraTransportSpeed = 35.0f;

    [Header("Session State")]
    public MenuState currentState = MenuState.MainMenu;
    public bool isHost = false;
    public string hostJoinCode = "";
    public string enteredJoinCode = "";
    public string statusMessage = "";

    [Header("Assigned Roles")]
    public PlayerRole hostRole = PlayerRole.None;
    public PlayerRole joinerRole = PlayerRole.None;
    public PlayerRole localRole = PlayerRole.None;

    // Networking internals: Unity Relay (Internet / Cross-Network) + TCP P2P fallback (Offline / LAN)
    private NetworkDriver relayDriver;
    private NetworkPipeline relayReliablePipeline;
    private NetworkConnection relayConnection;
    private bool isUsingRelay = false;
    private bool isRelayHostingActive = false;
    private bool isAllocatingRelay = false;
    private string pendingRelayJoinCode = "";
    private int networkSessionId = 0;
    private static string s_SessionProfileName;
    private readonly ConcurrentQueue<string> outgoingRelayMessages = new ConcurrentQueue<string>();

    private TcpListener tcpServer;
    private TcpClient connectedClient;
    private StreamReader netReader;
    private StreamWriter netWriter;
    private Thread networkThread;
    private volatile bool networkRunning;
    private readonly ConcurrentQueue<string> incomingMessages = new ConcurrentQueue<string>();
    private int hostingPort = 7777;
    private const string Base36Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private Vector3 targetCameraPos;

    [Header("Menu Polish & UI")]
    public bool showHowToPlayModal = false;
    [SerializeField] private Font menuFont;
    private static Font s_CachedSophieFont;
    private static bool s_SearchedSophieFont = false;
    private float codeCopiedTimer = 0f;
    private bool gridEventsSubscribed = false;

    private void Awake()
    {
        if (menuWallTransform == null)
        {
            menuWallTransform = transform;
        }
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        if (gameplayWallGrid == null)
        {
            gameplayWallGrid = FindFirstObjectByType<GridManager>();
        }

        SyncMenuWallScale();
        EnsureMenuBackdrop();
        Application.runInBackground = true;

        if (Application.isPlaying)
        {
            currentState = MenuState.MainMenu;
            EnsureGridEventsSubscribed();
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.lockCameraToThisWall = false;
                gameplayWallGrid.gameplayActive = false;
            }
            SnapCameraToMenuWall();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            currentState = MenuState.MainMenu;
            EnsureGridEventsSubscribed();
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.lockCameraToThisWall = false;
                gameplayWallGrid.gameplayActive = false;
            }
            SnapCameraToMenuWall();
        }
    }

    private void EnsureGridEventsSubscribed()
    {
        if (gameplayWallGrid == null)
        {
            gameplayWallGrid = FindFirstObjectByType<GridManager>();
        }
        if (gameplayWallGrid == null || gridEventsSubscribed)
        {
            return;
        }

        gameplayWallGrid.OnPlantPlacedLocal -= HandleLocalPlantPlaced;
        gameplayWallGrid.OnZombiePlacedLocal -= HandleLocalZombiePlaced;
        gameplayWallGrid.OnMatchEndedLocal -= HandleLocalMatchEnded;
        gameplayWallGrid.OnMatchRestartedLocal -= HandleLocalMatchRestarted;
        gameplayWallGrid.OnSkyMoonSpawnedLocal -= HandleSkyMoonSpawned;
        gameplayWallGrid.OnFlowerMoonSpawnedLocal -= HandleFlowerMoonSpawned;
        gameplayWallGrid.OnMoonsCollectedLocal -= HandleMoonsCollected;
        gameplayWallGrid.OnClashStartedLocal -= HandleClashStarted;
        gameplayWallGrid.OnArenaPoseLocal -= HandleArenaPose;
        gameplayWallGrid.OnArenaResolvedLocal -= HandleArenaResolved;
        gameplayWallGrid.OnArenaStunLocal -= HandleArenaStun;
        gameplayWallGrid.OnArenaAbilityFxLocal -= HandleArenaAbilityFx;
        gameplayWallGrid.OnArenaSeedLocal -= HandleArenaSeed;

        gameplayWallGrid.OnPlantPlacedLocal += HandleLocalPlantPlaced;
        gameplayWallGrid.OnZombiePlacedLocal += HandleLocalZombiePlaced;
        gameplayWallGrid.OnMatchEndedLocal += HandleLocalMatchEnded;
        gameplayWallGrid.OnMatchRestartedLocal += HandleLocalMatchRestarted;
        gameplayWallGrid.OnSkyMoonSpawnedLocal += HandleSkyMoonSpawned;
        gameplayWallGrid.OnFlowerMoonSpawnedLocal += HandleFlowerMoonSpawned;
        gameplayWallGrid.OnMoonsCollectedLocal += HandleMoonsCollected;
        gameplayWallGrid.OnClashStartedLocal += HandleClashStarted;
        gameplayWallGrid.OnArenaPoseLocal += HandleArenaPose;
        gameplayWallGrid.OnArenaResolvedLocal += HandleArenaResolved;
        gameplayWallGrid.OnArenaStunLocal += HandleArenaStun;
        gameplayWallGrid.OnArenaAbilityFxLocal += HandleArenaAbilityFx;
        gameplayWallGrid.OnArenaSeedLocal += HandleArenaSeed;
        gridEventsSubscribed = true;
    }

    private void OnDestroy()
    {
        if (gameplayWallGrid != null)
        {
            gameplayWallGrid.OnPlantPlacedLocal -= HandleLocalPlantPlaced;
            gameplayWallGrid.OnZombiePlacedLocal -= HandleLocalZombiePlaced;
            gameplayWallGrid.OnMatchEndedLocal -= HandleLocalMatchEnded;
            gameplayWallGrid.OnMatchRestartedLocal -= HandleLocalMatchRestarted;
            gameplayWallGrid.OnSkyMoonSpawnedLocal -= HandleSkyMoonSpawned;
            gameplayWallGrid.OnFlowerMoonSpawnedLocal -= HandleFlowerMoonSpawned;
            gameplayWallGrid.OnMoonsCollectedLocal -= HandleMoonsCollected;
            gameplayWallGrid.OnClashStartedLocal -= HandleClashStarted;
            gameplayWallGrid.OnArenaPoseLocal -= HandleArenaPose;
            gameplayWallGrid.OnArenaResolvedLocal -= HandleArenaResolved;
            gameplayWallGrid.OnArenaStunLocal -= HandleArenaStun;
            gameplayWallGrid.OnArenaAbilityFxLocal -= HandleArenaAbilityFx;
            gameplayWallGrid.OnArenaSeedLocal -= HandleArenaSeed;
        }
        gridEventsSubscribed = false;
        StopNetwork();
    }

    public void SyncMenuWallScale()
    {
        if (menuWallTransform != null)
        {
            menuWallTransform.rotation = Quaternion.identity;
            menuWallTransform.localScale = new Vector3(wallWidth, wallHeight, wallThickness);
        }
    }

    [ContextMenu("Snap Camera to Menu Wall")]
    public void SnapCameraToMenuWall()
    {
        SyncMenuWallScale();
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null && menuWallTransform != null)
        {
            mainCamera.orthographic = true;
            mainCamera.orthographicSize = wallHeight * 0.5f;
            targetCameraPos = menuWallTransform.position - menuWallTransform.forward * cameraDistance;
            mainCamera.transform.position = targetCameraPos;
            mainCamera.transform.rotation = Quaternion.LookRotation(menuWallTransform.forward, Vector3.up);

            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.Apply16By9Viewport(mainCamera);
            }
        }
    }

    private void Update()
    {
        SyncMenuWallScale();

        if (!Application.isPlaying)
        {
            return;
        }

        if (codeCopiedTimer > 0f)
        {
            codeCopiedTimer -= Time.deltaTime;
        }

        // Pump Unity Relay Transport driver if active
        UpdateRelayDriver();

        // Process incoming network messages on the Unity main thread
        while (incomingMessages.TryDequeue(out string msg))
        {
            ProcessNetworkMessage(msg);
        }

        // Host decides the 3D 1v1 result: N = Zombie won, M = Plant won
        if (isHost && gameplayWallGrid != null && gameplayWallGrid.IsIn3DArena)
        {
            if (Input.GetKeyDown(KeyCode.N))
            {
                gameplayWallGrid.ResolveArena(zombieWon: true);
            }
            else if (Input.GetKeyDown(KeyCode.M))
            {
                gameplayWallGrid.ResolveArena(zombieWon: false);
            }
        }

        // Smooth camera transport from MenuWall to Gameplay Wall
        if (mainCamera != null)
        {
            if (currentState == MenuState.TransportingToGameWall)
            {
                mainCamera.transform.position = Vector3.MoveTowards(
                    mainCamera.transform.position,
                    targetCameraPos,
                    cameraTransportSpeed * Time.deltaTime
                );

                if (Vector3.Distance(mainCamera.transform.position, targetCameraPos) < 0.05f)
                {
                    mainCamera.transform.position = targetCameraPos;
                    currentState = (localRole == PlayerRole.None) ? MenuState.RoleSelection : MenuState.Gameplay;
                }
            }
            else if (currentState == MenuState.MainMenu ||
                     currentState == MenuState.HostOrJoinSelect ||
                     currentState == MenuState.HostingWaitingForPlayer ||
                     currentState == MenuState.JoiningEnterCode)
            {
                Vector3 menuCamPos = menuWallTransform.position - menuWallTransform.forward * cameraDistance;
                mainCamera.transform.position = menuCamPos;
            }
        }
    }

    // ==================== HOST & JOIN NETWORKING (UNITY RELAY + TCP FALLBACK) ====================

    private static async Task EnsureUnityServicesInitializedAsync()
    {
        if (string.IsNullOrEmpty(s_SessionProfileName))
        {
            s_SessionProfileName = "P_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            var options = new InitializationOptions();
            await UnityServices.InitializeAsync(options);
        }
        else
        {
            while (UnityServices.State == ServicesInitializationState.Initializing)
            {
                await Task.Delay(50);
            }
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            try
            {
                AuthenticationService.Instance.SwitchProfile(s_SessionProfileName);
            }
            catch
            {
                // Ignore if profile already set
            }
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private void CreateAndConfigureRelayDriver(ref RelayServerData relayServerData)
    {
        DisposeRelayDriver();

        var settings = new NetworkSettings();
        settings.WithNetworkConfigParameters(
            maxConnectAttempts: 60,
            connectTimeoutMS: 1000,
            disconnectTimeoutMS: 30000,
            heartbeatTimeoutMS: 500);
        settings.WithRelayParameters(ref relayServerData);
        settings.WithReliableStageParameters(windowSize: 64, maximumResendTime: 750);

#if UNITY_WEBGL && !UNITY_EDITOR
        relayDriver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings);
#else
        relayDriver = NetworkDriver.Create(new UDPNetworkInterface(), settings);
#endif
        relayReliablePipeline = relayDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
    }

    private void UpdateRelayDriver()
    {
        if (!relayDriver.IsCreated)
        {
            return;
        }

        // 1. Complete driver update tick first so connections, acks, and pipeline buffers are up to date
        relayDriver.ScheduleUpdate().Complete();

        // 2. Accept incoming connections (Host) before popping events
        if (isHost && relayDriver.Listening)
        {
            NetworkConnection incoming;
            while ((incoming = relayDriver.Accept()) != default)
            {
                relayConnection = incoming;
                isUsingRelay = true;
            }
        }

        // 3. Pop all connection & data events
        NetworkEvent.Type cmd;
        while ((cmd = relayDriver.PopEvent(out NetworkConnection conn, out DataStreamReader stream)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                relayConnection = conn;
                isUsingRelay = true;
                if (!isHost)
                {
                    outgoingRelayMessages.Enqueue("JOIN:" + pendingRelayJoinCode);
                }
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                relayConnection = conn;
                isUsingRelay = true;
                FixedString512Bytes raw = stream.ReadFixedString512();
                string line = raw.ToString();

                if (isHost && line.StartsWith("JOIN:"))
                {
                    outgoingRelayMessages.Enqueue("ACCEPTED");
                    incomingMessages.Enqueue("PLAYER_JOINED");
                }
                else if (!isHost && line == "ACCEPTED")
                {
                    incomingMessages.Enqueue("CONNECTED_TO_HOST");
                }
                else
                {
                    incomingMessages.Enqueue(line);
                }
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                if (conn == relayConnection)
                {
                    relayConnection = default;
                }
                if (!isHost && currentState == MenuState.JoiningEnterCode)
                {
                    incomingMessages.Enqueue("ERROR:Relay connection closed or timed out. Check the code and try again!");
                }
            }
        }

        // 4. Send queued outgoing messages after events are processed (if reliability window is momentarily full, keep in queue)
        if (relayConnection != default &&
            relayDriver.GetConnectionState(relayConnection) == NetworkConnection.State.Connected)
        {
            int maxPerFrame = 64;
            while (maxPerFrame-- > 0 && outgoingRelayMessages.TryPeek(out string outMsg))
            {
                if (string.IsNullOrEmpty(outMsg))
                {
                    outgoingRelayMessages.TryDequeue(out _);
                    continue;
                }

                NetworkPipeline pipe = outMsg.StartsWith("APOSE:") ? NetworkPipeline.Null : relayReliablePipeline;
                int res = relayDriver.BeginSend(pipe, relayConnection, out DataStreamWriter writer);
                if (res == 0)
                {
                    outgoingRelayMessages.TryDequeue(out _);
                    FixedString512Bytes fs = new FixedString512Bytes(outMsg);
                    writer.WriteFixedString512(fs);
                    relayDriver.EndSend(writer);
                }
                else if (pipe == NetworkPipeline.Null)
                {
                    // Drop stale unreliable pose if buffer is busy
                    outgoingRelayMessages.TryDequeue(out _);
                }
                else
                {
                    // Reliable pipeline window full; retry next frame without losing the message!
                    break;
                }
            }
        }
    }

    public void StartHosting()
    {
        StopNetwork();
        int sessionId = ++networkSessionId;
        isHost = true;
        hostRole = PlayerRole.None;
        joinerRole = PlayerRole.None;
        localRole = PlayerRole.None;
        currentState = MenuState.HostingWaitingForPlayer;

        // Start local TCP listener in the background as an automatic offline/local fallback
        try
        {
            int randomStart = UnityEngine.Random.Range(7200, 8800);
            hostingPort = randomStart;
            for (int i = 0; i < 200; i++)
            {
                int p = 7100 + ((randomStart - 7100 + i) % 1800);
                try
                {
                    tcpServer = new TcpListener(IPAddress.Any, p);
                    tcpServer.Start();
                    hostingPort = p;
                    break;
                }
                catch
                {
                    tcpServer = null;
                }
            }

            if (tcpServer != null)
            {
                networkRunning = true;
                networkThread = new Thread(HostAcceptLoop) { IsBackground = true };
                networkThread.Start();
            }
        }
        catch
        {
            // Ignore local TCP errors if Relay succeeds
        }

        // Allocate online room on Unity Relay so friends on ANY network can join with the 6-char code
        _ = StartRelayHostAsync(sessionId);
    }

    private async Task StartRelayHostAsync(int sessionId)
    {
        isAllocatingRelay = true;
        isRelayHostingActive = false;
        hostJoinCode = "......";
        statusMessage = "Creating Online Relay Lobby (works over any network)...";

        try
        {
            await EnsureUnityServicesInitializedAsync();
            if (sessionId != networkSessionId) return;

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
            if (sessionId != networkSessionId) return;

            string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (sessionId != networkSessionId) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            RelayServerData relayServerData = allocation.ToRelayServerData("wss");
#else
            RelayServerData relayServerData = allocation.ToRelayServerData("dtls");
#endif
            CreateAndConfigureRelayDriver(ref relayServerData);

            if (relayDriver.Bind(NetworkEndpoint.AnyIpv4) != 0 || relayDriver.Listen() != 0)
            {
                throw new Exception("Failed to bind or listen on Relay driver.");
            }

            isRelayHostingActive = true;
            isAllocatingRelay = false;
            hostJoinCode = relayCode.ToUpperInvariant();
            statusMessage = "Online Relay Active! Share this code with Player 2 on ANY network or PC.";
        }
        catch (Exception ex)
        {
            if (sessionId != networkSessionId) return;
            isAllocatingRelay = false;
            isRelayHostingActive = false;
            hostJoinCode = GenerateRandomJoinCode(hostingPort);
            statusMessage = $"Offline/LAN Fallback Active ({ex.GetType().Name}). Share code for local/LAN play.";
            Debug.LogWarning($"Unity Relay host allocation failed, using local TCP fallback: {ex.Message}");
        }
    }

    private void HostAcceptLoop()
    {
        try
        {
            while (networkRunning && tcpServer != null)
            {
                TcpClient client = tcpServer.AcceptTcpClient();
                connectedClient = client;
                NetworkStream stream = client.GetStream();
                netReader = new StreamReader(stream);
                netWriter = new StreamWriter(stream) { AutoFlush = true };

                // Wait for joiner handshake with code verification
                string line = netReader.ReadLine();
                if (line != null && line.StartsWith("JOIN:") &&
                    (line.Substring(5).Trim().Equals(hostJoinCode, StringComparison.OrdinalIgnoreCase) ||
                     (TryDecodeCodeToPort(line.Substring(5), out int p, out _) && p == hostingPort)))
                {
                    isUsingRelay = false;
                    netWriter.WriteLine("ACCEPTED");
                    incomingMessages.Enqueue("PLAYER_JOINED");
                    ReadLoop();
                    break;
                }
                else
                {
                    netWriter.WriteLine("INVALID_CODE");
                    client.Close();
                }
            }
        }
        catch
        {
            // Listener stopped
        }
    }

    public void ConnectAsJoiner()
    {
        string cleanCode = (enteredJoinCode ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (string.IsNullOrEmpty(cleanCode))
        {
            statusMessage = "Please enter the 6-character code from the Host!";
            return;
        }

        StopNetwork();
        int sessionId = ++networkSessionId;
        isHost = false;
        hostRole = PlayerRole.None;
        joinerRole = PlayerRole.None;
        localRole = PlayerRole.None;

        // If the user explicitly typed CODE@IP, connect directly via TCP
        if (cleanCode.Contains("@"))
        {
            if (!TryDecodeCodeToPort(cleanCode, out int port, out string hostIp))
            {
                statusMessage = "Invalid CODE@IP format!";
                return;
            }
            ConnectViaTcp(cleanCode, port, hostIp);
            return;
        }

        // Connect over the internet via Unity Relay (with automatic fallback to local TCP if offline)
        _ = ConnectViaRelayOrFallbackAsync(cleanCode, sessionId);
    }

    private async Task ConnectViaRelayOrFallbackAsync(string cleanCode, int sessionId)
    {
        statusMessage = $"Connecting to Host via Online Relay ({cleanCode})...";

        try
        {
            await EnsureUnityServicesInitializedAsync();
            if (sessionId != networkSessionId) return;

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(cleanCode);
            if (sessionId != networkSessionId) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            RelayServerData relayServerData = joinAllocation.ToRelayServerData("wss");
#else
            RelayServerData relayServerData = joinAllocation.ToRelayServerData("dtls");
#endif
            CreateAndConfigureRelayDriver(ref relayServerData);

            if (relayDriver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                throw new Exception("Failed to bind local Relay client socket.");
            }

            pendingRelayJoinCode = cleanCode;
            isUsingRelay = true;
            relayConnection = relayDriver.Connect(relayServerData.Endpoint);
            statusMessage = $"Handshaking with Host ({cleanCode})...";
        }
        catch (Exception relayEx)
        {
            if (sessionId != networkSessionId) return;

            // If Relay lookup failed, check if this is a local offline port-encoded code on the same PC
            if (TryDecodeCodeToPort(cleanCode, out int localPort, out string hostIp))
            {
                Debug.Log($"Relay join failed ({relayEx.Message}); trying local TCP fallback on {hostIp}:{localPort}...");
                ConnectViaTcp(cleanCode, localPort, hostIp);
            }
            else
            {
                statusMessage = "Invalid or expired Join Code! Ask the Host for their 6-character code.";
            }
        }
    }

    private void ConnectViaTcp(string cleanCode, int port, string hostIp)
    {
        statusMessage = $"Connecting to Host ({cleanCode})...";
        networkRunning = true;
        networkThread = new Thread(() =>
        {
            try
            {
                connectedClient = new TcpClient();
                IAsyncResult result = connectedClient.BeginConnect(hostIp, port, null, null);
                bool connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3.0));

                if (!connected || !connectedClient.Connected)
                {
                    incomingMessages.Enqueue("ERROR:Could not find Host for this code. Make sure the Host is waiting in the lobby!");
                    return;
                }

                connectedClient.EndConnect(result);
                NetworkStream stream = connectedClient.GetStream();
                netReader = new StreamReader(stream);
                netWriter = new StreamWriter(stream) { AutoFlush = true };

                string rawCode = cleanCode.Contains("@") ? cleanCode.Split('@')[0] : cleanCode;
                netWriter.WriteLine("JOIN:" + rawCode);

                string response = netReader.ReadLine();
                if (response == "ACCEPTED")
                {
                    isUsingRelay = false;
                    incomingMessages.Enqueue("CONNECTED_TO_HOST");
                    ReadLoop();
                }
                else
                {
                    incomingMessages.Enqueue("ERROR:Host rejected code (Code mismatch).");
                }
            }
            catch (Exception ex)
            {
                incomingMessages.Enqueue("ERROR:Connection failed (" + ex.Message + ")");
            }
        })
        { IsBackground = true };
        networkThread.Start();
    }

    private void ReadLoop()
    {
        try
        {
            while (networkRunning && netReader != null)
            {
                string line = netReader.ReadLine();
                if (line == null) break;
                incomingMessages.Enqueue(line);
            }
        }
        catch
        {
            // Disconnected
        }
    }

    private void SendNetworkMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;

        if (isUsingRelay && relayDriver.IsCreated && relayConnection != default)
        {
            outgoingRelayMessages.Enqueue(msg);
            return;
        }

        try
        {
            if (netWriter != null)
            {
                netWriter.WriteLine(msg);
            }
        }
        catch
        {
            // Ignore write errors if testing solo
        }
    }

    private void ProcessNetworkMessage(string msg)
    {
        if (msg == "PLAYER_JOINED" || msg == "CONNECTED_TO_HOST")
        {
            BeginTransportToGameplayWall();
        }
        else if (msg.StartsWith("ERROR:"))
        {
            statusMessage = msg.Substring("ERROR:".Length);
        }
        else if (msg.StartsWith("ROLES:"))
        {
            // Format: ROLES:Plant,Zombie (hostRole,joinerRole)
            string[] parts = msg.Substring("ROLES:".Length).Split(',');
            if (parts.Length == 2 &&
                Enum.TryParse(parts[0], out PlayerRole hRole) &&
                Enum.TryParse(parts[1], out PlayerRole jRole))
            {
                ApplyRoleAssignment(hRole, jRole);
            }
        }
        else if (msg.StartsWith("PLACE:"))
        {
            string[] coords = msg.Substring("PLACE:".Length).Split(',');
            if (coords.Length >= 2 &&
                int.TryParse(coords[0], out int r) &&
                int.TryParse(coords[1], out int c))
            {
                PlantType pType = PlantType.Moonflower;
                if (coords.Length >= 3)
                {
                    Enum.TryParse(coords[2], out pType);
                }

                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.PlacePlantOnTile(r, c, pType, notifyNetwork: false);
                }
            }
        }
        else if (msg.StartsWith("ZOMBIE:"))
        {
            string[] coords = msg.Substring("ZOMBIE:".Length).Split(',');
            if (coords.Length >= 2 &&
                int.TryParse(coords[0], out int r) &&
                int.TryParse(coords[1], out int c))
            {
                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.SpawnZombieOnTile(r, c, notifyNetwork: false);
                }
            }
        }
        else if (msg.StartsWith("WIN:"))
        {
            string winner = msg.Substring("WIN:".Length);
            if (winner == "Plants" && gameplayWallGrid != null)
            {
                gameplayWallGrid.TriggerPlantsWin(notifyNetwork: false);
            }
            else if (winner == "Zombies" && gameplayWallGrid != null)
            {
                gameplayWallGrid.TriggerZombiesWin(notifyNetwork: false);
            }
        }
        else if (msg == "RESTART")
        {
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.ResetMatch(notifyNetwork: false);
            }
        }
        else if (gameplayWallGrid != null)
        {
            ProcessSyncMessage(msg);
        }
    }

    // ==================== MOON / CLASH / ARENA SYNC ====================

    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    private static string F(float v) => v.ToString("0.###", Inv);
    private static float PF(string s) => float.Parse(s, Inv);

    private void ProcessSyncMessage(string msg)
    {
        try
        {
            if (msg.StartsWith("MOONSKY:"))
            {
                // id,x,y,z,landY,value
                string[] p = msg.Substring(8).Split(',');
                gameplayWallGrid.SpawnSkyMoonWithId(int.Parse(p[0]),
                    new Vector3(PF(p[1]), PF(p[2]), PF(p[3])), PF(p[4]), int.Parse(p[5]));
            }
            else if (msg.StartsWith("MOONFLW:"))
            {
                // id,x,y,z,value
                string[] p = msg.Substring(8).Split(',');
                gameplayWallGrid.SpawnFlowerMoonWithId(int.Parse(p[0]),
                    new Vector3(PF(p[1]), PF(p[2]), PF(p[3])), int.Parse(p[4]));
            }
            else if (msg.StartsWith("MOONGET:"))
            {
                var ids = new System.Collections.Generic.List<int>();
                foreach (string s in msg.Substring(8).Split(','))
                {
                    if (int.TryParse(s, out int id)) ids.Add(id);
                }
                gameplayWallGrid.RemoveMoonsRemote(ids);
            }
            else if (msg.StartsWith("CLASH:"))
            {
                string[] p = msg.Substring(6).Split(',');
                int r = int.Parse(p[0]);
                int c = int.Parse(p[1]);
                PlantType pType = PlantType.Rose;
                if (p.Length >= 3 && Enum.TryParse(p[2], out PlantType pt)) pType = pt;
                PlayerRole remoteRole = PlayerRole.None;
                if (p.Length >= 4 && Enum.TryParse(p[3], out PlayerRole rr)) remoteRole = rr;

                // Sync local role from remote peer's role if needed
                if (remoteRole == PlayerRole.Zombie && localRole != PlayerRole.Plant)
                {
                    localRole = PlayerRole.Plant;
                    if (gameplayWallGrid != null) gameplayWallGrid.SetLocalRole(PlayerRole.Plant);
                }
                else if (remoteRole == PlayerRole.Plant && localRole != PlayerRole.Zombie)
                {
                    localRole = PlayerRole.Zombie;
                    if (gameplayWallGrid != null) gameplayWallGrid.SetLocalRole(PlayerRole.Zombie);
                }

                // Ensure we are in Gameplay state so camera isn't pulled back to menu/transport target
                currentState = MenuState.Gameplay;
                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.gameplayActive = true;
                    gameplayWallGrid.TriggerRemoteClash(r, c, pType, remoteRole);
                }
            }
            else if (msg.StartsWith("APOSE:"))
            {
                string[] p = msg.Substring(6).Split(',');
                gameplayWallGrid.SetRemoteArenaPose(new Vector3(PF(p[0]), PF(p[1]), PF(p[2])), PF(p[3]));
            }
            else if (msg.StartsWith("ARESULT:"))
            {
                gameplayWallGrid.ResolveArena(msg.Substring(8) == "Z", notifyNetwork: false);
            }
            else if (msg.StartsWith("ASTUN:"))
            {
                float dur = PF(msg.Substring(6));
                gameplayWallGrid.ApplyLocalArenaImmobilize(dur);
            }
            else if (msg.StartsWith("AFX:"))
            {
                string[] p = msg.Substring(4).Split(',');
                if (p.Length >= 7)
                {
                    gameplayWallGrid.SpawnRemoteArenaAbilityFx(
                        p[0],
                        new Vector3(PF(p[1]), PF(p[2]), PF(p[3])),
                        new Vector3(PF(p[4]), PF(p[5]), PF(p[6]))
                    );
                }
            }
            else if (msg.StartsWith("ASEED:"))
            {
                if (int.TryParse(msg.Substring(6), out int seed))
                {
                    gameplayWallGrid.ApplyRemoteArenaSeed(seed);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Bad sync message '{msg}': {ex.Message}");
        }
    }

    private void HandleSkyMoonSpawned(int id, Vector3 start, float landY, int value)
    {
        SendNetworkMessage($"MOONSKY:{id},{F(start.x)},{F(start.y)},{F(start.z)},{F(landY)},{value}");
    }

    private void HandleFlowerMoonSpawned(int id, Vector3 pos, int value)
    {
        SendNetworkMessage($"MOONFLW:{id},{F(pos.x)},{F(pos.y)},{F(pos.z)},{value}");
    }

    private void HandleMoonsCollected(System.Collections.Generic.List<int> ids)
    {
        SendNetworkMessage("MOONGET:" + string.Join(",", ids));
    }

    private void HandleClashStarted(int row, int col, PlantType pType, PlayerRole role)
    {
        SendNetworkMessage($"CLASH:{row},{col},{pType},{role}");
    }

    private void HandleArenaPose(Vector3 pos, float yaw)
    {
        SendNetworkMessage($"APOSE:{F(pos.x)},{F(pos.y)},{F(pos.z)},{F(yaw)}");
    }

    private void HandleArenaResolved(bool zombieWon)
    {
        SendNetworkMessage("ARESULT:" + (zombieWon ? "Z" : "P"));
    }

    private void HandleArenaStun(float duration)
    {
        SendNetworkMessage($"ASTUN:{F(duration)}");
    }

    private void HandleArenaAbilityFx(string fxType, Vector3 start, Vector3 end)
    {
        SendNetworkMessage($"AFX:{fxType},{F(start.x)},{F(start.y)},{F(start.z)},{F(end.x)},{F(end.y)},{F(end.z)}");
    }

    private void HandleArenaSeed(int seed)
    {
        SendNetworkMessage($"ASEED:{seed}");
    }

    public void BeginTransportToGameplayWall()
    {
        EnsureGridEventsSubscribed();
        statusMessage = "Connected! Transporting to the Gameplay Wall...";
        if (gameplayWallGrid != null)
        {
            targetCameraPos = gameplayWallGrid.GetIdealCameraPosition();
        }
        else
        {
            targetCameraPos = new Vector3(0f, 4.5f, -10f);
        }

        // Also move Player object to the gameplay area if present
        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0f, 1.75f, -5.5f);
            if (cc != null) cc.enabled = true;
        }

        currentState = MenuState.TransportingToGameWall;
    }

    /// <summary>
    /// Host selects their role (Plant or Zombie). The remaining option is automatically assigned to the other player.
    /// </summary>
    public void HostChooseRole(PlayerRole chosenByHost)
    {
        if (!isHost) return;

        PlayerRole remainingForJoiner = (chosenByHost == PlayerRole.Plant)
            ? PlayerRole.Zombie
            : PlayerRole.Plant;

        ApplyRoleAssignment(chosenByHost, remainingForJoiner);
        SendNetworkMessage($"ROLES:{chosenByHost},{remainingForJoiner}");
    }

    private void ApplyRoleAssignment(PlayerRole hRole, PlayerRole jRole)
    {
        EnsureGridEventsSubscribed();
        hostRole = hRole;
        joinerRole = jRole;
        localRole = isHost ? hostRole : joinerRole;

        if (currentState != MenuState.TransportingToGameWall)
        {
            currentState = MenuState.Gameplay;
        }

        if (gameplayWallGrid == null)
        {
            gameplayWallGrid = FindFirstObjectByType<GridManager>();
        }

        if (gameplayWallGrid != null)
        {
            gameplayWallGrid.gameplayActive = true;
            gameplayWallGrid.SetLocalRole(localRole);
        }
    }

    private void HandleLocalPlantPlaced(int row, int col, PlantType type)
    {
        SendNetworkMessage($"PLACE:{row},{col},{type}");
    }

    private void HandleLocalZombiePlaced(int row, int col)
    {
        SendNetworkMessage($"ZOMBIE:{row},{col}");
    }

    private void HandleLocalMatchEnded(GridManager.MatchResult result)
    {
        string winner = (result == GridManager.MatchResult.PlantsWin) ? "Plants" : "Zombies";
        SendNetworkMessage($"WIN:{winner}");
    }

    private void HandleLocalMatchRestarted()
    {
        SendNetworkMessage("RESTART");
    }

    private static int FeistelRound(int x)
    {
        return ((x * 20077 + 4919) ^ (x >> 5)) & 0x7FFF;
    }

    /// <summary>
    /// Generates a random 6-character alphanumeric room code (Base-36) that encodes the host port with a random 19-bit salt.
    /// Used as an automatic offline fallback when no internet connection is available.
    /// </summary>
    public static string GenerateRandomJoinCode(int port)
    {
        int portOffset = Mathf.Clamp(port - 7100, 0, 0x7FF);
        int salt = UnityEngine.Random.Range(0, 1 << 19);
        int combined = (salt << 11) | (portOffset & 0x7FF);

        int left = (combined >> 15) & 0x7FFF;
        int right = combined & 0x7FFF;
        int left1 = (left ^ FeistelRound(right)) & 0x7FFF;
        int right1 = (right ^ FeistelRound(left1)) & 0x7FFF;
        int scrambled = (left1 << 15) | right1;

        char[] chars = new char[6];
        int val = scrambled;
        for (int i = 0; i < 6; i++)
        {
            chars[i] = Base36Alphabet[val % 36];
            val /= 36;
        }

        return new string(chars);
    }

    /// <summary>
    /// Decodes a 6-character room code back into the target port.
    /// Also supports IP suffix (e.g. "97S2J4@192.168.1.5") and legacy "ST7777".
    /// </summary>
    public static bool TryDecodeCodeToPort(string input, out int port, out string hostIp)
    {
        hostIp = "127.0.0.1";
        port = 7777;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string clean = input.Trim().ToUpperInvariant().Replace(" ", "");
        string codePart = clean;
        if (clean.Contains("@"))
        {
            string[] split = clean.Split('@');
            codePart = split[0].Trim();
            if (split.Length > 1 && !string.IsNullOrWhiteSpace(split[1]))
            {
                hostIp = split[1].Trim();
            }
        }

        // Backward compatibility for legacy STxxxx format
        if (codePart.StartsWith("ST") && codePart.Length == 6 && int.TryParse(codePart.Substring(2), out int legacyPort))
        {
            port = legacyPort;
            return true;
        }

        // 6-character Base-36 random code
        if (codePart.Length == 6)
        {
            long scrambled = 0;
            long multiplier = 1;
            for (int i = 0; i < 6; i++)
            {
                int index = Base36Alphabet.IndexOf(codePart[i]);
                if (index < 0)
                {
                    return false;
                }
                scrambled += index * multiplier;
                multiplier *= 36;
            }

            if (scrambled < 0 || scrambled >= (1L << 30))
            {
                return false;
            }

            int sc32 = (int)scrambled;
            int left1 = (sc32 >> 15) & 0x7FFF;
            int right1 = sc32 & 0x7FFF;
            int right = (right1 ^ FeistelRound(left1)) & 0x7FFF;
            int left = (left1 ^ FeistelRound(right)) & 0x7FFF;
            int combined = (left << 15) | right;

            int portOffset = combined & 0x7FF;
            port = portOffset + 7100;
            return port >= 7100 && port <= 9147;
        }

        return false;
    }

    private void DisposeRelayDriver()
    {
        relayConnection = default;
        while (outgoingRelayMessages.TryDequeue(out _)) { }
        if (relayDriver.IsCreated)
        {
            try
            {
                relayDriver.Dispose();
            }
            catch
            {
            }
            relayDriver = default;
        }
    }

    private void StopNetwork()
    {
        networkSessionId++;
        networkRunning = false;
        isUsingRelay = false;
        isRelayHostingActive = false;
        isAllocatingRelay = false;
        DisposeRelayDriver();

        try { netReader?.Close(); } catch { }
        try { netWriter?.Close(); } catch { }
        try { connectedClient?.Close(); } catch { }
        try { tcpServer?.Stop(); } catch { }
        netReader = null;
        netWriter = null;
        connectedClient = null;
        tcpServer = null;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ==================== 3D MENU BACKDROP ====================

    public void EnsureMenuBackdrop(bool force = false)
    {
        if (menuWallTransform == null) menuWallTransform = transform;
        Transform existing = menuWallTransform.Find("MenuBackdropRoot");
        if (existing != null)
        {
            // Remove previous version if scale was not normalized or missing 2D moon / stars
            if (force || existing.localScale.x > 0.5f || existing.Find("MenuStar_0") == null || existing.Find("MenuMoon") == null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing.gameObject);
                }
                else
                {
                    DestroyImmediate(existing.gameObject);
                }
            }
            else
            {
                return;
            }
        }

        GameObject root = new GameObject("MenuBackdropRoot");
        root.transform.SetParent(menuWallTransform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = new Vector3(
            1.0f / Mathf.Max(0.01f, wallWidth),
            1.0f / Mathf.Max(0.01f, wallHeight),
            1.0f / Mathf.Max(0.01f, wallThickness)
        );

        Color woodCol = new Color(0.42f, 0.28f, 0.16f);
        Color grassCol = new Color(0.24f, 0.54f, 0.22f);

        // Dusk wall background tint
        Renderer wallRend = menuWallTransform.GetComponent<Renderer>();
        if (wallRend != null)
        {
            wallRend.material = GameVisuals.GetColoredMaterial(new Color(0.12f, 0.16f, 0.28f), 0.15f);
        }

        // Celestial star sparkles on wall
        for (int i = 0; i < 14; i++)
        {
            GameObject star = GameObject.CreatePrimitive(PrimitiveType.Cube);
            star.name = $"MenuStar_{i}";
            star.transform.SetParent(root.transform, false);
            float rx = ((i * 3.7f + 1.2f) % 15f) - 7.5f;
            float ry = 0.5f + ((i * 2.1f) % 3.5f);
            star.transform.localPosition = new Vector3(rx, ry, -0.15f);
            star.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            star.transform.localScale = new Vector3(0.15f, 0.15f, 0.05f);
            DestroyCollider(star);
            star.GetComponent<Renderer>().material = GameVisuals.GetColoredMaterial(new Color(1.0f, 0.95f, 0.75f), 0.9f);
        }

        // Lawn strip at bottom of menu wall
        GameObject lawn = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lawn.name = "MenuLawn";
        lawn.transform.SetParent(root.transform, false);
        lawn.transform.localPosition = new Vector3(0f, -3.8f, -0.2f);
        lawn.transform.localScale = new Vector3(16.2f, 1.4f, 0.25f);
        DestroyCollider(lawn);
        lawn.GetComponent<Renderer>().material = GameVisuals.GetColoredMaterial(grassCol);

        // Picket fence slats
        for (int i = -7; i <= 7; i++)
        {
            GameObject slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slat.name = $"FenceSlat_{i}";
            slat.transform.SetParent(root.transform, false);
            slat.transform.localPosition = new Vector3(i * 1.05f, -2.7f, -0.25f);
            slat.transform.localScale = new Vector3(0.38f, 1.7f, 0.12f);
            DestroyCollider(slat);
            slat.GetComponent<Renderer>().material = GameVisuals.GetColoredMaterial(woodCol);
        }

        // Horizontal fence rails
        GameObject railTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        railTop.transform.SetParent(root.transform, false);
        railTop.transform.localPosition = new Vector3(0f, -2.2f, -0.28f);
        railTop.transform.localScale = new Vector3(16.0f, 0.22f, 0.12f);
        DestroyCollider(railTop);
        railTop.GetComponent<Renderer>().material = GameVisuals.GetColoredMaterial(woodCol);

        GameObject railBot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        railBot.transform.SetParent(root.transform, false);
        railBot.transform.localPosition = new Vector3(0f, -3.2f, -0.28f);
        railBot.transform.localScale = new Vector3(16.0f, 0.22f, 0.12f);
        DestroyCollider(railBot);
        railBot.GetComponent<Renderer>().material = GameVisuals.GetColoredMaterial(woodCol);

        // 2D Animated Moonflower on left
        GameObject moonflower = GameVisuals.CreatePlant(PlantType.Moonflower, "MenuMoonflower", false, 2.0f, 2.0f);
        moonflower.transform.SetParent(root.transform, false);
        moonflower.transform.localPosition = new Vector3(-5.2f, -2.5f, -0.6f);
        moonflower.transform.localRotation = Quaternion.identity;

        // 2D Animated Rose on left
        GameObject rose = GameVisuals.CreatePlant(PlantType.Rose, "MenuRose", false, 2.0f, 2.0f);
        rose.transform.SetParent(root.transform, false);
        rose.transform.localPosition = new Vector3(-3.2f, -2.5f, -0.6f);
        rose.transform.localRotation = Quaternion.identity;

        // 2D Animated Zombie on right
        GameObject zombie = GameVisuals.CreateZombie("MenuZombie", false, 2.0f, 2.0f);
        zombie.transform.SetParent(root.transform, false);
        zombie.transform.localPosition = new Vector3(5.0f, -2.4f, -0.6f);
        zombie.transform.localRotation = Quaternion.identity;
        PlantBobber zBob = zombie.AddComponent<PlantBobber>();
        zBob.breatheSpeed = 2.0f;
        zBob.swayAngle = 3.5f;

        // 2D Floating Celestial Moon in night sky
        GameObject moon = GameVisuals.CreateMoon("MenuMoon", 0.65f);
        moon.transform.SetParent(root.transform, false);
        moon.transform.localPosition = new Vector3(-5.2f, 2.4f, -0.6f);

        // Warm Menu Point Light
        GameObject lightObj = new GameObject("MenuWarmLight");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.5f, -3.5f);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 22f;
        light.color = new Color(1.0f, 0.95f, 0.88f);
        light.intensity = 1.6f;
    }

    private void DestroyCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
            {
                Destroy(col);
            }
            else
            {
                DestroyImmediate(col);
            }
        }
    }

    // ==================== ON-SCREEN MENU & ROLE BOXES UI ====================

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Font font = menuFont != null ? menuFont : FindSophiecomicFont();
        if (font != null)
        {
            GUI.skin.font = font;
        }

        float sw = Screen.width;
        float sh = Screen.height;
        float centerX = sw * 0.5f;
        float centerY = sh * 0.5f;

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        titleStyle.normal.textColor = Color.white;

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        subStyle.normal.textColor = new Color(0.88f, 0.94f, 1.0f);

        GUIStyle bigBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        GUIStyle normalBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        // 1. HOW TO PLAY MODAL OVERLAY
        if (showHowToPlayModal)
        {
            DrawHowToPlayModal(sw, sh, centerX, centerY);
            return;
        }

        if (currentState == MenuState.MainMenu)
        {
            Rect cardRect = new Rect(centerX - 260f, centerY - 225f, 520f, 450f);
            GUI.DrawTexture(cardRect, GameVisuals.GetMenuTexture("MenuContainer"), ScaleMode.StretchToFill);

            // 1. Carved Wood Title Slab
            Rect titleRect = new Rect(centerX - 195f, centerY - 200f, 390f, 80f);
            GUI.DrawTexture(titleRect, GameVisuals.GetMenuTexture("MenuTitleBox"), ScaleMode.StretchToFill);

            GUIStyle pvzTitleStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold
            };
            Rect titleTopRect = new Rect(titleRect.x, titleRect.y + 7f, titleRect.width, 36f);
            Rect titleTopShadow = new Rect(titleTopRect.x + 2f, titleTopRect.y + 2f, titleTopRect.width, titleTopRect.height);
            GUI.Label(titleTopShadow, "<b><color=#000000>PLANTS vs. ZOMBIES</color></b>", pvzTitleStyle);
            GUI.Label(titleTopRect, "<b><color=#3cf277>PLANTS</color> <size=14><color=#f8fafc>vs.</color></size> <color=#d8b4fe>ZOMBIES</color></b>", pvzTitleStyle);

            GUIStyle moonSubStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            Rect titleBotRect = new Rect(titleRect.x, titleRect.y + 44f, titleRect.width, 24f);
            Rect titleBotShadow = new Rect(titleBotRect.x + 1.5f, titleBotRect.y + 1.5f, titleBotRect.width, titleBotRect.height);
            GUI.Label(titleBotShadow, "<b><color=#000000>M O O N   D E F E N S E</color></b>", moonSubStyle);
            GUI.Label(titleBotRect, "<b><color=#facc15>M O O N   D E F E N S E</color></b>", moonSubStyle);

            // 2. Start Adventure Button
            GUIStyle advBtnStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(44, 0, 0, 0)
            };
            advBtnStyle.normal.textColor = Color.white;
            Rect advRect = new Rect(centerX - 225f, centerY - 95f, 450f, 72f);
            if (DrawPvZButton(advRect, "START ADVENTURE", "BtnAdventure", advBtnStyle))
            {
                statusMessage = "";
                currentState = MenuState.HostOrJoinSelect;
            }

            // 3. Controls & Guide Button
            GUIStyle ctrlBtnStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(44, 0, 0, 0)
            };
            ctrlBtnStyle.normal.textColor = new Color(1.0f, 0.94f, 0.55f);
            Rect ctrlRect = new Rect(centerX - 225f, centerY - 5f, 450f, 66f);
            if (DrawPvZButton(ctrlRect, "CONTROLS & GUIDE", "BtnControls", ctrlBtnStyle))
            {
                showHowToPlayModal = true;
            }

            // 4. Quit Game Button
            GUIStyle quitBtnStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(44, 0, 0, 0)
            };
            quitBtnStyle.normal.textColor = new Color(0.95f, 0.96f, 0.98f);
            Rect quitRect = new Rect(centerX - 225f, centerY + 80f, 450f, 60f);
            if (DrawPvZButton(quitRect, "QUIT GAME", "BtnQuit", quitBtnStyle))
            {
                QuitGame();
            }

            // Footer
            GUIStyle footerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            footerStyle.normal.textColor = new Color(0.45f, 0.55f, 0.70f);
            GUI.Label(new Rect(centerX - 250f, centerY + 160f, 500f, 24f), "v1.3.0 • 16:9 Orthographic • Online Relay Ready", footerStyle);
        }
        else if (currentState == MenuState.HostOrJoinSelect)
        {
            Rect cardRect = new Rect(centerX - 310f, centerY - 230f, 620f, 460f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.95f), new Color(0.35f, 0.75f, 1.0f, 0.75f));

            GUI.Label(new Rect(centerX - 280f, centerY - 200f, 560f, 42f), "SELECT MULTIPLAYER MODE", titleStyle);
            GUI.Label(new Rect(centerX - 280f, centerY - 155f, 560f, 30f), "Connect with a friend on ANY network or PC:", subStyle);

            GUI.backgroundColor = new Color(0.25f, 0.75f, 0.98f);
            if (GUI.Button(new Rect(centerX - 280f, centerY - 105f, 270f, 185f),
                "👑 HOST GAME\n\nCreate an online lobby\n& get a 6-character code\nto invite Player 2!", bigBtn))
            {
                StartHosting();
            }

            GUI.backgroundColor = new Color(0.30f, 0.90f, 0.45f);
            if (GUI.Button(new Rect(centerX + 10f, centerY - 105f, 270f, 185f),
                "🔗 JOIN GAME\n\nEnter the Host's\n6-character code\nto connect from anywhere!", bigBtn))
            {
                statusMessage = "Enter the Host's 6-character code below:";
                currentState = MenuState.JoiningEnterCode;
            }

            GUI.backgroundColor = new Color(0.75f, 0.75f, 0.78f);
            if (GUI.Button(new Rect(centerX - 120f, centerY + 115f, 240f, 48f), "← Back to Main Menu", normalBtn))
            {
                currentState = MenuState.MainMenu;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.HostingWaitingForPlayer)
        {
            Rect cardRect = new Rect(centerX - 320f, centerY - 265f, 640f, 530f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.96f), new Color(0.35f, 0.95f, 0.55f, 0.85f));

            GUI.Label(new Rect(centerX - 300f, centerY - 235f, 600f, 42f), "HOSTING LOBBY", titleStyle);
            GUI.Label(new Rect(centerX - 300f, centerY - 190f, 600f, 30f), "Share this 6-Character Join Code with Player 2:", subStyle);

            // Spaced-out Code Box for maximum legibility
            string spacedCode = string.Join("   ", (hostJoinCode ?? "").ToCharArray());
            GUIStyle codeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            codeStyle.normal.textColor = new Color(0.35f, 1.0f, 0.55f);
            GUI.Box(new Rect(centerX - 210f, centerY - 150f, 420f, 70f), spacedCode, codeStyle);

            // Copy & New Code buttons
            string copyLabel = (codeCopiedTimer > 0f) ? "✓ Copied to Clipboard!" : "📋 Copy Code";
            GUI.backgroundColor = (codeCopiedTimer > 0f) ? new Color(0.3f, 0.95f, 0.5f) : new Color(0.32f, 0.75f, 0.98f);
            if (GUI.Button(new Rect(centerX - 210f, centerY - 70f, 250f, 44f), copyLabel, normalBtn))
            {
                if (!isAllocatingRelay && !string.IsNullOrEmpty(hostJoinCode))
                {
                    GUIUtility.systemCopyBuffer = hostJoinCode;
                    codeCopiedTimer = 2.5f;
                    statusMessage = "Code copied to clipboard! Share it with Player 2.";
                }
            }

            GUI.backgroundColor = new Color(0.85f, 0.85f, 0.90f);
            if (GUI.Button(new Rect(centerX + 50f, centerY - 70f, 160f, 44f), "🔄 New Code", normalBtn))
            {
                if (!isAllocatingRelay)
                {
                    codeCopiedTimer = 0f;
                    StartHosting();
                }
            }

            // Generous Status & Network info box (Word-wrap with ample height, NEVER clips)
            string pulse = (Time.time % 1.0f < 0.5f) ? "●" : "○";
            string modeLabel = isRelayHostingActive
                ? "🌐 Mode: ONLINE RELAY (Works across ANY Wi-Fi / Network / PC)"
                : (isAllocatingRelay ? "⏳ Allocating Unity Cloud Relay Room..." : $"🏠 Mode: Local / LAN (Port {hostingPort})");
            string fullStatus = $"{pulse} WAITING FOR PLAYER 2 TO CONNECT...\n{modeLabel}\n{statusMessage}";

            GUIStyle statusBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            statusBoxStyle.normal.textColor = new Color(0.9f, 0.95f, 1.0f);
            GUI.backgroundColor = new Color(0.12f, 0.18f, 0.28f, 0.95f);
            GUI.Box(new Rect(centerX - 280f, centerY - 12f, 560f, 75f), fullStatus, statusBoxStyle);

            // Solo Test Helper
            GUI.backgroundColor = new Color(0.32f, 0.92f, 0.48f);
            if (GUI.Button(new Rect(centerX - 220f, centerY + 80f, 440f, 50f), "⚡ Simulate Player 2 Joined (Solo Test Now)", bigBtn))
            {
                BeginTransportToGameplayWall();
            }

            // Cancel button
            GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
            if (GUI.Button(new Rect(centerX - 120f, centerY + 145f, 240f, 44f), "✕ Cancel & Return", normalBtn))
            {
                StopNetwork();
                currentState = MenuState.HostOrJoinSelect;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.JoiningEnterCode)
        {
            Rect cardRect = new Rect(centerX - 310f, centerY - 240f, 620f, 480f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.96f), new Color(0.35f, 0.85f, 1.0f, 0.85f));

            GUI.Label(new Rect(centerX - 280f, centerY - 210f, 560f, 42f), "JOIN GAME LOBBY", titleStyle);
            GUI.Label(new Rect(centerX - 280f, centerY - 165f, 560f, 30f), "Enter the Host's 6-character room code:", subStyle);

            GUIStyle inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            // Input field + Paste button
            enteredJoinCode = GUI.TextField(new Rect(centerX - 190f, centerY - 125f, 260f, 58f), enteredJoinCode ?? "", 24, inputStyle);

            GUI.backgroundColor = new Color(0.35f, 0.75f, 0.95f);
            if (GUI.Button(new Rect(centerX + 80f, centerY - 125f, 110f, 58f), "📋 Paste", normalBtn))
            {
                enteredJoinCode = (GUIUtility.systemCopyBuffer ?? "").Trim().ToUpperInvariant();
            }

            // Status message box (Generous height, word-wrap, NEVER clips)
            GUIStyle statusBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            statusBoxStyle.normal.textColor = new Color(1.0f, 0.92f, 0.45f);
            GUI.backgroundColor = new Color(0.12f, 0.18f, 0.28f, 0.95f);
            GUI.Box(new Rect(centerX - 270f, centerY - 52f, 540f, 68f), statusMessage, statusBoxStyle);

            GUI.backgroundColor = new Color(0.30f, 0.92f, 0.48f);
            if (GUI.Button(new Rect(centerX - 180f, centerY + 30f, 360f, 60f), "▶ CONNECT TO HOST", bigBtn))
            {
                ConnectAsJoiner();
            }

            GUI.backgroundColor = new Color(0.75f, 0.75f, 0.78f);
            if (GUI.Button(new Rect(centerX - 110f, centerY + 105f, 220f, 44f), "← Back", normalBtn))
            {
                StopNetwork();
                currentState = MenuState.HostOrJoinSelect;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.RoleSelection)
        {
            Rect cardRect = new Rect(centerX - 420f, centerY - 250f, 840f, 500f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.96f), new Color(0.45f, 0.85f, 1.0f, 0.85f));

            GUI.Label(new Rect(centerX - 380f, centerY - 225f, 760f, 42f), "CHOOSE YOUR SIDE", titleStyle);

            string prompt = isHost
                ? "👑 HOST: Click a side below! The remaining role is automatically assigned to Player 2."
                : "⏳ WAITING FOR HOST TO CHOOSE... (You will automatically receive the remaining side)";
            GUI.Label(new Rect(centerX - 380f, centerY - 180f, 760f, 45f), prompt, subStyle);

            // TWO ROLE CARDS: PLANT AND ZOMBIE
            Rect plantBoxRect = new Rect(centerX - 390f, centerY - 120f, 370f, 280f);
            Rect zombieBoxRect = new Rect(centerX + 20f, centerY - 120f, 370f, 280f);

            GUI.backgroundColor = new Color(0.24f, 0.88f, 0.38f);
            string plantText = "🌿 PLANT DEFENDER\n\n• Protect the 5x8 Garden Wall\n• Harvest Moons from sky & flowers\n• Place Roses to shoot Zombies\n• Place Durians to block lanes\n• Survive for 5:00 to Win!\n\n" + (isHost ? "[ CLICK TO CHOOSE PLANTS ]" : "[ HOST CHOOSES ]");
            if (GUI.Button(plantBoxRect, plantText, bigBtn))
            {
                if (isHost)
                {
                    HostChooseRole(PlayerRole.Plant);
                }
            }

            GUI.backgroundColor = new Color(0.78f, 0.32f, 0.95f);
            string zombieText = "🧟 ZOMBIE INVADER\n\n• Assault the Garden Perimeter\n• Earn Brains every 10 seconds\n• Deploy Zombies in right lanes\n• Cross the Red Line to Win!\n• Touch Plants to enter 3D Arena!\n\n" + (isHost ? "[ CLICK TO CHOOSE ZOMBIES ]" : "[ HOST CHOOSES ]");
            if (GUI.Button(zombieBoxRect, zombieText, bigBtn))
            {
                if (isHost)
                {
                    HostChooseRole(PlayerRole.Zombie);
                }
            }

            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.Gameplay)
        {
            if (gameplayWallGrid != null && gameplayWallGrid.is2DWorldPaused)
            {
                return;
            }

            PlayerRole otherRole = isHost ? joinerRole : hostRole;
            string roleBanner = $"YOU: {localRole.ToString().ToUpper()}  |  OTHER: {otherRole.ToString().ToUpper()}";

            Rect bannerRect = new Rect(sw - 235f, 12f, 220f, 34f);
            DrawPanelBox(bannerRect, new Color(0.08f, 0.12f, 0.18f, 0.92f), new Color(0.35f, 0.75f, 1.0f, 0.75f));

            GUIStyle bannerStyle = new GUIStyle(subStyle) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(bannerRect.x + 6f, bannerRect.y + 4f, bannerRect.width - 12f, 26f), roleBanner, bannerStyle);

            GUI.backgroundColor = new Color(0.35f, 0.75f, 0.95f);
            GUIStyle switchBtnStyle = new GUIStyle(normalBtn) { fontSize = 11, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(sw - 165f, 50f, 150f, 32f), "Switch Role (Test)", switchBtnStyle))
            {
                localRole = (localRole == PlayerRole.Plant) ? PlayerRole.Zombie : PlayerRole.Plant;
                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.SetLocalRole(localRole);
                }
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private static Font FindSophiecomicFont()
    {
        if (s_SearchedSophieFont)
        {
            return s_CachedSophieFont;
        }

        s_SearchedSophieFont = true;
        Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (Font font in fonts)
        {
            if (font != null && font.name.Contains("Sophiecomic"))
            {
                s_CachedSophieFont = font;
                return s_CachedSophieFont;
            }
        }

        return null;
    }

    private void DrawHowToPlayModal(float sw, float sh, float centerX, float centerY)
    {
        // Dark dimming overlay
        Color prev = GUI.color;
        GUI.color = new Color(0.02f, 0.02f, 0.04f, 0.85f);
        GUI.Box(new Rect(0f, 0f, sw, sh), GUIContent.none);
        GUI.color = prev;

        Rect modalRect = new Rect(centerX - 360f, centerY - 260f, 720f, 520f);
        DrawPanelBox(modalRect, new Color(0.08f, 0.12f, 0.18f, 0.98f), new Color(0.35f, 0.85f, 1.0f, 0.9f));

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = new Color(0.45f, 0.95f, 0.65f);
        GUI.Label(new Rect(centerX - 330f, centerY - 235f, 660f, 36f), "HOW TO PLAY & GAME RULES", titleStyle);

        GUIStyle guideText = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true
        };
        guideText.normal.textColor = Color.white;

        string guideContent =
            "🌿 PLANT DEFENDER GOAL:\n" +
            "• Protect the 5x8 garden wall for 5:00 minutes against the zombie onslaught.\n" +
            "• Collect Moons falling from the sky or produced by Moonflowers.\n" +
            "• [1] Moonflower (50 Moons): Produces 50 Moons every 15 seconds.\n" +
            "• [2] Rose (100 Moons): Fires thorn darts whenever zombies enter its lane.\n" +
            "• [3] Durian (50 Moons): High-HP armored defender that blocks zombie advances.\n\n" +
            "🧟 ZOMBIE INVADER GOAL:\n" +
            "• Earn Brains every 10 seconds. Spawn zombies in the rightmost 2 columns.\n" +
            "• [4] Zombie (50 Brains): Walks forward toward the red defense line.\n" +
            "• If any Zombie crosses the Red Defense Line on the left, Zombies Win!\n\n" +
            "⚔️ 3D 1v1 ARENA CLASH:\n" +
            "• When a Zombie collides with any Plant, the 2D world pauses immediately!\n" +
            "• You are transported into a 3D first-person clash arena to fight!\n" +
            "• Controls: WASD to Move & Tilt, Space to Jump, Backspace to return to 2D Wall.";

        GUI.Label(new Rect(centerX - 330f, centerY - 185f, 660f, 340f), guideContent, guideText);

        GUI.backgroundColor = new Color(0.24f, 0.88f, 0.38f);
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        if (GUI.Button(new Rect(centerX - 130f, centerY + 185f, 260f, 48f), "GOT IT! CLOSE GUIDE", btnStyle))
        {
            showHowToPlayModal = false;
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawPanelBox(Rect rect, Color bg, Color border)
    {
        Color prev = GUI.color;
        // Border outline
        GUI.color = border;
        GUI.Box(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), GUIContent.none);
        // Background card
        GUI.color = bg;
        GUI.Box(rect, GUIContent.none);
        GUI.color = prev;
    }

    private void DrawPanelBox(Rect rect, Color color)
    {
        DrawPanelBox(rect, color, new Color(0.35f, 0.65f, 0.95f, 0.6f));
    }

    private bool DrawPvZButton(Rect rect, string text, string keyPrefix, GUIStyle textStyle)
    {
        Vector2 mousePos = Event.current.mousePosition;
        bool isHover = rect.Contains(mousePos);
        bool isMouseDown = isHover && (Event.current.type == EventType.MouseDown || (Event.current.type == EventType.MouseDrag && GUIUtility.hotControl != 0));

        string stateSuffix = isMouseDown ? "Pressed" : (isHover ? "Hover" : "Normal");
        Texture2D tex = GameVisuals.GetMenuTexture($"{keyPrefix}_{stateSuffix}");

        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);

        // Text with drop shadow
        float yOffset = isMouseDown ? 2f : 0f;
        Rect shadowRect = new Rect(rect.x + 2f, rect.y + 2f + yOffset, rect.width, rect.height);
        Rect textRect = new Rect(rect.x, rect.y + yOffset, rect.width, rect.height);

        Color origColor = textStyle.normal.textColor;
        textStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(shadowRect, text, textStyle);
        textStyle.normal.textColor = origColor;
        GUI.Label(textRect, text, textStyle);

        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }
}
