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

    private Vector3 targetCameraPos;

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

        if (Application.isPlaying)
        {
            currentState = MenuState.MainMenu;
            if (gameplayWallGrid != null)
            {
                gameplayWallGrid.lockCameraToThisWall = false;
                gameplayWallGrid.gameplayActive = false;
                gameplayWallGrid.OnPlantPlacedLocal += HandleLocalPlantPlaced;
            }
            SnapCameraToMenuWall();
        }
    }

    private void OnDestroy()
    {
        if (gameplayWallGrid != null)
        {
            gameplayWallGrid.OnPlantPlacedLocal -= HandleLocalPlantPlaced;
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
            // Listen on an available port in range 7770..7869
            int chosenPort = 7777;
            for (int p = 7777; p < 7850; p++)
            {
                try
                {
                    tcpServer = new TcpListener(IPAddress.Any, p);
                    tcpServer.Start();
                    chosenPort = p;
                    break;
                }
                catch
                {
                    tcpServer = null;
                }
            }

            hostJoinCode = EncodePortToCode(chosenPort);
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
                if (line != null && line == "JOIN:" + hostJoinCode)
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
        string cleanCode = (enteredJoinCode ?? "").Trim().ToUpperInvariant();
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
            if (coords.Length == 2 &&
                int.TryParse(coords[0], out int r) &&
                int.TryParse(coords[1], out int c))
            {
                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.PlacePlantOnTile(r, c, notifyNetwork: false);
                }
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
            // Plant player can place plants on the 5x8 wall (also enabled if testing)
            gameplayWallGrid.canPlacePlants = (localRole == PlayerRole.Plant);
        }
    }

    private void HandleLocalPlantPlaced(int row, int col)
    {
        SendNetworkMessage($"PLACE:{row},{col}");
    }

    // Encodes port (e.g. 7777) into a friendly 6-character code like "ST7777"
    private string EncodePortToCode(int port)
    {
        return $"ST{port:D4}";
    }

    private bool TryDecodeCodeToPort(string input, out int port, out string hostIp)
    {
        hostIp = "127.0.0.1";
        port = 7777;

        string codePart = input;
        if (input.Contains("@"))
        {
            string[] split = input.Split('@');
            codePart = split[0].Trim();
            hostIp = split[1].Trim();
        }

        if (codePart.StartsWith("ST") && codePart.Length == 6 && int.TryParse(codePart.Substring(2), out int parsedPort))
        {
            port = parsedPort;
            return true;
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
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = Color.white;

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = new Color(0.9f, 0.95f, 1.0f);

        GUIStyle bigBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        if (currentState == MenuState.MainMenu)
        {
            DrawPanelBox(new Rect(centerX - 240f, centerY - 180f, 480f, 360f), new Color(0.1f, 0.14f, 0.22f, 0.92f));
            GUI.Label(new Rect(centerX - 220f, centerY - 150f, 440f, 60f), "PLANTS VS ZOMBIES\nWALL DEFENSE", titleStyle);

            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUI.Button(new Rect(centerX - 150f, centerY - 40f, 300f, 70f), "PLAY", bigBtn))
            {
                statusMessage = "";
                currentState = MenuState.HostOrJoinSelect;
            }

            GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
            if (GUI.Button(new Rect(centerX - 150f, centerY + 55f, 300f, 70f), "QUIT", bigBtn))
            {
                QuitGame();
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.HostOrJoinSelect)
        {
            DrawPanelBox(new Rect(centerX - 260f, centerY - 200f, 520f, 400f), new Color(0.1f, 0.14f, 0.22f, 0.92f));
            GUI.Label(new Rect(centerX - 240f, centerY - 175f, 480f, 50f), "SELECT MULTIPLAYER MODE", titleStyle);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 1.0f);
            if (GUI.Button(new Rect(centerX - 160f, centerY - 85f, 320f, 75f), "HOST GAME", bigBtn))
            {
                StartHosting();
            }

            GUI.backgroundColor = new Color(0.4f, 0.95f, 0.5f);
            if (GUI.Button(new Rect(centerX - 160f, centerY + 10f, 320f, 75f), "JOIN GAME", bigBtn))
            {
                statusMessage = "Enter the Host's 6-character code below:";
                currentState = MenuState.JoiningEnterCode;
            }

            GUI.backgroundColor = new Color(0.75f, 0.75f, 0.75f);
            if (GUI.Button(new Rect(centerX - 100f, centerY + 115f, 200f, 45f), "Back"))
            {
                currentState = MenuState.MainMenu;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.HostingWaitingForPlayer)
        {
            DrawPanelBox(new Rect(centerX - 280f, centerY - 210f, 560f, 420f), new Color(0.1f, 0.14f, 0.22f, 0.94f));
            GUI.Label(new Rect(centerX - 260f, centerY - 185f, 520f, 45f), "HOSTING LOBBY", titleStyle);
            GUI.Label(new Rect(centerX - 260f, centerY - 130f, 520f, 35f), "Share this Join Code with the other player:", subStyle);

            // Big Join Code Box
            GUIStyle codeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            codeStyle.normal.textColor = new Color(0.3f, 1.0f, 0.45f);
            GUI.Box(new Rect(centerX - 140f, centerY - 85f, 280f, 70f), hostJoinCode, codeStyle);

            GUI.backgroundColor = new Color(0.4f, 0.85f, 1.0f);
            if (GUI.Button(new Rect(centerX - 100f, centerY - 5f, 200f, 40f), "Copy Code"))
            {
                GUIUtility.systemCopyBuffer = hostJoinCode;
                statusMessage = "Code copied to clipboard!";
            }

            GUI.Label(new Rect(centerX - 260f, centerY + 45f, 520f, 35f), statusMessage, subStyle);

            // Convenient helper so you can also test in 1 window immediately without opening a 2nd build
            GUI.backgroundColor = new Color(0.35f, 0.95f, 0.5f);
            if (GUI.Button(new Rect(centerX - 180f, centerY + 90f, 360f, 48f), "Simulate Player 2 Joined (Test Now)"))
            {
                BeginTransportToGameplayWall();
            }

            GUI.backgroundColor = new Color(0.8f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(centerX - 100f, centerY + 150f, 200f, 40f), "Cancel"))
            {
                StopNetwork();
                currentState = MenuState.HostOrJoinSelect;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.JoiningEnterCode)
        {
            DrawPanelBox(new Rect(centerX - 280f, centerY - 200f, 560f, 400f), new Color(0.1f, 0.14f, 0.22f, 0.94f));
            GUI.Label(new Rect(centerX - 260f, centerY - 175f, 520f, 45f), "JOIN GAME", titleStyle);
            GUI.Label(new Rect(centerX - 260f, centerY - 120f, 520f, 35f), statusMessage, subStyle);

            GUIStyle inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            enteredJoinCode = GUI.TextField(new Rect(centerX - 150f, centerY - 70f, 300f, 60f), enteredJoinCode, 24, inputStyle);

            GUI.backgroundColor = new Color(0.35f, 0.95f, 0.5f);
            if (GUI.Button(new Rect(centerX - 150f, centerY + 10f, 300f, 65f), "CONNECT TO HOST", bigBtn))
            {
                ConnectAsJoiner();
            }

            GUI.backgroundColor = new Color(0.75f, 0.75f, 0.75f);
            if (GUI.Button(new Rect(centerX - 100f, centerY + 110f, 200f, 42f), "Back"))
            {
                StopNetwork();
                currentState = MenuState.HostOrJoinSelect;
            }
            GUI.backgroundColor = Color.white;
        }
        else if (currentState == MenuState.RoleSelection)
        {
            DrawPanelBox(new Rect(centerX - 380f, centerY - 220f, 760f, 440f), new Color(0.08f, 0.1f, 0.14f, 0.92f));
            GUI.Label(new Rect(centerX - 350f, centerY - 195f, 700f, 45f), "CHOOSE YOUR SIDE", titleStyle);

            string prompt = isHost
                ? "HOST: Click a box below! The remaining option will be given to the other player."
                : "Waiting for the HOST to choose a box... You will receive the remaining side!";
            GUI.Label(new Rect(centerX - 350f, centerY - 140f, 700f, 35f), prompt, subStyle);

            // TWO BOXES: ONE PLANT AND ONE ZOMBIE
            Rect plantBoxRect = new Rect(centerX - 330f, centerY - 80f, 300f, 240f);
            Rect zombieBoxRect = new Rect(centerX + 30f, centerY - 80f, 300f, 240f);

            GUI.backgroundColor = new Color(0.22f, 0.85f, 0.35f);
            if (GUI.Button(plantBoxRect, "PLANT PLAYER\n\n(Defend the 5x8 Wall\n& Place Plants)", bigBtn))
            {
                if (isHost)
                {
                    HostChooseRole(PlayerRole.Plant);
                }
            }

            GUI.backgroundColor = new Color(0.75f, 0.25f, 0.85f);
            if (GUI.Button(zombieBoxRect, "ZOMBIE PLAYER\n\n(Attack the Wall\n& Command Zombies)", bigBtn))
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
            PlayerRole otherRole = isHost ? joinerRole : hostRole;
            string roleBanner = $"YOU: {localRole.ToString().ToUpper()} PLAYER   |   OTHER PLAYER: {otherRole.ToString().ToUpper()} PLAYER";

            DrawPanelBox(new Rect(sw - 540f, 20f, 520f, 54f), new Color(0.1f, 0.14f, 0.2f, 0.88f));
            GUI.Label(new Rect(sw - 530f, 25f, 500f, 44f), roleBanner, subStyle);

            if (GUI.Button(new Rect(sw - 160f, 82f, 140f, 34f), "Switch Role (Test)"))
            {
                localRole = (localRole == PlayerRole.Plant) ? PlayerRole.Zombie : PlayerRole.Plant;
                if (gameplayWallGrid != null)
                {
                    gameplayWallGrid.canPlacePlants = (localRole == PlayerRole.Plant);
                }
            }
        }
    }

    private void DrawPanelBox(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.Box(rect, GUIContent.none);
        GUI.color = prev;
    }
}
