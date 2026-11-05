using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class ShaderBundleLoader : ShaderBundleLoaderBase<Shader>
    {
        // coverage: only read by LoadAssets (live bundle reload)
        [ExcludeFromCodeCoverage]
        protected override string BundleType
        {
            get { return "shaders";  }
        }

        public ShaderBundleLoader()
        {
        }
    }
}
