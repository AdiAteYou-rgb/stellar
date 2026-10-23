using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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

    // Networking internals (TCP P2P for instant local/LAN + code encoding)
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
    private float codeCopiedTimer = 0f;

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
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.lockCameraToThisWall = false;
                gameplayWallGrid.gameplayActive = false;
                gameplayWallGrid.OnPlantPlacedLocal += HandleLocalPlantPlaced;
                gameplayWallGrid.OnZombiePlacedLocal += HandleLocalZombiePlaced;
                gameplayWallGrid.OnMatchEndedLocal += HandleLocalMatchEnded;
                gameplayWallGrid.OnMatchRestartedLocal += HandleLocalMatchRestarted;
            }
            SnapCameraToMenuWall();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            currentState = MenuState.MainMenu;
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.lockCameraToThisWall = false;
                gameplayWallGrid.gameplayActive = false;
            }
            SnapCameraToMenuWall();
        }
    }

    private void OnDestroy()
    {
        if (gameplayWallGrid != null)
        {
            gameplayWallGrid.OnPlantPlacedLocal -= HandleLocalPlantPlaced;
            gameplayWallGrid.OnZombiePlacedLocal -= HandleLocalZombiePlaced;
            gameplayWallGrid.OnMatchEndedLocal -= HandleLocalMatchEnded;
            gameplayWallGrid.OnMatchRestartedLocal -= HandleLocalMatchRestarted;
        }
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

        // Process incoming network messages on the Unity main thread
        while (incomingMessages.TryDequeue(out string msg))
        {
            ProcessNetworkMessage(msg);
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
                    currentState = MenuState.RoleSelection;
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

    // ==================== HOST & JOIN NETWORKING ====================

    public void StartHosting()
    {
        StopNetwork();
        isHost = true;
        hostRole = PlayerRole.None;
        joinerRole = PlayerRole.None;
        localRole = PlayerRole.None;

        try
        {
            // Pick a random starting port in range 7200..8800 so ports aren't always 7777
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

            if (tcpServer == null)
            {
                statusMessage = "Could not find an available port to host on!";
                return;
            }

            hostJoinCode = GenerateRandomJoinCode(hostingPort);
            statusMessage = "Waiting for the other player to enter the code...";
            currentState = MenuState.HostingWaitingForPlayer;

            networkRunning = true;
            networkThread = new Thread(HostAcceptLoop) { IsBackground = true };
            networkThread.Start();
        }
        catch (Exception ex)
        {
            statusMessage = "Host error: " + ex.Message;
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
            statusMessage = "Please enter the code from the Host!";
            return;
        }

        if (!TryDecodeCodeToPort(cleanCode, out int port, out string hostIp))
        {
            statusMessage = "Invalid code format! Ask the Host for their 6-character code.";
            return;
        }

        StopNetwork();
        isHost = false;
        hostRole = PlayerRole.None;
        joinerRole = PlayerRole.None;
        localRole = PlayerRole.None;
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
                    incomingMessages.Enqueue("ERROR:Could not connect to Host. Make sure Host is waiting!");
                    return;
                }

                connectedClient.EndConnect(result);
                NetworkStream stream = connectedClient.GetStream();
                netReader = new StreamReader(stream);
                netWriter = new StreamWriter(stream) { AutoFlush = true };

                // Extract raw 6-char code part if IP was prefixed
                string rawCode = cleanCode.Contains("@") ? cleanCode.Split('@')[0] : cleanCode;
                netWriter.WriteLine("JOIN:" + rawCode);

                string response = netReader.ReadLine();
                if (response == "ACCEPTED")
                {
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
    }

    public void BeginTransportToGameplayWall()
    {
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
        hostRole = hRole;
        joinerRole = jRole;
        localRole = isHost ? hostRole : joinerRole;

        currentState = MenuState.Gameplay;

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
    /// Every time you click Host, a totally new, unique room code is generated (e.g. 97S2J4, Z6C5H1, 7LR3O6).
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

    private void StopNetwork()
    {
        networkRunning = false;
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
            Rect cardRect = new Rect(centerX - 270f, centerY - 240f, 540f, 480f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.95f), new Color(0.35f, 0.75f, 1.0f, 0.75f));

            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            badgeStyle.normal.textColor = new Color(0.45f, 0.95f, 0.65f);
            GUI.Label(new Rect(centerX - 250f, centerY - 215f, 500f, 26f), "🌿 MULTIPLAYER GARDEN DEFENSE 🧟", badgeStyle);

            GUI.Label(new Rect(centerX - 250f, centerY - 185f, 500f, 44f), "PLANTS VS ZOMBIES", titleStyle);

            GUIStyle goldSub = new GUIStyle(subStyle) { fontSize = 18 };
            goldSub.normal.textColor = new Color(1.0f, 0.88f, 0.35f);
            GUI.Label(new Rect(centerX - 250f, centerY - 140f, 500f, 32f), "5x8 TACTICAL WALL DEFENSE", goldSub);

            GUIStyle descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.85f, 0.90f, 0.98f);
            GUI.Label(new Rect(centerX - 240f, centerY - 105f, 480f, 44f),
                "Defend the 5x8 garden wall for 5 minutes, or breach the perimeter with the zombie horde!", descStyle);

            GUI.backgroundColor = new Color(0.24f, 0.88f, 0.38f);
            if (GUI.Button(new Rect(centerX - 170f, centerY - 45f, 340f, 62f), "PLAY GAME", bigBtn))
            {
                statusMessage = "";
                currentState = MenuState.HostOrJoinSelect;
            }

            GUI.backgroundColor = new Color(0.28f, 0.60f, 0.92f);
            if (GUI.Button(new Rect(centerX - 170f, centerY + 28f, 340f, 50f), "HOW TO PLAY & CONTROLS", normalBtn))
            {
                showHowToPlayModal = true;
            }

            GUI.backgroundColor = new Color(0.85f, 0.30f, 0.30f);
            if (GUI.Button(new Rect(centerX - 170f, centerY + 88f, 340f, 46f), "QUIT GAME", normalBtn))
            {
                QuitGame();
            }

            GUI.backgroundColor = Color.white;
            GUIStyle footerStyle = new GUIStyle(descStyle) { fontSize = 11 };
            footerStyle.normal.textColor = new Color(0.6f, 0.7f, 0.85f);
            GUI.Label(new Rect(centerX - 250f, centerY + 160f, 500f, 24f), "v1.2.0 • 16:9 Orthographic • Netcode Ready", footerStyle);
        }
        else if (currentState == MenuState.HostOrJoinSelect)
        {
            Rect cardRect = new Rect(centerX - 310f, centerY - 230f, 620f, 460f);
            DrawPanelBox(cardRect, new Color(0.08f, 0.12f, 0.18f, 0.95f), new Color(0.35f, 0.75f, 1.0f, 0.75f));

            GUI.Label(new Rect(centerX - 280f, centerY - 200f, 560f, 42f), "SELECT MULTIPLAYER MODE", titleStyle);
            GUI.Label(new Rect(centerX - 280f, centerY - 155f, 560f, 30f), "Choose how you want to connect with another player:", subStyle);

            GUI.backgroundColor = new Color(0.25f, 0.75f, 0.98f);
            if (GUI.Button(new Rect(centerX - 280f, centerY - 105f, 270f, 185f),
                "👑 HOST GAME\n\nCreate a new lobby\n& get a 6-character code\nto invite Player 2!", bigBtn))
            {
                StartHosting();
            }

            GUI.backgroundColor = new Color(0.30f, 0.90f, 0.45f);
            if (GUI.Button(new Rect(centerX + 10f, centerY - 105f, 270f, 185f),
                "🔗 JOIN GAME\n\nEnter the Host's\n6-character code\nto connect instantly!", bigBtn))
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
            string spacedCode = string.Join("   ", hostJoinCode.ToCharArray());
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
                GUIUtility.systemCopyBuffer = hostJoinCode;
                codeCopiedTimer = 2.5f;
                statusMessage = "Code copied to clipboard! Share it with Player 2.";
            }

            GUI.backgroundColor = new Color(0.85f, 0.85f, 0.90f);
            if (GUI.Button(new Rect(centerX + 50f, centerY - 70f, 160f, 44f), "🔄 New Code", normalBtn))
            {
                hostJoinCode = GenerateRandomJoinCode(hostingPort);
                codeCopiedTimer = 0f;
                statusMessage = "Generated a new 6-character room code!";
            }

            // Generous Status & Network info box (Word-wrap with ample height, NEVER clips)
            string pulse = (Time.time % 1.0f < 0.5f) ? "●" : "○";
            string fullStatus = $"{pulse} WAITING FOR PLAYER 2 TO CONNECT...\nLocal Port: {hostingPort}   |   LAN Format: {hostJoinCode}@127.0.0.1\n{statusMessage}";

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
}
