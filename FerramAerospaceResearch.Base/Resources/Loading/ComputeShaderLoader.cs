using System.Collections;
using UnityEngine;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class ComputeShaderLoader : IAssetLoader<ComputeShader>
    {
        public IEnumerator Load(IAssetRequest<ComputeShader> assetRequest)
        {
            assetRequest.State = Progress.InProgress;

            ComputeShader shader = UnityEngine.Resources.Load<ComputeShader>(assetRequest.Url);

            if (shader == null)
            {
                FARLogger.DebugFormat("Could not find compute shader {0}", assetRequest.Url);
                assetRequest.State = Progress.Error;
                assetRequest.OnError();
            }
            else
            {
                FARLogger.DebugFormat("Found compute shader {0}", assetRequest.Url);
                assetRequest.State = Progress.Completed;
                assetRequest.OnLoad(shader);
            }

            yield break;
        }

        public string Name { get; } = "default";
    }
}
