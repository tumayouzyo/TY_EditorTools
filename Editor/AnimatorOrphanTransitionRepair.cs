using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Repairs only unreferenced transition sub-assets with missing local destinations.
// Does not guess replacement states or remove live transitions.
namespace TY.EditorTools
{
public static class AnimatorOrphanTransitionRepair
{
    private static readonly Regex Header = new Regex(@"^--- !u!(\d+) &(-?\d+)[^\r\n]*\r?\n", RegexOptions.Multiline);
    private static readonly Regex Reference = new Regex(@"\{fileID:\s*(-?\d+)([^}]*)\}");
    private static readonly Regex Destination = new Regex(@"^  m_DstState(?:Machine)?: \{fileID: (-?\d+)\}\r?$", RegexOptions.Multiline);

    [MenuItem("Tools/TY_EditorTools/Animator参照切れを検査・修復", false, 2000)]
    [MenuItem("Assets/TY_EditorTools/Animator参照切れを検査・修復", false, 2000)]
    private static void Repair() { RunSelected(true); }

    [MenuItem("Tools/TY_EditorTools/Animator参照切れを検査・修復", true)]
    [MenuItem("Assets/TY_EditorTools/Animator参照切れを検査・修復", true)]
    private static bool CanRunSelected()
    {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return Selection.objects.Length == 1 &&
               Selection.activeObject is UnityEditor.Animations.AnimatorController &&
               path.StartsWith("Assets/", StringComparison.Ordinal) &&
               path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunSelected(bool repair)
    {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        try { Debug.Log(Process(path, repair)); }
        catch (Exception ex) { Debug.LogError("Animator repair: " + ex.Message); }
    }

    public static string Process(string path, bool repair)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select an Animator Controller under Assets.");
        var original = File.ReadAllBytes(path);
        var text = new UTF8Encoding(false, true).GetString(original);
        if (!text.TrimStart('\uFEFF').StartsWith("%YAML", StringComparison.Ordinal))
            throw new InvalidOperationException("Only text-serialized controllers are supported.");
        var headers = Header.Matches(text).Cast<Match>().ToArray();
        if (headers.Length == 0) throw new InvalidOperationException("No YAML objects found.");
        var ids = new HashSet<string>(headers.Select(h => h.Groups[2].Value));
        if (ids.Count != headers.Length) throw new InvalidOperationException("Duplicate object IDs; repair refused.");
        var references = Reference.Matches(text).Cast<Match>()
            .Where(m => !m.Groups[2].Value.Contains("guid:"))
            .Select(m => m.Groups[1].Value).ToLookup(id => id);
        var remove = new List<Tuple<int, int>>();
        var removedIds = new List<string>();
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i];
            if (h.Groups[1].Value != "1101" && h.Groups[1].Value != "1109") continue;
            var end = i + 1 < headers.Length ? headers[i + 1].Index : text.Length;
            var block = text.Substring(h.Index, end - h.Index);
            var missing = Destination.Matches(block).Cast<Match>()
                .Any(m => m.Groups[1].Value != "0" && !ids.Contains(m.Groups[1].Value));
            if (!missing || references.Contains(h.Groups[2].Value)) continue;
            remove.Add(Tuple.Create(h.Index, end - h.Index));
            removedIds.Add(h.Groups[2].Value);
        }
        var summary = path + ": " + remove.Count + " orphan transition(s)";
        if (!repair || remove.Count == 0) return summary + (repair ? "; no changes." : "; scan only.");
        // Refuse to overwrite unsaved Inspector edits.
        if (AssetDatabase.LoadAllAssetsAtPath(path).Any(o => o != null && EditorUtility.IsDirty(o)))
            throw new InvalidOperationException("Controller has unsaved edits. Save it before repairing.");
        var backupDir = Path.Combine("Library", "AnimatorRepairBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, Path.GetFileName(path) + ".bak");
        File.WriteAllBytes(backup, original);
        foreach (var range in remove.OrderByDescending(r => r.Item1)) text = text.Remove(range.Item1, range.Item2);
        if (!File.ReadAllBytes(path).SequenceEqual(original))
            throw new IOException("Controller changed during inspection; repair refused.");
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return summary + "; removed IDs: " + string.Join(", ", removedIds) + "; backup: " + Path.GetFullPath(backup);
    }
}
}
