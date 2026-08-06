using UnityEngine;

// ビルドしたアプリで、接続されている2台目以降のモニターにもウィンドウを出すために
// 各Displayを有効化する。エディタ実行中は効果がない（Game Viewは1画面のみ）。
public class MultiDisplayActivator : MonoBehaviour
{
    void Awake()
    {
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
        }
    }
}
