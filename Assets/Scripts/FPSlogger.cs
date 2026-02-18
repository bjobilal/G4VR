using UnityEngine;
using System.IO;
using System.Text;

public class FPSLogger : MonoBehaviour
{
    public float logInterval = 1.0f; // seconds between logs

    private float timeAccumulator = 0f;
    private int frameCount = 0;
    private float logTimer = 0f;

    private string filePath;

    void Start()
    {
        filePath = Path.Combine(Application.persistentDataPath, "fps_log_BC89MB.csv");

        // Write header if file doesn't exist
        if (!File.Exists(filePath))
        {
            File.WriteAllText(filePath, "TimeSinceStart,FPS\n");
        }

        Debug.Log("FPS Log Path: " + filePath);
    }

    void Update()
    {
        float currentFPS = 1f / Time.deltaTime;

        logTimer += Time.deltaTime;

        if (logTimer >= logInterval)
        {
            float timeSinceStart = Time.time;

            WriteToCSV(timeSinceStart, currentFPS);

            logTimer = 0f;
        }
    }

    void WriteToCSV(float time, float fps)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(time.ToString("F3") + "," + fps.ToString("F2"));

        File.AppendAllText(filePath, sb.ToString());
    }

}
