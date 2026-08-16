using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// 歴代ハイスコアをJSONファイルとして永続化する（アプリを再起動しても残る）
public class HighScoreManager : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string name;
        public int score;
    }

    [Serializable]
    private class EntryList
    {
        public List<Entry> entries = new List<Entry>();
    }

    [Tooltip("保持する件数")]
    public int maxEntries = 10;

    private EntryList data = new EntryList();
    private string FilePath => Path.Combine(Application.persistentDataPath, "phonegun_highscores.json");

    void Awake()
    {
        Load();
    }

    public List<Entry> GetTop(int count)
    {
        var list = new List<Entry>(data.entries);
        list.Sort((a, b) => b.score.CompareTo(a.score));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        return list;
    }

    // このスコアが殿堂入りできるか（枠が空いている、または最下位より上）
    public bool WouldQualify(int score)
    {
        if (score <= 0) return false;
        if (data.entries.Count < maxEntries) return true;

        int lowest = int.MaxValue;
        foreach (var e in data.entries)
        {
            if (e.score < lowest) lowest = e.score;
        }
        return score > lowest;
    }

    // 主催者用。歴代ハイスコアを全消去する
    public void ClearAll()
    {
        data.entries.Clear();
        Save();
    }

    public void AddEntry(string name, int score)
    {
        data.entries.Add(new Entry { name = name, score = score });
        data.entries.Sort((a, b) => b.score.CompareTo(a.score));
        if (data.entries.Count > maxEntries)
        {
            data.entries.RemoveRange(maxEntries, data.entries.Count - maxEntries);
        }
        Save();
    }

    public string BuildRankingText()
    {
        var top = GetTop(maxEntries);
        if (top.Count == 0) return "まだ記録がありません";

        var sb = new StringBuilder();
        for (int i = 0; i < top.Count; i++)
        {
            sb.AppendLine(RankingColor.FormatLine(i + 1, top[i].name, top[i].score));
        }
        return sb.ToString();
    }

    void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                data = JsonUtility.FromJson<EntryList>(json) ?? new EntryList();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HighScoreManager] 読み込みに失敗しました: {e.Message}");
            data = new EntryList();
        }
    }

    void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(data));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HighScoreManager] 保存に失敗しました: {e.Message}");
        }
    }
}
