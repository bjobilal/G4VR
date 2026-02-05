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

    public static string GET_URL; 


    void Start()
    {
        loadStatus = false;
        try { GameObject obj = GameObject.Find("Scene"); if (obj.transform.childCount > 0) { for (int i = 0; i < obj.transform.childCount; i++) { Destroy(obj.transform.GetChild(i).gameObject); } } } catch { }
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
            // this is to test that the gameobject is actually parsable
            GameManager.instance.customscene = name;
            GameManager.instance.URL = URL;

            await LoadGLB(URL + $"{name}.glb");
            
            if (loadStatus) SceneManager.LoadScene("Custom");
            else
            {
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

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            Debug.Log("Requesting at URL...");
            www.downloadHandler = new DownloadHandlerBuffer();
            var request = www.SendWebRequest();

            while (!request.isDone)
            {
                loadingBar.value = www.downloadProgress; 
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                ShowError("URL Error", "There was an error loading data from this address. Please check that it is reachable and try again.");
                return;
            }

            byte[] data = www.downloadHandler.data;
            Debug.Log($"Downloaded {data.Length} bytes");

            statusText.text = "Parsing geometry...";
            loadingBar.value = 0.75f;

            var gltf = new GltfImport();

            bool success = await gltf.LoadGltfBinary(data);
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

            Geometry.transform.GetChild(0).gameObject.SetActive(false);
            loadingBar.value = 1f;
            statusText.text = "Model loaded.";
            loadStatus = true;
            //Geometry.SetActive(false);
            
            await Task.Delay(2000);
            
        }
    }

    private void ShowError(string header, string message)
    {
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


