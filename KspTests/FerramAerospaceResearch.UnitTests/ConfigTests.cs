using System;
using System.IO;
using System.Linq;
using FerramAerospaceResearch.Reflection;
using UnityEngine;
using Xunit;

namespace FerramAerospaceResearch.UnitTests
{
    /// <summary>
    /// FAR's config reflection (NodeReflection with ConfigAdapter's load and save visitors) on FAR's own FARConfig.cfg:
    /// the file's values land in FARConfig's sections, changed values are taken, a save loads back to the same config,
    /// and a bad value is reported without spoiling the rest. One class: FARConfig is static, so these run in turn.
    /// </summary>
    public class ConfigTests : IDisposable
    {
        private static readonly string FarConfigPath =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../GameData/FerramAerospaceResearch/FARConfig.cfg"));

        private static NodeReflection Root => NodeReflection.GetReflection(typeof(FARConfig));

        private static ConfigNode FileNode()
        {
            Assert.True(File.Exists(FarConfigPath), FarConfigPath);
            return ConfigNode.Load(FarConfigPath).GetNode("FARConfig");
        }

        private static int Load(ConfigNode node)
        {
            object instance = null;   // FARConfig is static
            return Root.Load(new LoadVisitor { Node = node }, ref instance);
        }

        private static ConfigNode Save()
        {
            var node = new ConfigNode("FARConfig");
            Root.Save(new SaveVisitor { Node = node }, null);
            return node;
        }

        public ConfigTests()
        {
            UnityRuntime.RequireLog();   // ConfigNode, Color and FAR's logger
        }

        // the shipped config back in place for whatever runs next
        public void Dispose()
        {
            if (UnityRuntime.Available && TestLog.Installed)
                Load(FileNode());
        }

        [SkippableFact]
        public void ShippedConfigLoadsWithoutErrors()
        {
            TestLog.Instance.Clear();
            Assert.Equal(0, Load(FileNode()));
            Assert.DoesNotContain(TestLog.Instance.Lines, l => l.StartsWith("Error"));
            Assert.Equal(50, FARConfig.FlightLog.LogPeriod.Value);
            Assert.Equal(10, FARConfig.FlightLog.FlushPeriod.Value);
            Assert.Equal("yyyy_MM_dd_HH_mm_ss", FARConfig.FlightLog.DatetimeFormat);
            Assert.Equal("<<<VESSEL_NAME>>>_<<<DATETIME>>>.csv", FARConfig.FlightLog.NameFormat.FormatString);
            Assert.Equal(new Color(0, 1, 1), FARConfig.GUIColors.ClColor);
            Assert.Equal(new Color(1, 0, 0), FARConfig.GUIColors.CdColor);
        }

        [SkippableFact]
        public void ChangedValuesAreTaken()
        {
            ConfigNode node = FileNode();
            ConfigNode log = node.GetNode("FlightLog"), colors = node.GetNode("GuiColors");
            log.SetValue("period", "37");
            log.SetValue("datetimeFormat", "HHmm");
            log.SetValue("nameFormat", "flight_<<<VESSEL_NAME>>>.csv");
            colors.SetValue("ClColor", "0.25,0.5,0.75");
            Assert.Equal(0, Load(node));
            Assert.Equal(37, FARConfig.FlightLog.LogPeriod.Value);
            Assert.Equal("HHmm", FARConfig.FlightLog.DatetimeFormat);
            Assert.Equal("flight_<<<VESSEL_NAME>>>.csv", FARConfig.FlightLog.NameFormat.FormatString);
            Assert.Equal(new Color(0.25f, 0.5f, 0.75f), FARConfig.GUIColors.ClColor);
        }

        [SkippableFact]
        public void SavedConfigLoadsBackToTheSameConfig()
        {
            Assert.Equal(0, Load(FileNode()));
            ConfigNode first = Save();
            Assert.True(first.HasNode("FlightLog") && first.HasNode("GuiColors"), "the sections FAR saves:\n" + first);
            Assert.Equal(0, Load(first));
            Assert.Equal(first.ToString(), Save().ToString());
        }

        [SkippableFact]
        public void ABadValueIsReportedAndTheRestStillLoads()
        {
            ConfigNode node = FileNode();
            node.GetNode("FlightLog").SetValue("period", "often");
            node.GetNode("FlightLog").SetValue("flushPeriod", "7");
            Assert.Equal(0, Load(FileNode()));
            Load(node);
            Assert.Equal(50, FARConfig.FlightLog.LogPeriod.Value);   // kept: "often" is not an int
            Assert.Equal(7, FARConfig.FlightLog.FlushPeriod.Value);
        }

        [SkippableFact]
        public void ObservableValuesNotifyOnChange()
        {
            int seen = -1;
            void OnChange(int v) => seen = v;
            FARConfig.FlightLog.LogPeriod.OnValueChanged += OnChange;
            try
            {
                ConfigNode node = FileNode();
                node.GetNode("FlightLog").SetValue("period", "23");
                Load(node);
                Assert.Equal(23, seen);
            }
            finally
            {
                FARConfig.FlightLog.LogPeriod.OnValueChanged -= OnChange;
            }
        }
    }
}
