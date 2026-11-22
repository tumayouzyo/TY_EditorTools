// Execute this snippet with isuzu-unity-cli call execute_code --file <this file>.
// Works against either the package or the standalone Assets copy.
var type = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("TY.EditorTools.AnimatorOrphanTransitionRepair") ?? a.GetType("AnimatorOrphanTransitionRepair"))
    .First(t => t != null);
var process = type.GetMethod("Process");
var replace = type.GetMethod("ReplaceFileAtomically", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var folder = "Assets/__TYRepairRegression_" + Guid.NewGuid().ToString("N");
var diskFolder = System.IO.Path.Combine("Library", "TYRepairRegression_" + Guid.NewGuid().ToString("N"));
var warnings = new System.Collections.Generic.List<string>();
Application.LogCallback capture = (message, stack, level) => { if (level == LogType.Warning) warnings.Add(message); };
Application.logMessageReceived += capture;
try
{
    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
    var path = folder + "/Test.controller";
    var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
    var machine = controller.layers[0].stateMachine;
    var source = machine.AddState("Source");
    var destination = machine.AddState("Destination");
    source.AddTransition(destination);
    AssetDatabase.SaveAssets();
    string guid;
    long destinationId;
    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(destination, out guid, out destinationId);
    const string missingId = "-9876543210999";
    var text = System.IO.File.ReadAllText(path).Replace("m_DstState: {fileID: " + destinationId + "}", "m_DstState: {fileID: " + missingId + "}");
    var orphan = "--- !u!1109 &-9876543210123\nAnimatorTransition:\n  m_ObjectHideFlags: 1\n  m_Name: \n  m_Conditions: []\n  m_DstStateMachine: {fileID: 0}\n  m_DstState: {fileID: " + missingId + "}\n  m_Solo: 0\n  m_Mute: 0\n  m_IsExit: 0\n  serializedVersion: 1\n";
    System.IO.File.WriteAllText(path, text + orphan, new System.Text.UTF8Encoding(false));
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    var before = System.IO.File.ReadAllBytes(path);
    var scan = (string)process.Invoke(null, new object[] { path, false });
    if (!scan.Contains("1 orphan transition(s), 1 unresolved transition(s)") || !before.SequenceEqual(System.IO.File.ReadAllBytes(path)))
        throw new Exception("Scan classification or read-only behavior failed: " + scan);
    var repair = (string)process.Invoke(null, new object[] { path, true });
    if (System.IO.File.ReadAllText(path) != text) throw new Exception("Repair modified data other than the orphan block.");
    var repeat = (string)process.Invoke(null, new object[] { path, true });
    if (!repeat.Contains("0 orphan transition(s), 1 unresolved transition(s)")) throw new Exception("Unresolved issue was hidden.");
    if (!warnings.Any(w => w.Contains(missingId) && w.Contains("Manual repair required"))) throw new Exception("Warning missing.");

    System.IO.Directory.CreateDirectory(diskFolder);
    var diskPath = System.IO.Path.Combine(diskFolder, "atomic.txt");
    var original = System.Text.Encoding.UTF8.GetBytes("original");
    var replacement = System.Text.Encoding.UTF8.GetBytes("replacement");
    System.IO.File.WriteAllBytes(diskPath, original);
    Action<byte[], bool> attempt = (expected, mustFail) =>
    {
        bool failed = false;
        try { replace.Invoke(null, new object[] { diskPath, expected, replacement }); }
        catch (System.Reflection.TargetInvocationException ex)
        {
            if (!(ex.InnerException is System.IO.IOException)) throw;
            failed = true;
        }
        if (failed != mustFail) throw new Exception("Unexpected atomic replacement result.");
        if (System.IO.Directory.GetFiles(diskFolder).Length != 1) throw new Exception("Temporary file was not cleaned up.");
    };
    attempt(replacement, true);
    if (!System.IO.File.ReadAllBytes(diskPath).SequenceEqual(original)) throw new Exception("Concurrent-change guard lost data.");
    // Windows: holding the destination without delete sharing forces File.Replace to fail.
    if (Application.platform == RuntimePlatform.WindowsEditor)
    {
        using (var locked = new System.IO.FileStream(diskPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            attempt(original, true);
        if (!System.IO.File.ReadAllBytes(diskPath).SequenceEqual(original)) throw new Exception("Replacement failure lost data.");
    }
    attempt(original, false);
    if (!System.IO.File.ReadAllBytes(diskPath).SequenceEqual(replacement)) throw new Exception("Atomic replacement failed.");
    return new { scan, repair, repeat, unresolvedWarningsPassed = true, unrelatedBlocksPreserved = true,
        concurrentChangeGuardPassed = true, lockedFileFailurePassed = Application.platform == RuntimePlatform.WindowsEditor,
        atomicReplacePassed = true, temporaryFilesCleaned = true };
}
finally
{
    Application.logMessageReceived -= capture;
    AssetDatabase.DeleteAsset(folder);
    var diskPath = System.IO.Path.Combine(diskFolder, "atomic.txt");
    if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath);
    if (System.IO.Directory.Exists(diskFolder)) System.IO.Directory.Delete(diskFolder);
}
