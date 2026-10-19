using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// On-disk cache of the run binaries for one server URL.
//
// Run files used to be fetched one at a time, from inside the Custom scene, at the exact moment the
// user asked to see a run -- so every run switch paid a full download before anything could happen.
// ServerGET now pulls all of them during the menu's existing loading screen (where the user is already
// waiting for the geometry), and the in-scene path just reads them off disk.
//
// This is disk, not memory: the files are streamed straight to storage by DownloadHandlerFile and are
// only read back one at a time, so prefetching does not change the headset's RAM high-water mark.
//
// Cached under persistentDataPath/runcache/<hash of the parent URL>/run{N}.bin. The key is the URL
// rather than the experiment name because the server lists runs flat, alongside every .glb -- one run
// set per address, shared by all experiments at that address.
public static class RunCache
{
    private const string RootFolder = "runcache";
    private static readonly Regex RunFilePattern = new Regex(@"^run(\d+)\.bin$", RegexOptions.IgnoreCase);

    // Run numbers discovered by the last successful listing, ascending. Null until a prefetch has run,
    // which is the signal for RunManager to fall back to asking the server itself.
    public static int[] KnownRuns { get; private set; }

    private static string Root => Path.Combine(Application.persistentDataPath, RootFolder);

    // A short, filesystem-safe, stable key for a URL.
    private static string KeyFor(string parentURL)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in parentURL ?? "")
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }
    }

    public static string DirectoryFor(string parentURL) => Path.Combine(Root, KeyFor(parentURL));

    public static string PathFor(string parentURL, int run) =>
        Path.Combine(DirectoryFor(parentURL), $"run{run}.bin");

    // The cached path for a run, or null if it is not on disk. Callers fall back to downloading.
    public static string PathIfPresent(string parentURL, int run)
    {
        string path = PathFor(parentURL, run);
        return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    // Creates this URL's cache directory and deletes every other one, so the disk footprint stays
    // bounded to a single server's run set no matter how many addresses get visited.
    public static void Prepare(string parentURL)
    {
        string dir = DirectoryFor(parentURL);
        Directory.CreateDirectory(dir);

        try
        {
            foreach (string other in Directory.GetDirectories(Root))
            {
                if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.Delete(other, true);
                Debug.Log($"[RUN-CACHE] Evicted stale cache '{Path.GetFileName(other)}'.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RUN-CACHE] Could not evict stale caches: {e.Message}");
        }
    }

    // Asks the server which runs exist. Returns null when the listing could not be read at all, and an
    // empty array when it succeeded but held no run files -- the two cases are reported differently.
    public static async Task<int[]> ListRuns(string parentURL)
    {
        string url = parentURL + "list";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[RUN-CACHE] Could not list runs at {url}: {request.error}");
                return null;
            }

            List<int> runs = new List<int>();
            try
            {
                string[] files = ServerGET.JsonHelper.FromJson<string>("{\"array\":" + request.downloadHandler.text + "}");
                foreach (string file in files)
                {
                    Match m = RunFilePattern.Match(file.Trim());
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                        runs.Add(n);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RUN-CACHE] Malformed listing from {url}: {e.Message}");
                return null;
            }

            runs.Sort();
            return runs.ToArray();
        }
    }

    public static void SetKnownRuns(int[] runs) => KnownRuns = runs;

    // True when the cached copy's size matches what the server reports, so an unchanged run can be
    // skipped on a repeat visit. A server that does not answer HEAD simply gets re-downloaded.
    private static async Task<bool> IsUpToDate(string url, string path)
    {
        if (!File.Exists(path))
            return false;

        using (UnityWebRequest head = UnityWebRequest.Head(url))
        {
            var operation = head.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (head.result != UnityWebRequest.Result.Success)
                return false;

            string length = head.GetResponseHeader("Content-Length");
            if (!long.TryParse(length, out long remoteSize) || remoteSize <= 0)
                return false;

            return new FileInfo(path).Length == remoteSize;
        }
    }

    // Downloads one run into the cache, streaming to disk. `onProgress` receives 0..1 for this file.
    // Returns the cached path, or null if the download failed.
    public static async Task<string> Fetch(string parentURL, int run, Action<float> onProgress = null, bool allowSkip = true)
    {
        string url = parentURL + $"run{run}.bin";
        string path = PathFor(parentURL, run);

        Directory.CreateDirectory(Path.GetDirectoryName(path));

        if (allowSkip && await IsUpToDate(url, path))
        {
            Debug.Log($"[RUN-CACHE] run{run}.bin already cached and current.");
            onProgress?.Invoke(1f);
            return path;
        }

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            // removeFileOnAbort so a failed transfer never leaves a truncated file that later looks
            // like a valid cache entry.
            www.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };

            var operation = www.SendWebRequest();
            while (!operation.isDone)
            {
                onProgress?.Invoke(www.downloadProgress);
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[RUN-CACHE] Failed to download {url}: {www.error}");
                TryDelete(path);
                return null;
            }

            onProgress?.Invoke(1f);
            return path;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RUN-CACHE] Could not delete '{path}': {e.Message}");
        }
    }

    // Total bytes currently cached for a URL, for logging and for the disk warning in ServerGET.
    public static long CachedBytes(string parentURL)
    {
        string dir = DirectoryFor(parentURL);
        if (!Directory.Exists(dir))
            return 0;

        long total = 0;
        foreach (string file in Directory.GetFiles(dir))
            total += new FileInfo(file).Length;

        return total;
    }

    public static void ClearAll()
    {
        KnownRuns = null;
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RUN-CACHE] Could not clear cache: {e.Message}");
        }
    }
}
