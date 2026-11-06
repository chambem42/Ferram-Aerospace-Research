using UnityEngine;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class ComputeShaderBundleLoader : ShaderBundleLoaderBase<ComputeShader>
    {
        protected override string BundleType
        {
            get { return "compute shaders";  }
        }

        public ComputeShaderBundleLoader()
        {
        }
    }
}
