using FerramAerospaceResearch.Reflection;

namespace FerramAerospaceResearch.Config
{
    public class ResourceNode
    {
        public ResourceNode(string url = "", string loader = "default")
        {
            Url.Value = url;
            Loader.Value = loader;
        }

        [ConfigValue("url")]
        public Observable<string> Url { get; } = new Observable<string>();

        [ConfigValue("loader")]
        public Observable<string> Loader { get; } = new Observable<string>("default");

        public static implicit operator string(ResourceNode node)
        {
            return node.Url;
        }
    }

    [ConfigNode("TEXTURE")]
    public class TextureNode : ResourceNode
    {
        public TextureNode(string url = "", string loader = "default") : base(url, loader)
        {
        }
    }

    [ConfigNode("SHADER")]
    public class ShaderNode : ResourceNode
    {
        public ShaderNode(string url = "", string loader = "default") : base(url, loader)
        {
        }
    }

    [ConfigNode("SHADER")]
    public class DebugVoxelNode : ShaderNode
    {
        public DebugVoxelNode(string url = "", string loader = "default") : base(url, loader)
        {
        }

        [ConfigValue("_Cutoff")]
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global - reflection
        public float Cutoff { get; set; } = 0.45f;
    }

    [ConfigNode("COMPUTE_SHADER")]
    public class ComputeShaderNode : ResourceNode
    {
        public ComputeShaderNode(string url = "", string loader = "default") : base(url, loader)
        {
        }

        [ConfigValue("kernel")] public string Kernel { get; set; } = string.Empty;
    }
}
