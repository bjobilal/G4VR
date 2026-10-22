using GLTFast;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ServerGET : MonoBehaviour
{
    [Serializable]
    public class InitResponse
    {
        public string userId;
        public string uploadUrl;
        public string downloadUrl;
        public string fileListUrl;
    }

    public Text DEBUG;
    //public Text FILES;
    public TextMeshProUGUI FILES;
    public Text UUID;
    public TextMeshProUGUI IFtext;

    public GameObject back;
    public GameObject MainObj1, MainObj2, Keyboard, urlScreen, errorScreen;

    private GameObject SERVER_TEST;

    public InitResponse sessionData;

    public List<string> scene_list = new List<string>();

    public Transform scroll_content;
    public GameObject button_prefab;

    public Text error_header;
    public Text error_message;

    // loading slider
    public GameObject loadingScreen;
    public Slider loadingBar;
    public Text statusText;
    //public GameObject loadingSpinner;
    public Transform geometryParent;

    bool loadStatus;

    // Set by ShowError. `loadStatus` means only "is the scene safe to enter", and several paths clear
    // it after already putting a specific message on screen -- a refused run, a bad URL, geometry that
    // would not parse. Without this flag the generic "could not be reached" fallback in InitializeFile
    // overwrites every one of them, so a size refusal reads as a network failure.
    bool errorDisplayed;

    public static string GET_URL; 


    void Start()
    {
        loadStatus = false;

        // Returning to the menu from an analysis scene: drop the previous experiment entirely before
        // the user picks the next one. ReleaseEverything also unloads the now-orphaned meshes and
        // textures, which the old loop here never did -- so a second experiment was loaded on top of
        // the first one's assets rather than in place of them. (The old loop also skipped every other
        // child, since it indexed forward while destroying.)
        SessionMemory.ReleaseEverything();
    }

    // Step 1: Request a new user session from the server


    public void GetGetFileList()
    {
        Debug.Log("Getting file list...");
        if (scroll_content.childCount > 0)
        {
            for (int i = 0; i < scroll_content.childCount; i++) { Destroy(scroll_content.GetChild(i).gameObject); }
        }

        try
        {
            StartCoroutine(GetFileList(IFtext.text));

        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowError("Error", "Unable to reach files. Please check your URL and try again.");
        }
    }

    public void GetGetFileList(string URL="http://10.99.2.141:2525") // Probably deprecated - 10/16/2025
    {
        if (scroll_content.childCount > 0)
        {
            for (int i = 0; i < scroll_content.childCount; i++) { Destroy(scroll_content.GetChild(i).gameObject); }
        }

        StartCoroutine(GetFileList(URL));
    }

    public IEnumerator GetFileList()//Action<string[]> onFilesReceived
    {
        
        //FILES.text = "";

        UnityWebRequest request = UnityWebRequest.Get("http://10.94.10.228:3000/files/testuser/list");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string[] files = JsonHelper.FromJson<string>(FixJsonArray(request.downloadHandler.text));
            //onFilesReceived?.Invoke(files);
            //FILES.text += "Listing Files...";
            foreach (string file in files)
            {
                if (file.EndsWith(".glb"))
                { scene_list.Add(file.Substring(0, file.Length - 5));
                        //FILES.text += $"\n{file.Substring(0,file.Length-5)}";
                    FormatOptions(file.Substring(0, file.Length - 5),""); // MANUALLY
                }
                //FILES.text += file;
            }
        }
        else
        {
            //FILES.text += "Failed to get file list";
            //Debug.LogError($"Failed to get file list: {request.error}");
        }
    }
    string CleanURL(string url)
    {
        return new string(url.Where(c => !char.IsControl(c) && c != '\u200B' && c != '\u200C' && c != '\u200D' && c != '\uFEFF').ToArray());
    }

    public IEnumerator GetFileList(string URLinput)//Action<string[]> onFilesReceived
    {

        //FILES.text = "";
        string URL = CleanURL(URLinput);
        Debug.Log($"Requesting at {URL + "/files/testuser/list"}");
        UnityWebRequest request = UnityWebRequest.Get(URL + @"/files/testuser/list"); // MANUALLY LISTED

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Request Succeeded");
            string[] files = JsonHelper.FromJson<string>(FixJsonArray(request.downloadHandler.text));
            //onFilesReceived?.Invoke(files);
            //FILES.text += "Listing Files...";
            GameManager.number_of_runs = files.Length; // TODO: works for single-scene, not for multi-scene
            
            foreach (string file in files)
            {
                //FILES.text += file;
                if (file.EndsWith(".glb"))
                {
                    scene_list.Add(file.Substring(0, file.Length - 4));
                    //FILES.text += $"\n{file.Substring(0,file.Length-5)}";
                    FormatOptions(file.Substring(0, file.Length - 4), URL);
                }
            }
        }
        else
        {
            Debug.Log("Request Failed");
            ShowError("Error", "Unable to reach files. Please check your URL and internet connection and try again.");
            //FILES.text += "Failed to get file list";
            //Debug.LogError($"Failed to get file list: {request.error}");
        }
    }

    public void FormatOptions(string scene, string URL)
    {
        //Debug.Log("formatting buttons");

            Debug.Log(scene);
            GameObject temp = Instantiate(button_prefab);
            temp.transform.GetChild(0).GetComponent<Text>().text = scene;
        //temp.GetComponent<Toggle>().isOn = true;
        //temp.GetComponent<Button>().onClick.AddListener((interactor) => InitializeFile(scene));
        temp.GetComponent<Button>().onClick.AddListener(() => InitializeFile(scene, URL+"/files/testuser/"));

        temp.transform.SetParent(scroll_content, false);
        
    }
    
 
    public string GetUploadCurlExample(string filename)
    {
        DEBUG.text += $"Upload URL: {sessionData.uploadUrl}";
        return $"curl -X POST -F \"file=@{filename}\" {sessionData.uploadUrl}";
    }

    private string FixJsonArray(string rawJson)
    {
        return "{\"array\":" + rawJson + "}";
    }

    public static class JsonHelper
    {
        [Serializable]
        private class Wrapper<T>
        {
            public T[] array;
        }

        public static T[] FromJson<T>(string json)
        {
            return JsonUtility.FromJson<Wrapper<T>>(json).array;
        }
    }

    public void setactive(GameObject obj){obj.SetActive(true);}

    public void Back()
    { setactive(MainObj1); MainObj2.SetActive(false); Keyboard.SetActive(false); }

    async public void InitializeFile(string name, string URL)
    {
        /*if (SERVER_TEST != null)
        {
            foreach (Transform child in SERVER_TEST.transform)
            {
                GameObject.Destroy(child.gameObject);
            }
            var existingGltf = SERVER_TEST.GetComponent<GLTFast.GltfAsset>();
            if (existingGltf != null)
            {
                Destroy(existingGltf);
            }
        }
        else
        {
            SERVER_TEST = new GameObject(name);
        }*/
        try
        {
            errorDisplayed = false;

            // this is to test that the gameobject is actually parsable
            GameManager.instance.customscene = name;
            GameManager.instance.URL = URL;

            await LoadGLB(URL + $"{name}.glb");

            // Pull every run binary now, while the user is already watching a loading screen. The runs
            // are the slow half of the load, and fetching them on demand inside the Custom scene meant
            // a full download stall every time the carousel changed run. A failure here is not fatal:
            // RunManager falls back to downloading a missing run on demand, exactly as before.
            if (loadStatus) await PrefetchRuns(URL);

            if (loadStatus)
            {
                SceneManager.LoadScene("Custom");
            }
            else if (!errorDisplayed)
            {
                // Only reached when something failed without reporting why.
                ShowError("Connection Lost", "The address could not be reached. Please check that you are on the same network and traffic is allowed");
            }
            

        }
        catch (Exception e) 
        {
            Debug.LogException(e);
            ShowError("Error", "Unable to reach files. Please check your URL and try again.");
        }

    }

    async Task LoadGLB(string url) // GLB is binary form of GLTF - unity recommends since embedded buffers in gltf is slow apparently. 
    {
        loadingScreen.SetActive(true);
        urlScreen.SetActive(false);
        SetSpheresOff();

        //loadingSpinner.SetActive(true);
        loadingBar.value = 0;
        loadingBar.gameObject.SetActive(true);
        statusText.text = "Downloading...";

        // Stream the download straight to a temp file instead of buffering the whole GLB in the managed heap.
        // The file is then read by glTFast as a stream, so the raw GLB never occupies managed memory at all --
        // this removes the ~file-size (x2) transient spike that was starving TrackReco.ReadBin.
        string glbPath = System.IO.Path.Combine(Application.temporaryCachePath, "g4vr_geometry.glb");

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            Debug.Log("Requesting at URL...");
            www.downloadHandler = new DownloadHandlerFile(glbPath) { removeFileOnAbort = true };
            var request = www.SendWebRequest();

            while (!request.isDone)
            {
                loadingBar.value = www.downloadProgress;
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                TryDeleteFile(glbPath);
                ShowError("URL Error", "There was an error loading data from this address. Please check that it is reachable and try again.");
                return;
            }

            Debug.Log($"Downloaded geometry to {glbPath}");

            statusText.text = "Parsing geometry...";
            loadingBar.value = 0.75f;

            var gltf = new GltfImport();

            // Keep imported textures non-readable and mip-less (no extra CPU copy / +33% mem). These are the
            // glTFast defaults, but set explicitly since textures are by far the largest memory consumer.
            var importSettings = new ImportSettings
            {
                GenerateMipMaps = false,
                TexturesReadable = false
            };

            bool success = await gltf.LoadFile(glbPath, null, importSettings);
            // glTFast has finished reading the file; delete it now to reclaim the disk and keep managed memory flat.
            TryDeleteFile(glbPath);
            if (!success)
            {
                ShowError("Geometry Load Error", "Failed to parse geometry data.");
                return;
            }

            statusText.text = "Instantiating model...";
            loadingBar.value = 0.9f;

            GameObject Geometry = GameObject.Find("Scene");
            success = await gltf.InstantiateMainSceneAsync(Geometry.transform);
            if (!success)
            {
                ShowError("Geometry Load Error", "Failed to instantiate geometry.");
                return;
            }

            // Post-import render optimization. The geometry is thousands of pieces built from only a handful
            // of meshes/materials. Disable shadows and cache each piece's original mesh (for TrackAnalyser's
            // per-piece colliders), then hand rendering to InstancedGeometryDrawer, which collapses the scene
            // to ~a dozen instanced draw calls. Built-in RP's automatic batchers won't do this (auto-instancing
            // fragments on sort order; runtime static batching didn't register and would balloon memory).
            TrackAnalyser.originalMeshes.Clear();
            foreach (MeshRenderer mr in Geometry.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    TrackAnalyser.originalMeshes[mr.gameObject] = mf.sharedMesh;
            }

            GameObject modelRoot = Geometry.transform.GetChild(0).gameObject;
            InstancedGeometryDrawer drawer = modelRoot.GetComponent<InstancedGeometryDrawer>();
            if (drawer == null)
                drawer = modelRoot.AddComponent<InstancedGeometryDrawer>();
            drawer.Build(modelRoot);

            modelRoot.SetActive(false);

            // Free imported-but-UNUSED assets -- this is the big one. glTFast loads every image/material
            // declared in the GLB (this file ships ~903 textures, ~0.74 GB) even though the instantiated
            // geometry references only a handful of materials. GltfImport holds references to ALL of them, so
            // UnloadUnusedAssets can't reclaim anything while `gltf` is alive. Drop the import object, GC it so
            // its references die, THEN unload: the unreferenced textures/materials are freed while the used
            // assets survive (still referenced by the scene's renderers). GltfImport has no finalizer, so
            // collecting it does not destroy the Unity assets.
            gltf = null;
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
            System.GC.Collect();

            loadingBar.value = 1f;
            statusText.text = "Model loaded.";
            loadStatus = true;
            //Geometry.SetActive(false);
            
            await Task.Delay(2000);
            
        }
    }

    // Downloads all of this address's run binaries into RunCache, driving the same loading bar the
    // geometry download used. Runs stream straight to disk, so this costs storage, not headset RAM.
    private async Task PrefetchRuns(string parentURL)
    {
        statusText.text = "Finding runs...";
        loadingBar.value = 0f;

        // Drop any previous address's run list first: if this listing fails we must fall back to
        // asking the server from inside the scene, not silently reuse a count from another server.
        RunCache.SetKnownRuns(null);

        // Likewise the fidelity verdict: it belongs to the experiment we are about to load.
        RunBudget.Reset();

        int[] runs = await RunCache.ListRuns(parentURL);

        if (runs == null)
        {
            // Listing failed. Leave KnownRuns unset so RunManager asks the server itself in-scene.
            Debug.LogWarning("[SERVER-GET] Could not list runs; they will be fetched on demand instead.");
            return;
        }

        if (runs.Length == 0)
        {
            Debug.LogWarning("[SERVER-GET] No run*.bin files found at this address.");
            RunCache.SetKnownRuns(runs);
            return;
        }

        RunCache.Prepare(parentURL);

        int failed = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            int runNumber = runs[i];
            float start = i / (float)runs.Length;
            float span = 1f / runs.Length;

            statusText.text = $"Downloading run {i + 1} of {runs.Length}...";

            string path = await RunCache.Fetch(parentURL, runNumber,
                p => loadingBar.value = start + Mathf.Clamp01(p) * span);

            if (path == null)
                failed++;

            loadingBar.value = start + span;
        }

        RunCache.SetKnownRuns(runs);

        // Every run is now on disk, so measure them all before the analysis scene is ever loaded.
        // Walking the binaries is pure file I/O, so it goes on a worker thread -- a freeze of even a
        // few hundred ms is unpleasant in a headset.
        statusText.text = "Checking run sizes...";

        List<string> cachedRuns = new List<string>();
        foreach (int runNumber in runs)
        {
            string cachedPath = RunCache.PathIfPresent(parentURL, runNumber);
            if (cachedPath != null)
                cachedRuns.Add(cachedPath);
        }

        RunBudget.Fidelity fidelity = await Task.Run(() => RunBudget.Evaluate(cachedRuns));

        if (fidelity == RunBudget.Fidelity.Refused)
        {
            // Refuse here rather than let the OS kill us partway through loading, which would look
            // like a crash to the user and tell them nothing.
            // We are not entering the analysis scene, so drop the geometry we just imported rather
            // than leaving it resident in the menu while the user picks something else.
            loadStatus = false;
            SessionMemory.ReleaseEverything();
            ShowError("Run Too Large", RunBudget.DescribeRefusal());
            return;
        }

        if (fidelity == RunBudget.Fidelity.MeshOnly)
        {
            Debug.LogWarning("[SERVER-GET] Loading at reduced fidelity; per-step interaction will be disabled.");
        }

        long megabytes = RunCache.CachedBytes(parentURL) / (1024 * 1024);
        Debug.Log($"[SERVER-GET] Cached {runs.Length - failed}/{runs.Length} runs ({megabytes} MB on disk).");

        loadingBar.value = 1f;
        statusText.text = failed == 0
            ? $"{runs.Length} run(s) ready."
            : $"{runs.Length - failed} of {runs.Length} run(s) ready.";

        await Task.Delay(500);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ServerGET] Could not delete temp GLB '{path}': {e.Message}");
        }
    }

    private void ShowError(string header, string message)
    {
        errorDisplayed = true;
        errorScreen.SetActive(true);
        loadingScreen.SetActive(false);
        error_header.text = header;
        error_message.text = message;

    }
    
    private void Update()
    {
        if (!errorScreen.activeSelf && !loadingScreen.activeSelf && !urlScreen.activeSelf && !MainObj1.activeSelf) { urlScreen.SetActive(true); }
    }

    private void SetSpheresOff()
    {
        GameObject obj1 = GameObject.Find("Sphere_1");
        GameObject obj2 = GameObject.Find("Sphere_2");
        obj1.SetActive(false);
        obj2.SetActive(false);
    }
}


