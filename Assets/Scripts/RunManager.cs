using GLTFast;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static ServerGET;

public class RunManager : MonoBehaviour
{
    // Start is called before the first frame update

    public int run;
    public GameObject track_renderer_Prefab;

    private GameObject runInstance;


    public GameObject runDisplay;
    public GameObject maxRunDisplay;

    public GameObject Carousel;

    private void Awake()
    {
        run = 0; 
    }

    public async void Start()
    {
        InstantiateRun();
        int buffer = await getRunCount(GameObject.Find("GameManager").transform.GetComponent<GameManager>().URL) - 1; // run indexed from 0
    }

    public void SwitchRun(int newrunnumber)
    {
        Debug.Log("[RUNMANAGER] Switching to run " + newrunnumber);
        run = newrunnumber;
        ClearCurrentRun();
        InstantiateRun();
    }

    private void ClearCurrentRun()
    {
        Debug.Log("[RUNMANAGER] Clearing run " + run);

        foreach (var typeEntry in NewBehaviourScript.trackInfo)
        {
            var tracksByType = typeEntry.Value;
            foreach (var track in tracksByType)
            {
                if (track.Value.trackObj != null)
                    Destroy(track.Value.trackObj);
            }
        }

        // Destroy the clone by reference. The old name lookup missed it whenever the clone had been
        // reparented under WorldScaleRoot, which left the previous run's TrackMeshRenderer alive --
        // and with it a whole run's worth of vertex and index buffers, since those are only released
        // by its OnDestroy.
        if (runInstance != null)
        {
            Destroy(runInstance);
            runInstance = null;
        }
        else
        {
            GameObject stale = GameObject.Find("track_renderer(Clone)");
            if (stale == null) stale = GameObject.Find("track_renderer");
            if (stale != null) Destroy(stale);
        }

        // Drop the parsed run itself. Its per-step arrays are the single largest allocation in the
        // scene, and holding them while the next run downloads and parses would double the peak.
        // The detector geometry is left alone -- it outlives a run switch.
        NewBehaviourScript.ReleaseRunData();
        TrackAnalyser.ReleaseRunState();

        Debug.Log("Cleared Run "+run);
    }

    public async void InstantiateRun()
    {
        //TextAsset newRun = Resources.Load<TextAsset>($"run{run}"); // to be replaced with server based logic.; use for manual debugging
        GameObject gManager = GameObject.Find("GameManager");
        if (gManager) 
        {
            string newRun = await PullRun(gManager.transform.GetComponent<GameManager>().URL);
            //TextAsset newRun = await PullRun(); // debugging with default address
            if (newRun != null)
            {
                // The previous run's GameObjects were destroyed before the download started, so by now
                // several frames have passed and Unity has actually processed those destroys. This is
                // therefore the point at which their meshes and materials can really be reclaimed --
                // and it is already a loading moment, so the collection pause is free.
                SessionMemory.Unload();

                track_renderer_Prefab.transform.GetComponent<NewBehaviourScript>().customFile = newRun;
                runInstance = Instantiate(track_renderer_Prefab);
                runInstance.transform.GetComponent<NewBehaviourScript>().enabled = true;
                runInstance.transform.GetComponent<TrackAnalyser>().enabled = true;

                Debug.Log("Instantiated Run " + run);
            }
            else { Debug.LogError("Null Asset!"); }
        } else { Debug.LogError("Your damn game manager instance does not exist!"); }
        
    }

    // Resolves the current run to a file on disk. ServerGET prefetched every run into RunCache during
    // the menu's loading screen, so the common path is a cache hit and costs no network time at all --
    // which is the whole point: switching runs in-scene no longer stalls on a download.
    private async Task<string> PullRun(string parentURL = @"http://192.168.0.214:2535/files/testuser/")
    {
        string cached = RunCache.PathIfPresent(parentURL, run);
        if (cached != null)
        {
            Debug.Log($"[RunManager] Run {run} served from cache ({cached}).");
            return cached;
        }

        // Miss: the prefetch failed, was skipped, or this run appeared after it. Fetch it now and seed
        // the cache, so a second visit to this run is instant. allowSkip:false -- a miss means there is
        // no usable local copy, so the HEAD round-trip would only add latency.
        Debug.Log($"[RunManager] Run {run} not cached; downloading on demand.");
        return await RunCache.Fetch(parentURL, run, null, allowSkip: false);
    }


    public async Task<int> getRunCount(string parentURL)
    {
        // The menu already listed the runs while prefetching them, so reuse that instead of making the
        // scene wait on a second request. It is also more accurate than the fallback below, which
        // assumes the listing holds exactly one non-run file (the .glb).
        int[] known = RunCache.KnownRuns;
        if (known != null && known.Length > 0)
        {
            Debug.Log($"[RunManager] Using {known.Length} prefetched run(s).");
            Carousel.transform.GetComponent<NumberCarousel>().InstantiateCarousel(known.Length);
            return known.Length;
        }

        string url = parentURL + "list";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            Debug.Log("[RunManager] Requesting Run Count from " + url);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[RunManager] Request Succeeded");
                string[] files = JsonHelper.FromJson<string>(FixJsonArray(request.downloadHandler.text));
                Carousel.transform.GetComponent<NumberCarousel>().InstantiateCarousel(files.Length-1);
                return files.Length - 1;
            }
            else
            {
                Debug.LogError($"[RunManager] Request Failed: {request.error}");
                
                return 1; 
            }
        }
    }
    // helper functions

    private void ParseText(TextAsset csv )
    {
        string[] lines = csv.text.Split('\n');
        foreach (string line in lines) {
            Debug.Log(line);
        }
    }

    private string FixJsonArray(string rawJson)
    {
        return "{\"array\":" + rawJson + "}";
    }
}
