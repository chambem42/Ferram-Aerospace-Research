using System;
using FerramAerospaceResearch.Config;
using Unity.Mathematics;
using UnityEngine;

namespace FerramAerospaceResearch.Resources
{
    [Serializable]
    public struct Kernel
    {
        public string name;
        public int index;
        public uint3 threadGroupSizes;

        public bool IsValid
        {
            get { return index >= 0 && !string.IsNullOrEmpty(name); }
        }

        public Kernel(ComputeShader shader, string name)
        {
            this.name = name;
            index = shader.FindKernel(name);
            if (index < 0)
                // kernel not found
                threadGroupSizes = default;
            else
                shader.GetKernelThreadGroupSizes(index,
                                                 out threadGroupSizes.x,
                                                 out threadGroupSizes.y,
                                                 out threadGroupSizes.z);
        }
    }

    public class FARComputeShaderCache : FARAssetDictionary<ComputeShader>
    {
        public readonly ComputeShaderAssetRequest CountPixels;

        public FARComputeShaderCache()
        {
            CountPixels = MakeAsset(FARConfig.Shaders.CountColors, "countPixels");
        }

        private ComputeShaderAssetRequest MakeAsset(ComputeShaderNode node, string name)
        {
            var asset = new ComputeShaderAssetRequest
            {
                AssetLoaders = FARAssets.Instance.Loaders.ComputeShaders,
                Key = name,
                Node = node
            };
            SetupAsset(asset);
            return asset;
        }

        public class ComputeShaderAssetRequest : LoadableAsset<ComputeShader>
        {
            public Kernel Kernel { get; private set; }

            /// <inheritdoc />
            protected override void AssetLoaded()
            {
                base.AssetLoaded();
                if (Node is not ComputeShaderNode node)
                    return;
                Kernel = new Kernel(Asset, node.Kernel);
            }
        }
    }
}
