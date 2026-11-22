using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TY.EditorTools
{
    public static class AnimatorEmptyMotionFiller
    {
        private const string Menu = "TY_EditorTools/Fill Empty Motions with proxy_empty";
        private const string AvatarRoot = "Packages/com.vrchat.avatars";
        private const string WorldRoot = "Packages/com.vrchat.worlds";

        [MenuItem("Assets/" + Menu, false, 2001)]
        [MenuItem("Tools/" + Menu, false, 2001)]
        private static void FillSelected()
        {
            var paths = SelectedPaths();
            if (!TryFindProxyEmpty(out var clip)) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fill Empty Animator Motions");
            int completed = 0, failed = 0, changed = 0;
            try
            {
                foreach (var path in paths)
                {
                    try
                    {
                        int count = FillController(path, clip);
                        changed += count;
                        completed++;
                        Debug.Log("Empty motion filler: " + path + ": assigned proxy_empty to " + count + " state(s).");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Debug.LogError("Empty motion filler: " + path + ": " + ex.Message);
                    }
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
            Debug.Log("Empty motion filler: " + completed + " completed, " + failed + " failed, " + changed + " state(s) updated.");
        }

        [MenuItem("Assets/" + Menu, true)]
        [MenuItem("Tools/" + Menu, true)]
        private static bool CanFill() { return SelectedPaths().Length > 0; }

        private static string[] SelectedPaths()
        {
            return Selection.objects.OfType<AnimatorController>().Select(AssetDatabase.GetAssetPath)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        public static bool TryFindProxyEmpty(out AnimationClip clip)
        {
            clip = null;
            // Asset lookup only: no VRChat assemblies or package dependencies required.
            if (!AssetDatabase.IsValidFolder(AvatarRoot))
            {
                Debug.LogWarning(AssetDatabase.IsValidFolder(WorldRoot)
                    ? "Empty motion filler: only the VRChat Worlds SDK is installed. proxy_empty requires the Avatars SDK. No changes made."
                    : "Empty motion filler: VRChat Avatars SDK not found. No changes made.");
                return false;
            }
            var matches = AssetDatabase.FindAssets("proxy_empty t:AnimationClip", new[] { AvatarRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == "proxy_empty").ToArray();
            if (matches.Length != 1)
            {
                Debug.LogWarning("Empty motion filler: expected one proxy_empty clip in the Avatars SDK, found " + matches.Length + ". No changes made.");
                return false;
            }
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(matches[0]);
            if (clip != null) return true;
            Debug.LogWarning("Empty motion filler: could not load proxy_empty. No changes made.");
            return false;
        }

        public static int FillController(string path, AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Select an Animator Controller under Assets.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) throw new InvalidOperationException("Could not load Animator Controller.");
            if (AssetDatabase.LoadAllAssetsAtPath(path).Any(o => o != null && EditorUtility.IsDirty(o)))
                throw new InvalidOperationException("Controller has unsaved edits. Save it before filling motions.");
            var layers = controller.layers;
            var targets = new List<Tuple<AnimatorState, int>>();
            for (int i = 0; i < layers.Length; i++)
            {
                int source = i;
                var indices = new HashSet<int>();
                while (layers[source].syncedLayerIndex >= 0)
                {
                    if (!indices.Add(source)) throw new InvalidOperationException("Cyclic synced layers.");
                    source = layers[source].syncedLayerIndex;
                    if (source >= layers.Length) throw new InvalidOperationException("Invalid synced layer index.");
                }
                var visited = new HashSet<AnimatorStateMachine>();
                Action<AnimatorStateMachine> walk = null;
                walk = machine =>
                {
                    if (machine == null || !visited.Add(machine)) return;
                    foreach (var child in machine.states)
                        if (child.state != null && controller.GetStateEffectiveMotion(child.state, i) == null)
                            targets.Add(Tuple.Create(child.state, i));
                    foreach (var child in machine.stateMachines) walk(child.stateMachine);
                };
                walk(layers[source].stateMachine);
            }
            if (targets.Count == 0) return 0;
            var backupDir = Path.Combine("Library", "AnimatorEmptyMotionBackups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backupDir);
            var backup = Path.Combine(backupDir, Path.GetFileName(path) + "." + AssetDatabase.AssetPathToGUID(path) + ".bak");
            File.Copy(path, backup);
            Undo.RecordObject(controller, "Fill Empty Animator Motions");
            foreach (var target in targets)
            {
                Undo.RecordObject(target.Item1, "Fill Empty Animator Motions");
                controller.SetStateEffectiveMotion(target.Item1, clip, target.Item2);
                EditorUtility.SetDirty(target.Item1);
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log("Empty motion filler: backup saved to " + Path.GetFullPath(backup));
            return targets.Count;
        }
    }
}
