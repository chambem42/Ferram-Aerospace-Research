using System.Collections.Generic;
using FerramAerospaceResearch.Reflection;
using UnityEngine;

// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace FerramAerospaceResearch.Config
{
    [ConfigNode("ColorMap")]
    public class ColorMap
    {
        [ConfigValueIgnore] public static readonly ColorMap Default = new()
        {
            Name = "default",
            Colors = {new Color(0.18f, 0f, 0.106f)}
        };

        [ConfigValue("name")] public string Name { get; set; }

        [ConfigValue("color")] public List<Color> Colors { get; } = new();

        public Color this[int index]
        {
            get { return Colors[index]; }
            set { Colors[index] = value; }
        }

        public Color Get(int index)
        {
            return Colors[index % Colors.Count];
        }
    }

    [ConfigNode("Voxelization", shouldSave: false)]
    public class VoxelizationConfig
    {
        [ConfigValue("default")] public string Default { get; set; }

        [ConfigValue] public List<ColorMap> ColorMaps { get; } = new();

        [ConfigValue("debugInFlight")] public bool DebugInFlight { get; set; } = false;

        public ColorMap FirstOrDefault()
        {
            return FirstOrDefault(Default);
        }

        public ColorMap FirstOrDefault(string name)
        {
            if (string.IsNullOrEmpty(name))
                return ColorMap.Default;
            foreach (ColorMap map in ColorMaps)
            {
                if (map.Name == name)
                    return map;
            }

            return ColorMap.Default;
        }

        public Texture2D ColorMapTexture(string name)
        {
            ColorMap map = FirstOrDefault(name);
            var tex = new Texture2D(map.Colors.Count, 1) { filterMode = FilterMode.Point };
            tex.SetPixels(map.Colors.ToArray());
            tex.Apply();
            return tex;
        }

        public Texture2D ColorMapTexture()
        {
            return ColorMapTexture(Default);
        }
    }
}
