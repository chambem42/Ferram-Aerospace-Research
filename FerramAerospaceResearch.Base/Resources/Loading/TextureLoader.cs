using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace FerramAerospaceResearch.Resources.Loading
{
    public class TextureLoader : IAssetLoader<Texture2D>
    {
        // cache loaded textures
        private readonly Dictionary<string, Texture2D> loadedTextures = new Dictionary<string, Texture2D>();

        public IEnumerator Load(IAssetRequest<Texture2D> assetRequest)
        {
            assetRequest.State = Progress.InProgress;

            if (loadedTextures.TryGetValue(assetRequest.Url, out Texture2D texture))
            {
                FARLogger.DebugFormat("Using already loaded texture {0}", assetRequest.Url);
                assetRequest.State = Progress.Completed;
                assetRequest.OnLoad(texture);
                yield break;
            }

            string path = $@"file:///{PathUtil.Combine(PathUtil.ParentDir, assetRequest.Url)}";
            FARLogger.DebugFormat("Loading texture from {0}", path);
            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(path);
            yield return request.SendWebRequest();

            if (!string.IsNullOrEmpty(request.error))
            {
                FARLogger.Error($"Error loading texture from {request.url}: {request.error}");
                assetRequest.State = Progress.Error;
                assetRequest.OnError();
            }
            else
            {
                FARLogger.DebugFormat("Texture loaded from from {0}", request.url);
                Texture2D content = DownloadHandlerTexture.GetContent(request);
                loadedTextures.Add(assetRequest.Url, content);
                assetRequest.State = Progress.Completed;
                assetRequest.OnLoad(content);
            }
        }

        public string Name { get; } = "default";
    }
}
