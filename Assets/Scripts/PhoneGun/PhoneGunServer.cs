using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

// スマホをQRコードで開かせ、ジャイロの傾きとタップをHTTPSで受け取るローカルサーバー。
// 自己署名証明書によるTLSを自前実装しているのは、HttpListenerでのHTTPS化にはOSへの
// 証明書バインド（netsh + 管理者権限）が必要で、参加者のイベント運用に向かないため。
// ブラウザの傾きセンサー系APIはセキュアコンテキスト（https）でないと使えない機種が多く、
// 平文httpのままだとAndroid Chrome等でジャイロが一切取得できない。
// PhoneGunManagerがメインスレッドから各Try系メソッドをポーリングして消費する。
public class PhoneGunServer : MonoBehaviour
{
    public static PhoneGunServer Instance { get; private set; }

    [Header("サーバー設定")]
    public int port = 8787;
    [Tooltip("StreamingAssets からの相対パス。スマホに配信するHTMLファイル")]
    public string controllerHtmlRelativePath = "PhoneGun/controller.html";
    [Tooltip("StreamingAssets からの相対パス。TLS用の自己署名証明書（PFX）")]
    public string certificateRelativePath = "PhoneGun/server.pfx";
    private const string CertificatePassword = "phonegun";
    [Tooltip("StreamingAssets からの相対パス。主催者がPCのブラウザで開く、QRコード+URL表示用ページ")]
    public string displayHtmlRelativePath = "PhoneGun/display.html";

    [Header("ngrok設定（インターネット越しの接続用）")]
    [Tooltip("有効にすると、同一Wi-Fiでなくてもインターネット経由でスマホから接続できるようにngrokトンネルを自動起動する")]
    public bool enableNgrok = false;
    [Tooltip("ngrok実行ファイルのパス。PATHが通っていれば'ngrok'のままでよい")]
    public string ngrokPath = "ngrok";
    [Tooltip("ngrokが転送してくる先のローカルポート（TLSポートとは別に、平文HTTPで待ち受ける）")]
    public int ngrokLocalPort = 8788;
    [Tooltip("ngrokのローカル管理API（トンネルURLの問い合わせ先）のポート")]
    public int ngrokApiPort = 4040;

    // 起動後に確定するスマホ接続用URL（QRコード化して使う）。自己署名証明書のため
    // スマホ側で「安全ではない接続」の警告を一度だけ許可してもらう必要がある。
    public string ServerUrl { get; private set; } = "";
    // ngrok経由の公開URL（https、ngrokが正規の証明書を提供するため警告は出ない）。
    // 確立するまでは空文字列。
    public string PublicUrl { get; private set; } = "";
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
        public float alpha;
        public float beta;
        public float gamma;
        public bool recenter;
    }

    [Serializable]
    private class AimPayload
    {
        public string id;
        public float alpha;
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

    private TcpListener tlsListener;
    private Thread tlsListenerThread;
    private TcpListener plainListener;
    private Thread plainListenerThread;
    private X509Certificate2 serverCertificate;
    private string htmlContent;
    private string displayHtmlContent;
    private volatile bool running;
    private int colorCounter;
    private Process ngrokProcess;

    // QRコードPNGはメインスレッド(Texture2D)でしか生成できないため、PhoneGunManagerが
    // URL確定時に生成してここへセットしたものを、バックグラウンドスレッドからは
    // 読み取り専用でそのまま返す。参照の差し替えだけなのでロック不要。
    public byte[] QrPngBytes { get; set; }

    // /display ページが参照する現在の接続先URL（ngrokがあればそちらを優先）
    public string DisplayUrl => !string.IsNullOrEmpty(PublicUrl) ? PublicUrl : ServerUrl;

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

        string displayPath = Path.Combine(Application.streamingAssetsPath, displayHtmlRelativePath);
        try
        {
            displayHtmlContent = File.ReadAllText(displayPath, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] display.htmlの読み込みに失敗しました: {displayPath}\n{e}");
            displayHtmlContent = "<html><body>display.html not found</body></html>";
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

    // UnityのMonoランタイムは実行時の証明書生成API（CertificateRequest等）に未対応のため、
    // PowerShellで事前に一度だけ生成したPFXファイルをStreamingAssetsから読み込む。
    // どうせ自己署名で「信頼されていない証明書」としてスマホ側に警告が出る前提のため、
    // 証明書のCN/SANが実際の接続先IPと一致していなくても、参加者が警告を許可すれば接続できる。
    X509Certificate2 LoadServerCertificate()
    {
        string path = Path.Combine(Application.streamingAssetsPath, certificateRelativePath);
        byte[] pfxBytes = File.ReadAllBytes(path);
        return new X509Certificate2(pfxBytes, CertificatePassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);
    }

    void StartServer()
    {
        string ip = GetLocalIPv4();
        ServerUrl = $"https://{ip}:{port}/";

        try
        {
            serverCertificate = LoadServerCertificate();
            tlsListener = new TcpListener(IPAddress.Any, port);
            tlsListener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] サーバー起動に失敗しました（{ServerUrl}）。ポート使用中か権限不足の可能性があります。\n{e}");
            IsRunning = false;
            return;
        }

        running = true;
        IsRunning = true;
        tlsListenerThread = new Thread(AcceptLoopTls) { IsBackground = true };
        tlsListenerThread.Start();

        Debug.Log($"[PhoneGunServer] サーバー起動: {ServerUrl}  このURLをQRコード化してスマホで読み込んでください（同一Wi-Fi内のみ）。自己署名証明書のため、スマホ初回アクセス時は「詳細設定→このまま進む」等で警告を許可してください。");

        if (enableNgrok)
        {
            StartPlainListenerForNgrok();
            StartNgrok();
        }
    }

    // ngrokは自分自身が正規のHTTPS証明書を提供してくれるため、ngrokからこのPCへの
    // 転送区間（ループバック内）は平文HTTPで十分。専用の別ポートで待ち受ける。
    void StartPlainListenerForNgrok()
    {
        try
        {
            plainListener = new TcpListener(IPAddress.Loopback, ngrokLocalPort);
            plainListener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] ngrok用の平文HTTPポート({ngrokLocalPort})の起動に失敗しました。\n{e}");
            return;
        }

        plainListenerThread = new Thread(AcceptLoopPlain) { IsBackground = true };
        plainListenerThread.Start();
    }

    void StartNgrok()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ngrokPath,
                Arguments = $"http {ngrokLocalPort} --log=stdout",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            ngrokProcess = Process.Start(psi);

            // ngrok側の出力を捨てずにUnityコンソールへ流す（トンネル失敗時の原因調査用）
            ngrokProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[ngrok] {e.Data}");
            };
            ngrokProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[ngrok:stderr] {e.Data}");
            };
            ngrokProcess.BeginOutputReadLine();
            ngrokProcess.BeginErrorReadLine();
            ngrokProcess.EnableRaisingEvents = true;
            ngrokProcess.Exited += (s, e) =>
            {
                Debug.LogWarning($"[PhoneGunServer] ngrokプロセスが終了しました（ExitCode={ngrokProcess.ExitCode}）。");
            };

            StartCoroutine(PollNgrokPublicUrl());
        }
        catch (Exception e)
        {
            Debug.LogError($"[PhoneGunServer] ngrokの起動に失敗しました。ngrokがインストールされ、PATHが通っているか確認してください。\n{e}");
        }
    }

    [Serializable] private class NgrokTunnel { public string public_url; public string proto; }
    [Serializable] private class NgrokTunnelsResponse { public NgrokTunnel[] tunnels; }

    IEnumerator PollNgrokPublicUrl()
    {
        string apiUrl = $"http://127.0.0.1:{ngrokApiPort}/api/tunnels";
        float timeout = 20f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            yield return new WaitForSeconds(1f);
            elapsed += 1f;

            string json = null;
            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    json = wc.DownloadString(apiUrl);
                }
            }
            catch
            {
                continue; // ngrokがまだAPIを起動していない等。次のポーリングへ
            }

            NgrokTunnelsResponse response = null;
            try { response = JsonUtility.FromJson<NgrokTunnelsResponse>(json); } catch { }

            if (response?.tunnels != null)
            {
                foreach (var t in response.tunnels)
                {
                    if (t.proto == "https" && !string.IsNullOrEmpty(t.public_url))
                    {
                        PublicUrl = t.public_url + "/";
                        Debug.Log($"[PhoneGunServer] ngrokトンネル確立: {PublicUrl}  このURLならインターネット経由でも接続できます。");
                        yield break;
                    }
                }
            }
        }

        Debug.LogWarning("[PhoneGunServer] ngrokの公開URL取得がタイムアウトしました。ngrokが正しく起動しているか、認証トークンが設定されているか確認してください。");
    }

    void AcceptLoopTls()
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = tlsListener.AcceptTcpClient();
            }
            catch
            {
                break; // Stop()されるとAcceptTcpClientが例外を投げてループを抜ける
            }

            var clientThread = new Thread(() => HandleTlsClient(client)) { IsBackground = true };
            clientThread.Start();
        }
    }

    void AcceptLoopPlain()
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = plainListener.AcceptTcpClient();
            }
            catch
            {
                break;
            }

            var clientThread = new Thread(() => HandlePlainClient(client)) { IsBackground = true };
            clientThread.Start();
        }
    }

    void HandleTlsClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var sslStream = new SslStream(client.GetStream(), false))
            {
                try
                {
                    sslStream.AuthenticateAsServer(serverCertificate, false, SslProtocols.Tls12, false);
                }
                catch (Exception)
                {
                    return; // TLSハンドシェイクに失敗した接続は静かに切る（暗号化されていない誤アクセス等）
                }

                ProcessHttp(sslStream);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PhoneGunServer] リクエスト処理中にエラー: {e.Message}");
        }
    }

    void HandlePlainClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                ProcessHttp(stream);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PhoneGunServer] リクエスト処理中にエラー(ngrok経由): {e.Message}");
        }
    }

    void ProcessHttp(Stream stream)
    {
        if (!TryReadHttpRequest(stream, out string method, out string path, out string body))
        {
            return;
        }

        // QRコードのPNGはテキストではなくバイナリなので、専用の応答経路で処理する
        if (method == "GET" && path == "/qr.png")
        {
            byte[] png = QrPngBytes;
            if (png == null)
            {
                WriteHttpResponse(stream, 404, "text/plain", "qr not ready");
            }
            else
            {
                WriteHttpResponseBytes(stream, 200, "image/png", png);
            }
            return;
        }

        Route(method, path, body, out int statusCode, out string contentType, out string responseBody);
        WriteHttpResponse(stream, statusCode, contentType, responseBody);
    }

    bool TryReadHttpRequest(Stream stream, out string method, out string path, out string body)
    {
        method = null;
        path = null;
        body = "";

        List<byte> headerBytes = ReadUntilHeaderEnd(stream);
        if (headerBytes == null || headerBytes.Count == 0) return false;

        string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0) return false;

        string[] requestLineParts = lines[0].Split(' ');
        if (requestLineParts.Length < 2) return false;
        method = requestLineParts[0];
        path = requestLineParts[1];

        int contentLength = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int sep = line.IndexOf(':');
            if (sep <= 0) continue;
            string key = line.Substring(0, sep).Trim();
            string value = line.Substring(sep + 1).Trim();
            if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out contentLength);
            }
        }

        if (contentLength > 0)
        {
            byte[] bodyBytes = new byte[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = stream.Read(bodyBytes, totalRead, contentLength - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            body = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
        }

        return true;
    }

    List<byte> ReadUntilHeaderEnd(Stream stream)
    {
        var buf = new List<byte>();
        var one = new byte[1];
        while (buf.Count < 16384) // 異常に長いヘッダは切り捨てる
        {
            int n = stream.Read(one, 0, 1);
            if (n <= 0) break;
            buf.Add(one[0]);
            int c = buf.Count;
            if (c >= 4 && buf[c - 4] == (byte)'\r' && buf[c - 3] == (byte)'\n' &&
                buf[c - 2] == (byte)'\r' && buf[c - 1] == (byte)'\n')
            {
                return buf;
            }
        }
        return buf.Count > 0 ? buf : null;
    }

    void WriteHttpResponse(Stream stream, int statusCode, string contentType, string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
        string statusText = statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : "Bad Request";

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    void WriteHttpResponseBytes(Stream stream, int statusCode, string contentType, byte[] bodyBytes)
    {
        string statusText = statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : "Bad Request";

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    void Route(string method, string path, string body, out int statusCode, out string contentType, out string responseBody)
    {
        if (method == "GET" && path == "/")
        {
            statusCode = 200;
            contentType = "text/html; charset=utf-8";
            responseBody = htmlContent;
        }
        else if (method == "GET" && path == "/display")
        {
            statusCode = 200;
            contentType = "text/html; charset=utf-8";
            responseBody = displayHtmlContent;
        }
        else if (method == "GET" && path == "/status")
        {
            statusCode = 200;
            contentType = "application/json";
            string url = DisplayUrl ?? "";
            responseBody = $"{{\"url\":\"{url}\",\"running\":{(IsRunning ? "true" : "false")}}}";
        }
        else if (method == "GET" && path == "/join")
        {
            statusCode = 200;
            contentType = "application/json";
            responseBody = HandleJoin();
        }
        else if (method == "POST" && path == "/aim")
        {
            responseBody = HandleAim(body, out statusCode);
            contentType = "application/json";
        }
        else if (method == "POST" && path == "/fire")
        {
            responseBody = HandleFire(body, out statusCode);
            contentType = "application/json";
        }
        else
        {
            statusCode = 404;
            contentType = "text/plain";
            responseBody = "not found";
        }
    }

    string HandleJoin()
    {
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        int idx = Interlocked.Increment(ref colorCounter) - 1;
        string color = PlayerColors[idx % PlayerColors.Length];

        var info = new PlayerInfo { id = id, colorHex = color, score = 0 };
        players[id] = info;
        joinQueue.Enqueue(id);

        return $"{{\"id\":\"{id}\",\"color\":\"{color}\"}}";
    }

    string HandleAim(string body, out int statusCode)
    {
        AimPayload payload = null;
        try { payload = JsonUtility.FromJson<AimPayload>(body); } catch { }

        if (payload == null || string.IsNullOrEmpty(payload.id) || !players.ContainsKey(payload.id))
        {
            statusCode = 400;
            return "{\"ok\":false}";
        }

        latestAim[payload.id] = new AimData { alpha = payload.alpha, beta = payload.beta, gamma = payload.gamma, recenter = payload.recenter };
        int score = players[payload.id].score;
        statusCode = 200;
        return $"{{\"ok\":true,\"score\":{score}}}";
    }

    string HandleFire(string body, out int statusCode)
    {
        FirePayload payload = null;
        try { payload = JsonUtility.FromJson<FirePayload>(body); } catch { }

        if (payload == null || string.IsNullOrEmpty(payload.id) || !players.ContainsKey(payload.id))
        {
            statusCode = 400;
            return "{\"ok\":false}";
        }

        fireQueue.Enqueue(payload.id);
        statusCode = 200;
        return "{\"ok\":true}";
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
            tlsListener?.Stop();
        }
        catch { }
        try
        {
            plainListener?.Stop();
        }
        catch { }
        try
        {
            serverCertificate?.Dispose();
        }
        catch { }
        try
        {
            if (ngrokProcess != null && !ngrokProcess.HasExited)
            {
                ngrokProcess.Kill();
            }
            ngrokProcess?.Dispose();
        }
        catch { }
        IsRunning = false;
    }

    void OnApplicationQuit()
    {
        OnDestroy();
    }
}
