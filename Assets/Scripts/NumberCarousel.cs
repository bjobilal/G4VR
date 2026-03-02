using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System;
using Unity.VisualScripting;

public class NumberCarousel : MonoBehaviour
{
    [Header("Carousel Setup")]
    public List<TextMeshPro> numbers = new List<TextMeshPro>();
    public float spacing = 0.15f;        // smaller spacing = less spread
    public float scaleFactor = 0.7f;     // side numbers scale (0.7–0.9 is subtle)
    public float moveSpeed = 6f;         // how quickly transitions happen
    public float fadeAmount = 0.4f;      // transparency of side numbers
    public float curve = 0.25f;          // curvature intensity
    public int currentIndex = 0;
    public int currentRun = 0;

    [Header("Input Settings")]
    public float inputCooldown = 0.3f;
    private float lastInputTime = 0f;

    [Header("Optional - Attach Controller Transform")]
    public Transform controller;

    public GameObject Manager;
    public GameObject numberPrefab;

    void Start()
    {
        UpdateCarouselImmediate();
    }

    void Update()
    {
        HandleInput();
        UpdateCarouselSmooth();
    }

    public void InstantiateCarousel(int numberOfRuns)
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
        numbers.Clear();

        // Create each number object
        for (int i = 0; i < numberOfRuns; i++)
        {
            // Instantiate prefab as child of this GameObject (the carousel)
            GameObject newNumber = Instantiate(numberPrefab, transform);
            newNumber.name = $"{i}";

            // Get its TextMeshPro component
            TextMeshPro tmp = newNumber.GetComponent<TextMeshPro>();
            if (tmp == null)
            {
                Debug.LogError("Prefab must contain a TextMeshPro component!");
                Destroy(newNumber);
                return;
            }

            // Set the text
            tmp.text = (i).ToString();

            // Set neutral position + scale initially
            newNumber.transform.localPosition = Vector3.zero;
            newNumber.transform.localScale = Vector3.one;

            // Add to list for carousel logic
            numbers.Add(tmp);
        }

        print("[CAROUSEL] Numbers has length " + numbers.Count);
        // Position them immediately
        UpdateCarouselImmediate();
    }


    void HandleInput()
    {
        OVRInput.Update();

        float leftTrigger = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        if (Time.time - lastInputTime > inputCooldown)
        {
            if (leftTrigger > 0.8f)
            {
                NextNumber();
                lastInputTime = Time.time;
            }
        }

        /* // Button logic breakdown during single controller sessions
        if (OVRInput.GetDown(OVRInput.Button.Four)) NextNumber();     // Y button
        if (OVRInput.GetDown(OVRInput.Button.Three)) PreviousNumber(); // X button
        */
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick)) LoadRuns();
        if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
        {
            LoadRuns();
            Debug.Log("[CAROUSEL] Requesting new run load");
        }

    }

    void NextNumber()
    {
        if (numbers.Count == 0)
        {
            Debug.Log("[CAROUSEL] Numbers list is empty");
            return;
        }

        currentIndex = (currentIndex + 1) % numbers.Count;
        Debug.Log($"[CAROUSEL] Focused index: {currentIndex}");
        HighlightFocused();
    }

    void PreviousNumber()
    {
        Debug.Log("[CAROUSEL] Handling Previous");
        if (numbers.Count == 0)
        {
            Debug.Log("[CAROUSEL] Numbers list is empty");
            return;
        }

        // Decrement and wrap properly
        currentIndex--;
        if (currentIndex < 0) currentIndex = numbers.Count - 1;

        Debug.Log($"[CAROUSEL] Focused index: {currentIndex}");

        // Optionally snap immediately to the new index:
        // UpdateCarouselImmediate();

        // Optional visual debug highlight:
        HighlightFocused();
    }

    // Optional helper to change color of focused number so you can see it's updating
    void HighlightFocused()
    {
        for (int i = 0; i < numbers.Count; i++)
        {
            Color col = numbers[i].color;
            if (i == currentIndex)
            {
                // focused number: make it bright and fully opaque
                col = Color.white;
                col.a = 1f;
            }
            else
            {
                // side numbers: dim a bit
                col = Color.gray;
                col.a = Mathf.Lerp(1f, fadeAmount, Mathf.Clamp01(Mathf.Abs(i - currentIndex) / 2f));
            }
            numbers[i].color = col;
        }
    }

    void UpdateCarouselSmooth()
    {
        for (int i = 0; i < numbers.Count; i++)
        {
            int offset = i - currentIndex;

            // Wrap-around effect
            if (offset > numbers.Count / 2) offset -= numbers.Count;
            if (offset < -numbers.Count / 2) offset += numbers.Count;

            float angle = offset * curve;

            // Subtle curved placement
            Vector3 targetPos = new Vector3(
                Mathf.Sin(angle) * spacing,
                0,
                Mathf.Cos(angle) * spacing * 0.25f
            );

            // Gentle scale difference
            float targetScale = Mathf.Lerp(1f, scaleFactor, Mathf.Clamp01(Mathf.Abs(offset) / 2f));
            float targetAlpha = Mathf.Lerp(1f, fadeAmount, Mathf.Clamp01(Mathf.Abs(offset) / 2f));

            // Smooth position/scale fade
            numbers[i].transform.localPosition = Vector3.Lerp(
                numbers[i].transform.localPosition,
                targetPos,
                Time.deltaTime * moveSpeed
            );

            numbers[i].transform.localScale = Vector3.Lerp(
                numbers[i].transform.localScale,
                Vector3.one * targetScale,
                Time.deltaTime * moveSpeed
            );

            // Fade side numbers
            Color c = numbers[i].color;
            c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * moveSpeed);
            numbers[i].color = c;
        }
    }

    void UpdateCarouselImmediate()
    {
        for (int i = 0; i < numbers.Count; i++)
        {
            int offset = i - currentIndex;

            if (offset > numbers.Count / 2) offset -= numbers.Count;
            if (offset < -numbers.Count / 2) offset += numbers.Count;

            float angle = offset * curve;
            Vector3 pos = new Vector3(
                Mathf.Sin(angle) * spacing,
                0,
                Mathf.Cos(angle) * spacing * 0.25f
            );

            numbers[i].transform.localPosition = pos;
            numbers[i].transform.localScale = Vector3.one * Mathf.Lerp(1f, scaleFactor, Mathf.Clamp01(Mathf.Abs(offset) / 2f));

            Color c = numbers[i].color;
            c.a = Mathf.Lerp(1f, fadeAmount, Mathf.Clamp01(Mathf.Abs(offset) / 2f));
            numbers[i].color = c;
        }
    }

    void LoadRuns()
    {
        if (currentIndex != currentRun)
        {
            // Process a new run if the selected run is different than the currently rendered run. 

            Debug.Log("[CAROUSEL] Requesting run change from " + currentIndex + " to " + currentRun);

            Manager.transform.GetComponent<RunManager>().SwitchRun(currentIndex);

            currentRun = currentIndex;
            

        }
        else
        {
            return;
        }
    }
}
