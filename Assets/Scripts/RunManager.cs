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
        Debug.Log("Clearing " + NewBehaviourScript.tracks.Count + " tracks");

        foreach (var typeEntry in NewBehaviourScript.trackInfo) // you cannot access List<> tracks - all are null.
        {
            var tracksByType = typeEntry.Value;
            foreach (var track in tracksByType)
            {
                Destroy(track.Value.trackObj);
            }
        }

        if (GameObject.Find("track_renderer")) Destroy(GameObject.Find("track_renderer"));
        if (GameObject.Find("track_renderer(Clone)")) Destroy(GameObject.Find("track_renderer(Clone)"));

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
                track_renderer_Prefab.transform.GetComponent<NewBehaviourScript>().customFile = newRun;
                runInstance = Instantiate(track_renderer_Prefab);
                runInstance.transform.GetComponent<NewBehaviourScript>().enabled = true;
                runInstance.transform.GetComponent<TrackAnalyser>().enabled = true;

                Debug.Log("Instantiated Run " + run);
            }
            else { Debug.LogError("Null Asset!"); }
        } else { Debug.LogError("Your damn game manager instance does not exist!"); }
        
    }

    private async Task<string> PullRun(string parentURL = @"http://192.168.0.214:2535/files/testuser/")
    {
        //string url = parentURL + "run" + run + ".csv";
        //string savePath = Path.Combine(Application.persistentDataPath, $"run{run}.csv");

        // for binary
        string url = parentURL + "run" + run + ".bin";
        string savePath = Path.Combine(Application.persistentDataPath, $"run{run}.bin");

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            Debug.Log("[RunManager] Requesting at URL: " + url);

            www.downloadHandler = new DownloadHandlerFile(savePath);

            var operation = www.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
                return null;
            }

            Debug.Log("Download complete to disk.");
            return savePath;
        }
    }


    public async Task<int> getRunCount(string parentURL)
    {
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
