using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class ComputeShaderBundleLoader : ShaderBundleLoaderBase<ComputeShader>
    {
        // coverage: only read by LoadAssets (live bundle reload)
        [ExcludeFromCodeCoverage]
        protected override string BundleType
        {
            get { return "compute shaders";  }
        }

        public ComputeShaderBundleLoader()
        {
        }
    }
}
