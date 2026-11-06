using System.Collections.Generic;
using FerramAerospaceResearch.Reflection;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable CollectionNeverUpdated.Global

namespace FerramAerospaceResearch.Settings
{
    [ConfigNode("FARAnimOverride", true, true, false)]
    public class FARAnimOverrides : Singleton<FARAnimOverrides>, Interfaces.IConfigNode
    {
        [ConfigValue]
        private static readonly List<Override> overrideList = new List<Override>();

        [ConfigValueIgnore]
        public static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>();

        public void BeforeLoaded()
        {
            overrideList.Clear();
        }

        public void AfterLoaded()
        {
            if (overrideList.Count == 0)
                return;

            Overrides.Clear();
            foreach (Override item in overrideList)
                if (!string.IsNullOrEmpty(item.ModuleName))
                    Overrides.Add(item.ModuleName, item.FieldName);
        }

        public void BeforeSaved()
        {
            overrideList.Clear();

            foreach (KeyValuePair<string, string> pair in Overrides)
            {
                overrideList.Add(new Override
                {
                    ModuleName = pair.Key,
                    FieldName = pair.Value
                });
            }
        }

        public void AfterSaved()
        {
        }

        [ConfigNode("FARAnimOverride")]
        public struct Override
        {
            [ConfigValue("moduleName")]
            public string ModuleName;

            [ConfigValue("animNameField")]
            public string FieldName;
        }

        public static string FieldNameForModule(string moduleName)
        {
            return Overrides.TryGetValue(moduleName, out string v) ? v : null;
        }
    }
}
