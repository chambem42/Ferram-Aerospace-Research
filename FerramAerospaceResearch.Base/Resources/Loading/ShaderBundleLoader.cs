using UnityEngine;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class ShaderBundleLoader : ShaderBundleLoaderBase<Shader>
    {
        protected override string BundleType
        {
            get { return "shaders";  }
        }

        public ShaderBundleLoader()
        {
        }
    }
}
