using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Central teardown for the state that outlives a scene load.
//
// G4VR keeps a lot in statics -- the parsed run, the geometry/edep bookkeeping, the instancing buffers
// -- and parents the imported detector geometry under a DontDestroyOnLoad object called "Scene". None
// of that is touched by SceneManager.LoadScene, so without an explicit hook the previous run's step
// arrays and the previous detector's meshes and textures stayed resident for the rest of the session:
// opening a second custom experiment meant holding both at once.
//
// The hook fires on scene *unload*, and only for the analysis scenes. It deliberately does NOT fire for
// the main menu: ServerGET downloads and instantiates the GLB while the menu is still the active scene
// and only then calls LoadScene("Custom"), so tearing the geometry down on the menu's unload would
// destroy the model the user just waited for.
public static class SessionMemory
{
    private static readonly HashSet<string> AnalysisScenes = new HashSet<string>
    {
        "Custom", "exampleB3a", "exampleB4a", "exampleB5", "Test", "G4VR-DM"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // Unsubscribe first: statics survive a domain reload being disabled in the editor, so a second
        // Play session would otherwise stack a duplicate handler.
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        Application.lowMemory -= OnLowMemory;
        Application.lowMemory += OnLowMemory;
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!AnalysisScenes.Contains(scene.name))
            return;

        Debug.Log($"[SESSION-MEMORY] Leaving '{scene.name}'; releasing run data and detector geometry.");
        ReleaseEverything();
    }

    // On Quest this is the last warning before the OS kills the app, so take whatever is free.
    private static void OnLowMemory()
    {
        Debug.LogWarning("[SESSION-MEMORY] Low-memory warning from the OS; unloading unused assets.");
        Unload();
    }

    // Tears down the run, the detector geometry, and every static that references either.
    public static void ReleaseEverything()
    {
        NewBehaviourScript.ResetStaticState();
        TrackAnalyser.ResetStaticState();
        InstancedGeometryDrawer.ResetStaticState();
        DestroyGeometryChildren();

        // Deferred: the geometry above is not actually gone until the end of this frame.
        UnloadNextFrame();
    }

    // Empties the persistent "Scene" container without destroying it: GameManager creates it once,
    // marks it DontDestroyOnLoad, and every later GLB import parents into it.
    public static void DestroyGeometryChildren()
    {
        GameObject geometry = GameObject.Find("Scene");
        if (geometry == null)
            return;

        // Backwards, because Destroy detaches children as it goes -- the old forward-indexing loop in
        // ServerGET skipped every other child for exactly this reason.
        for (int i = geometry.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(geometry.transform.GetChild(i).gameObject);

        // The auto-fit scale is per-experiment; leaving it applied would mis-size the next one.
        geometry.transform.localScale = Vector3.one;
    }

    // Destroying GameObjects does not free the meshes and textures they referenced -- only an explicit
    // unload does. The GC passes on either side ensure the managed references are dead first, so the
    // unload actually sees those assets as unreferenced.
    //
    // Call this only when any pending Destroy() calls have already been processed; otherwise the
    // objects are still alive and nothing is reclaimed. Use UnloadNextFrame() if unsure.
    public static void Unload()
    {
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    // Unity defers Destroy() to the end of the frame, so an unload issued in the same frame would still
    // see the geometry as referenced and free nothing. Waiting a frame is what makes the reclaim real.
    public static void UnloadNextFrame()
    {
        Runner.Instance.Run(DeferredUnload());
    }

    private static IEnumerator DeferredUnload()
    {
        yield return null; // let the pending destroys be processed
        Unload();
    }

    // A minimal always-alive host, so the deferred unload survives the scene change that triggered it.
    private class Runner : MonoBehaviour
    {
        private static Runner instance;

        public static Runner Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject host = new GameObject("[SessionMemory]") { hideFlags = HideFlags.HideAndDontSave };
                    Object.DontDestroyOnLoad(host);
                    instance = host.AddComponent<Runner>();
                }

                return instance;
            }
        }

        public void Run(IEnumerator routine) => StartCoroutine(routine);
    }
}
