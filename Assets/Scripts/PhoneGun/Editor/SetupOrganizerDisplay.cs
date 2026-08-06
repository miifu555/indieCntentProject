using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// 主催者用のQR・URL案内パネルを、2台目のモニター(Display 2)専用のCanvasとして
// シーンに一度だけ構築するためのエディタ用セットアップ。
// Unityメニューの [PhoneGun > 主催者用ディスプレイをセットアップ] から実行する。
public static class SetupOrganizerDisplay
{
    [MenuItem("PhoneGun/主催者用ディスプレイをセットアップ (Display 2)")]
    public static void Run()
    {
        GameObject managerGo = GameObject.Find("PhoneGunManager");
        if (managerGo == null)
        {
            Debug.LogError("[SetupOrganizerDisplay] PhoneGunManagerが見つかりません");
            return;
        }
        PhoneGunManager manager = managerGo.GetComponent<PhoneGunManager>();

        // --- Display有効化コンポーネントをPhoneGunServerに付与 ---
        GameObject serverGo = GameObject.Find("PhoneGunServer");
        if (serverGo != null && serverGo.GetComponent<MultiDisplayActivator>() == null)
        {
            serverGo.AddComponent<MultiDisplayActivator>();
        }

        // --- 既存のOrganizerCanvasがあれば作り直す ---
        GameObject existing = GameObject.Find("OrganizerCanvas");
        if (existing != null) Object.DestroyImmediate(existing);

        GameObject canvasGo = new GameObject("OrganizerCanvas", typeof(RectTransform));
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = 1; // Display 2（0始まり）

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // --- 背景 ---
        GameObject bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(canvasGo.transform, false);
        RectTransform bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.024f, 0.04f, 1f);

        // --- タイトル ---
        GameObject titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(canvasGo.transform, false);
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0, 380);
        titleRt.sizeDelta = new Vector2(1200, 80);
        TMP_Text titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "手裏剣に参加する";
        titleText.fontSize = 48;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(0.6f, 0.64f, 0.7f, 1f);

        // --- QRコード白背景パネル ---
        GameObject qrPanelGo = new GameObject("QrPanel", typeof(RectTransform));
        qrPanelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform qrPanelRt = qrPanelGo.GetComponent<RectTransform>();
        qrPanelRt.anchorMin = qrPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
        qrPanelRt.pivot = new Vector2(0.5f, 0.5f);
        qrPanelRt.anchoredPosition = new Vector2(0, 40);
        qrPanelRt.sizeDelta = new Vector2(480, 480);
        Image qrPanelImg = qrPanelGo.AddComponent<Image>();
        qrPanelImg.color = Color.white;

        // --- QRコード本体 ---
        GameObject qrImgGo = new GameObject("OrganizerQrImage", typeof(RectTransform));
        qrImgGo.transform.SetParent(qrPanelGo.transform, false);
        RectTransform qrImgRt = qrImgGo.GetComponent<RectTransform>();
        qrImgRt.anchorMin = Vector2.zero;
        qrImgRt.anchorMax = Vector2.one;
        qrImgRt.offsetMin = new Vector2(24, 24);
        qrImgRt.offsetMax = new Vector2(-24, -24);
        RawImage qrRawImage = qrImgGo.AddComponent<RawImage>();

        // --- 接続先URLテキスト ---
        GameObject urlGo = new GameObject("OrganizerUrlText", typeof(RectTransform));
        urlGo.transform.SetParent(canvasGo.transform, false);
        RectTransform urlRt = urlGo.GetComponent<RectTransform>();
        urlRt.anchorMin = urlRt.anchorMax = new Vector2(0.5f, 0.5f);
        urlRt.pivot = new Vector2(0.5f, 0.5f);
        urlRt.anchoredPosition = new Vector2(0, -260);
        urlRt.sizeDelta = new Vector2(1400, 80);
        TMP_Text urlText = urlGo.AddComponent<TextMeshProUGUI>();
        urlText.text = "サーバー起動中...";
        urlText.fontSize = 36;
        urlText.fontStyle = FontStyles.Bold;
        urlText.alignment = TextAlignmentOptions.Center;
        urlText.color = Color.white;
        urlText.enableWordWrapping = true;

        // --- PhoneGunManagerへ配線 ---
        manager.qrCodeImage = qrRawImage;
        manager.connectionInfoText = urlText;

        EditorUtility.SetDirty(managerGo);
        if (serverGo != null) EditorUtility.SetDirty(serverGo);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Debug.Log("[SetupOrganizerDisplay] OrganizerCanvas(Display 2)を作成し、PhoneGunManagerに配線しました。");
    }
}
