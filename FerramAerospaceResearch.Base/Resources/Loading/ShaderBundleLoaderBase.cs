using System;
using System.Diagnostics.CodeAnalysis;
using FerramAerospaceResearch.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FerramAerospaceResearch.Resources.Loading
{
    public abstract class ShaderBundleLoaderBase<T> : AssetBundleLoader<T> where T: Object
    {
        protected abstract string BundleType { get; }
        private readonly Observable<string> pathValue;

        protected ShaderBundleLoaderBase()
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (Application.platform)
            {
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.WindowsPlayer
                    when SystemInfo.graphicsDeviceVersion.StartsWith("OpenGL", StringComparison.Ordinal):
                    FARLogger.Info("Shaders will be loaded from Linux bundle");
                    //For OpenGL users on Windows we load the Linux shaders to fix OpenGL issues
                    pathValue = FARConfig.Shaders.BundleLinux;
                    break;
                case RuntimePlatform.WindowsPlayer:
                    FARLogger.Info("Shaders will be loaded from Windows bundle");
                    pathValue = FARConfig.Shaders.BundleWindows;
                    break;
                case RuntimePlatform.OSXPlayer:
                    FARLogger.Info("Shaders will be loaded from MacOSX bundle");
                    pathValue = FARConfig.Shaders.BundleMac;
                    break;
                default:
                    // Should never reach this
                    FARLogger.Error($"Invalid runtime platform {Application.platform}");
                    throw new PlatformNotSupportedException();
            }

            Url = pathValue.Value;
            pathValue.OnValueChanged += UpdateUrl;
        }

        // coverage: live reload when the bundle path changes at runtime; the bundle paths are fixed per platform
        [ExcludeFromCodeCoverage]
        private void UpdateUrl(string url)
        {
            Url = url;
            LoadAssets();
        }

        // coverage: only reached from UpdateUrl (live bundle reload)
        [ExcludeFromCodeCoverage]
        public void LoadAssets()
        {
            FARLogger.InfoFormat("Loading {0} bundle", BundleType);
            MainThread.StartCoroutine(Load);
        }

        public void Unsubscribe()
        {
            pathValue.OnValueChanged -= UpdateUrl;
        }
    }
}
