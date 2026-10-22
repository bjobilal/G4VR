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
    private StreamWriter writer;

    void Start()
    {
        filePath = Path.Combine(Application.persistentDataPath, "fps_log_BC89MB.csv");

        bool isNew = !File.Exists(filePath);

        // One handle held open for the session. AppendAllText opened, wrote and closed the file on
        // every sample, which is a round trip through the Android filesystem once a second -- enough
        // to show up in the very measurements this is here to take.
        writer = new StreamWriter(filePath, append: true);

        if (isNew)
            writer.WriteLine("TimeSinceStart,FPS");

        writer.Flush();

        Debug.Log("FPS Log Path: " + filePath);
    }

    void OnDestroy()
    {
        if (writer == null)
            return;

        writer.Flush();
        writer.Dispose();
        writer = null;
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
        if (writer == null)
            return;

        writer.WriteLine(time.ToString("F3") + "," + fps.ToString("F2"));
        writer.Flush(); // samples are a second apart, so keep the file readable if the app is killed
    }

}
