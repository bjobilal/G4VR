using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Decides, before a run is ever loaded, how much of the visualisation this headset can afford.
//
// Users do put very large exports in. Rendering one at full fidelity does not degrade gracefully --
// the OS simply kills the application for exceeding its memory allowance, with no explanation to the
// person wearing it. So every run file is measured up front, while the menu's loading screen is
// already on display, and the most expensive one decides the fidelity for the whole session.
//
// The runs load one at a time, so it is the WORST SINGLE FILE that matters, not the sum.
public static class RunBudget
{
    public enum Fidelity
    {
        Full,     // everything, including per-step hover/click colliders
        MeshOnly, // tracks, edep, movie and sliders -- but no per-step interaction
        Refused,  // too large even for mesh-only; we decline rather than be killed
    }

    // Exact on-disk size of one trajectory point: nine floats (x, y, z, time, edep, px, py, pz, energy)
    // plus two ushorts (processID, volumeID). Must match NewBehaviourScript.ReadBIN.
    private const int BytesPerPointOnDisk = 40;

    // Per-track bytes before the point arrays begin: trackID, [eventID in v2+], nameID, charge, r, g, b,
    // and the point count itself.
    private const int TrackHeaderV2 = 34;
    private const int TrackHeaderV1 = 30;

    // Thresholds
    public const int BaseBytesPerPoint = 40;


    public const int ColliderBytesPerSegment = 2560;

    public const long FullFidelityBudgetBytes = 250_000_000;

    public const long MaxRenderableSegments = 250_000;

    // Shown to the user, verbatim, when we drop to mesh-only.
    public const string MeshOnlyNotice =
        "your file is estimated to take more memory than the application can support, so we are only able to partially support the visualization feature";


    public static Fidelity Mode { get; private set; } = Fidelity.Full;

    // The heaviest file seen, by drawn segments.
    public static long WorstCaseSegments { get; private set; }
    public static long WorstCasePoints { get; private set; }
    public static long WorstCaseTracks { get; private set; }
    public static string WorstCaseFile { get; private set; }

    public static bool HasEvaluated { get; private set; }

    // The one thing the track-building code needs to know.
    public static bool SkipPerStepColliders => Mode != Fidelity.Full;

    public static void Reset()
    {
        Mode = Fidelity.Full;
        WorstCaseSegments = 0;
        WorstCasePoints = 0;
        WorstCaseTracks = 0;
        WorstCaseFile = null;
        HasEvaluated = false;
    }

    public static void EnsureEvaluated(string path)
    {
        if (HasEvaluated || string.IsNullOrEmpty(path))
            return;

        Debug.LogWarning("[RUN-BUDGET] Run was not measured in the menu; measuring it now.");
        Evaluate(new[] { path });
    }

    // probing
    public static bool Probe(string path, out long points, out long tracks)
    {
        points = 0;
        tracks = 0;

        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 16)
                    return false;

                reader.ReadBytes(4);             // magic
                uint version = reader.ReadUInt32();
                reader.ReadUInt32();             // stringCount
                uint trackCount = reader.ReadUInt32();

                int trackHeader = version >= 2 ? TrackHeaderV2 : TrackHeaderV1;

                for (uint t = 0; t < trackCount; t++)
                {
                    if (stream.Position + trackHeader > stream.Length)
                        return false;

                    // Skip to the point count, which is the last field of the track header.
                    stream.Seek(trackHeader - 4, SeekOrigin.Current);
                    uint n = reader.ReadUInt32();
                    points += n;

                    long pointsEnd = stream.Position + (long)n * BytesPerPointOnDisk;
                    if (pointsEnd > stream.Length)
                        return false;

                    stream.Seek(pointsEnd, SeekOrigin.Begin);
                }

                tracks = trackCount;
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RUN-BUDGET] Could not probe '{Path.GetFileName(path)}': {e.Message}");
            return false;
        }
    }


    public static long SegmentsOf(long points, long tracks) => Math.Max(points - tracks, 0);

    public static void EstimateFromSize(long fileBytes, out long points, out long tracks)
    {
        points = fileBytes / 44;
        tracks = points / 5;  // conservative: fewer tracks means more segments
    }

    // Measures every run and picks the fidelity the largest of them can sustain.
    public static Fidelity Evaluate(IEnumerable<string> paths)
    {
        Reset();

        long worstSegments = -1;
        long worstPoints = 0;
        long worstTracks = 0;
        string worstFile = null;
        int probed = 0;

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;

            if (!Probe(path, out long points, out long tracks))
            {
                EstimateFromSize(new FileInfo(path).Length, out points, out tracks);
                Debug.LogWarning($"[RUN-BUDGET] '{Path.GetFileName(path)}' could not be walked; estimating {points:N0} points from its size.");
            }

            long segments = SegmentsOf(points, tracks);
            probed++;

            // Logged per file, not just for the worst one: correlating "this file felt fine" with a
            // segment count is the only way MaxRenderableSegments gets tuned on real evidence.
            Debug.Log($"[RUN-BUDGET] '{Path.GetFileName(path)}': {tracks:N0} tracks, {points:N0} points, {segments:N0} segments.");

            if (segments > worstSegments)
            {
                worstSegments = segments;
                worstPoints = points;
                worstTracks = tracks;
                worstFile = Path.GetFileName(path);
            }
        }

        WorstCaseSegments = Math.Max(worstSegments, 0);
        WorstCasePoints = worstPoints;
        WorstCaseTracks = worstTracks;
        WorstCaseFile = worstFile;
        HasEvaluated = probed > 0;

        if (probed == 0)
        {
            Debug.LogWarning("[RUN-BUDGET] No run files could be probed; assuming full fidelity.");
            return Mode;
        }

        long meshOnlyBytes = worstPoints * BaseBytesPerPoint;
        long fullBytes = meshOnlyBytes + WorstCaseSegments * ColliderBytesPerSegment;

        if (WorstCaseSegments > MaxRenderableSegments)
            Mode = Fidelity.Refused;
        else if (fullBytes > FullFidelityBudgetBytes)
            Mode = Fidelity.MeshOnly;
        else
            Mode = Fidelity.Full;

        Debug.Log($"[RUN-BUDGET] Probed {probed} run(s). Heaviest: '{worstFile}' with {WorstCaseSegments:N0} segments " +
                  $"(~{Megabytes(fullBytes)} MB full, ~{Megabytes(meshOnlyBytes)} MB mesh-only; the ceiling here is " +
                  $"draw cost, not memory). Mode: {Mode}.");

        return Mode;
    }

    // Message for the menu's error screen when a run is beyond even mesh-only.
    public static string DescribeRefusal()
    {
        string file = string.IsNullOrEmpty(WorstCaseFile) ? "One of the run files" : $"Run file '{WorstCaseFile}'";

        return $"{file} is too large for G4VR to display smoothly " +
               "Please export the run with fewer events or filtered tracks and try again.";
    }

    private static long Megabytes(long bytes) => bytes / (1024 * 1024);
}
