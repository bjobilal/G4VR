using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using Unity.VisualScripting;


//using TMPro.EditorUtilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class MainScript : MonoBehaviour
{
    // Start is called before the first frame update

    public GameObject experimentButton;
    public GameObject descriptionsPanel;
    public GameObject MainObj2;
    public GameObject MainObj1;
    public GameObject Keyboard;
    public TextMeshProUGUI DEBUG;

    public Material[] skyboxMaterials;

    private Dictionary<string, string> EXAMPLE_DESCRIPTIONS = new Dictionary<string, string> { 
        { "B3A","A positron-emitted tomography system" }, {"B4A","A Pb-LAr Calorimeter"},{"B5","A double-arm spectrometer"},
    {"BROWSER","Load custom experiment files" } };

    public void filebrowsing() { MainObj2.SetActive(true); MainObj1.SetActive(false); Keyboard.SetActive(true); }

    private void Awake()
    {
        
            int index = Random.Range(0, skyboxMaterials.Length);
            RenderSettings.skybox = skyboxMaterials[index]; // fine if it raises an error here.

            DynamicGI.UpdateEnvironment();

        descriptionsPanel.SetActive(false);

    }
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LoadScene(string name)
    {
        SceneManager.LoadScene(name);
    }

    public void show_descriptions(string key)
    {
        descriptionsPanel.SetActive(true);
        descriptionsPanel.GetComponentInChildren<Text>().text = EXAMPLE_DESCRIPTIONS[key];
    }
    public void hide_descriptions() { descriptionsPanel.SetActive(false); }

    public void changecolor(GameObject colorMe)
    {
        Color current = colorMe.transform.GetComponent<Renderer>().material.color;

        current = Color.red;

        colorMe.transform.GetComponent<Renderer>().material.color = current;
    }

    public void changeback(GameObject gobj)
    {
        Color current = gobj.transform.GetComponent<Renderer>().material.color;

        current = Color.white;

        gobj.transform.GetComponent<Renderer>().material.color = current;

    }

    
    public void CloseButton(GameObject obj)
    {
        obj.SetActive(false);
    }


}
