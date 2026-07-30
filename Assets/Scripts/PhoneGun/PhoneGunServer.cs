using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// スマホをQRコードで開かせ、ジャイロの傾きとタップをHTTPで受け取るローカルサーバー。
// PhoneGunManagerがメインスレッドから各Try系メソッドをポーリングして消費する。
public class PhoneGunServer : MonoBehaviour
{
    public static PhoneGunServer Instance { get; private set; }

    [Header("サーバー設定")]
    public int port = 8787;
    [Tooltip("StreamingAssets からの相対パス。スマホに配信するHTMLファイル")]
    public string controllerHtmlRelativePath = "PhoneGun/controller.html";

    // 起動後に確定するスマホ接続用URL（QRコード化して使う）
    public string ServerUrl { get; private set; } = "";
    public bool IsRunning { get; private set; }

    [Serializable]
    public class PlayerInfo
    {
        public string id;
        public string colorHex;
        public int score;
    }

    public struct AimData
    {
        public float beta;
        public float gamma;
        public bool recenter;
    }

    [Serializable]
    private class AimPayload
    {
        public string id;
        public float beta;
        public float gamma;
        public bool recenter;
    }

    [Serializable]
    private class FirePayload
    {
        public string id;
    }

    private static readonly string[] PlayerColors =
    {
        "#ff4757", "#2ed573", "#1e90ff", "#ffa502",
        "#a55eea", "#00d2d3", "#ff6b81", "#eccc68",
    };

    private readonly ConcurrentDictionary<string, PlayerInfo> players = new ConcurrentDictionary<string, PlayerInfo>();
    private readonly ConcurrentDictionary<string, AimData> latestAim = new ConcurrentDictionary<string, AimData>();
    private readonly ConcurrentQueue<string> fireQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> joinQueue = new ConcurrentQueue<string>();

    private HttpListener listener;
    private Thread listenerThread;
    private string htmlContent;
    private volatile bool running;
    private int colorCounter;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        LoadHtml();
        StartServer();
    }

    void LoadHtml()
    {
        string path = Path.Combine(Application.streamingAssetsPath, controllerHtmlRelativePath);
        try
        {
            htmlContent = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] controller.htmlの読み込みに失敗しました: {path}\n{e}");
            htmlContent = "<html><body>controller.html not found</body></html>";
        }
    }

    // ルーター無しで直接同一Wi-Fi内に居るスマホから届く自分のLAN IPv4を推定する
    string GetLocalIPv4()
    {
        try
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint != null ? endPoint.Address.ToString() : "127.0.0.1";
            }
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    void StartServer()
    {
        string ip = GetLocalIPv4();
        ServerUrl = $"http://{ip}:{port}/";

        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(ServerUrl);
            listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] サーバー起動に失敗しました（{ServerUrl}）。ポート使用中か権限不足の可能性があります。\n{e}");
            IsRunning = false;
            return;
        }

        running = true;
        IsRunning = true;
        listenerThread = new Thread(ListenLoop) { IsBackground = true };
        listenerThread.Start();

        Debug.Log($"[PhoneGunServer] サーバー起動: {ServerUrl}  このURLをQRコード化してスマホで読み込んでください（同一Wi-Fi内のみ）。");
    }

    void ListenLoop()
    {
        while (running)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = listener.GetContext();
            }
            catch
            {
                break; // Stop()されるとGetContextが例外を投げてループを抜ける
            }

            try
            {
                HandleRequest(ctx);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PhoneGunServer] リクエスト処理中にエラー: {e}");
            }
        }
    }

    void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers.Add("Access-Control-Allow-Origin", "*");

        string path = req.Url.AbsolutePath;

        if (req.HttpMethod == "GET" && path == "/")
        {
            WriteText(res, htmlContent, "text/html; charset=utf-8");
        }
        else if (req.HttpMethod == "GET" && path == "/join")
        {
            HandleJoin(res);
        }
        else if (req.HttpMethod == "POST" && path == "/aim")
        {
            HandleAim(req, res);
        }
        else if (req.HttpMethod == "POST" && path == "/fire")
        {
            HandleFire(req, res);
        }
        else
        {
            res.StatusCode = 404;
            WriteText(res, "not found", "text/plain");
        }
    }

    void HandleJoin(HttpListenerResponse res)
    {
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        int idx = Interlocked.Increment(ref colorCounter) - 1;
        string color = PlayerColors[idx % PlayerColors.Length];

        var info = new PlayerInfo { id = id, colorHex = color, score = 0 };
        players[id] = info;
        joinQueue.Enqueue(id);

        string json = $"{{\"id\":\"{id}\",\"color\":\"{color}\"}}";
        WriteText(res, json, "application/json");
    }

    void HandleAim(HttpListenerRequest req, HttpListenerResponse res)
    {
        string body = ReadBody(req);
        AimPayload payload = null;
        try { payload = JsonUtility.FromJson<AimPayload>(body); } catch { }

        if (payload == null || string.IsNullOrEmpty(payload.id) || !players.ContainsKey(payload.id))
        {
            res.StatusCode = 400;
            WriteText(res, "{\"ok\":false}", "application/json");
            return;
        }

        latestAim[payload.id] = new AimData { beta = payload.beta, gamma = payload.gamma, recenter = payload.recenter };
        int score = players[payload.id].score;
        WriteText(res, $"{{\"ok\":true,\"score\":{score}}}", "application/json");
    }

    void HandleFire(HttpListenerRequest req, HttpListenerResponse res)
    {
        string body = ReadBody(req);
        FirePayload payload = null;
        try { payload = JsonUtility.FromJson<FirePayload>(body); } catch { }

        if (payload == null || string.IsNullOrEmpty(payload.id) || !players.ContainsKey(payload.id))
        {
            res.StatusCode = 400;
            WriteText(res, "{\"ok\":false}", "application/json");
            return;
        }

        fireQueue.Enqueue(payload.id);
        WriteText(res, "{\"ok\":true}", "application/json");
    }

    string ReadBody(HttpListenerRequest req)
    {
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
        {
            return reader.ReadToEnd();
        }
    }

    void WriteText(HttpListenerResponse res, string text, string contentType)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        res.ContentType = contentType;
        res.ContentLength64 = buffer.Length;
        try
        {
            res.OutputStream.Write(buffer, 0, buffer.Length);
        }
        finally
        {
            res.OutputStream.Close();
        }
    }

    // --- メインスレッド（PhoneGunManager）からのポーリング用API ---

    public bool TryDequeueJoin(out string id) => joinQueue.TryDequeue(out id);

    public bool TryDequeueFire(out string id) => fireQueue.TryDequeue(out id);

    public bool TryGetAim(string id, out AimData aim) => latestAim.TryGetValue(id, out aim);

    public bool TryGetPlayer(string id, out PlayerInfo info) => players.TryGetValue(id, out info);

    public void AddScore(string id, int amount)
    {
        if (players.TryGetValue(id, out var info))
        {
            info.score += amount;
        }
    }

    public IEnumerable<string> ConnectedPlayerIds => players.Keys;

    void OnDestroy()
    {
        running = false;
        try
        {
            listener?.Stop();
            listener?.Close();
        }
        catch { }
        IsRunning = false;
    }

    void OnApplicationQuit()
    {
        OnDestroy();
    }
}
