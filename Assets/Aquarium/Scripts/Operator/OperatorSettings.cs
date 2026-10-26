using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Aquarium
{
    /// <summary>
    /// Settings the operator changes from the control panel. Saved to
    /// %USERPROFILE%/AppData/LocalLow/&lt;company&gt;/&lt;product&gt;/AquariumSettings.json and applied on every start,
    /// so the installation always comes up the way it was last configured.
    /// </summary>
    [Serializable]
    public sealed class OperatorSettings
    {
        // Display routing (0-based: 0 = operator monitor)
        public int leftWallDisplay = 1;
        public int rightWallDisplay = 2;
        public int floorDisplay = 3;

        // Live look & feel
        public float sunIntensity = 1.9f;
        public float fogDensity = 0.042f;
        public float godRays = 0.4f;
        public float waterLayerOpacity = 0.6f;
        public float rippleStrength = 1f;
        public float fleeRadius = 2.6f;
        public float eyeHeight = 1.6f;
        public bool mouseHover = true;
        public bool feeding = true;
        public bool previews = true;

        // Applied on restart
        public float fishSizeMultiplier = 2f;
        public List<string> disabledSpecies = new List<string>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "AquariumSettings.json");
        public static bool Exists => File.Exists(FilePath);

        /// <summary>Saved settings, or the current scene values when nothing has been saved yet.</summary>
        public static OperatorSettings LoadOrCapture(AquariumDirector d)
        {
            if (Exists)
            {
                try
                {
                    var s = JsonUtility.FromJson<OperatorSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Aquarium] Could not read {FilePath}: {e.Message}. Using scene settings.");
                }
            }
            return Capture(d);
        }

        public static OperatorSettings Capture(AquariumDirector d)
        {
            var s = new OperatorSettings
            {
                leftWallDisplay = d.display.leftWallCamera != null ? d.display.leftWallCamera.targetDisplay : d.display.leftWallDisplay,
                rightWallDisplay = d.display.rightWallCamera != null ? d.display.rightWallCamera.targetDisplay : d.display.rightWallDisplay,
                floorDisplay = d.display.floorCamera != null ? d.display.floorCamera.targetDisplay : d.display.floorDisplay,
                sunIntensity = d.water.sunIntensity,
                fogDensity = d.water.fogDensity,
                godRays = d.water.godRayIntensity,
                waterLayerOpacity = d.waterLayer.maxOpacity,
                rippleStrength = d.interaction.rippleStrength,
                fleeRadius = d.interaction.fleeRadius,
                eyeHeight = d.tunnel.eyePosition.y,
                mouseHover = d.interaction.mouseHover,
                feeding = d.interaction.feedDelay < 100f,
                previews = d.display.controlPanelPreviews,
                fishSizeMultiplier = d.fishSizeMultiplier,
            };
            foreach (var sp in d.species)
                if (sp != null && !sp.enabled) s.disabledSpecies.Add(sp.name);
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Aquarium] Could not save {FilePath}: {e.Message}");
            }
        }

        public static void Delete()
        {
            if (Exists) File.Delete(FilePath);
        }

        /// <summary>Everything that has to be in place before the aquarium is built.</summary>
        public void ApplyBeforeBuild(AquariumDirector d)
        {
            d.fishSizeMultiplier = fishSizeMultiplier;
            foreach (var sp in d.species)
                if (sp != null) sp.enabled = !disabledSpecies.Contains(sp.name);
            var ds = d.display;
            if (ds.leftWallCamera != null) ds.leftWallCamera.targetDisplay = leftWallDisplay;
            if (ds.rightWallCamera != null) ds.rightWallCamera.targetDisplay = rightWallDisplay;
            if (ds.floorCamera != null) ds.floorCamera.targetDisplay = floorDisplay;
            ds.leftWallDisplay = leftWallDisplay;
            ds.rightWallDisplay = rightWallDisplay;
            ds.floorDisplay = floorDisplay;
            ApplyLive(d);
        }

        /// <summary>Values that can change while the aquarium is running.</summary>
        public void ApplyLive(AquariumDirector d)
        {
            d.water.sunIntensity = sunIntensity;
            d.water.fogDensity = fogDensity;
            RenderSettings.fogDensity = fogDensity;
            d.water.godRayIntensity = godRays;
            if (d.Environment != null) d.Environment.SetGodRayIntensity(godRays);
            d.waterLayer.maxOpacity = waterLayerOpacity;
            d.interaction.rippleStrength = rippleStrength;
            d.interaction.fleeRadius = fleeRadius;
            d.interaction.mouseHover = mouseHover;
            d.interaction.feedDelay = feeding ? 1.2f : 1e6f;
            var eye = d.tunnel.eyePosition;
            eye.y = eyeHeight;
            d.tunnel.eyePosition = eye;
            d.display.controlPanelPreviews = previews;
        }
    }
}
