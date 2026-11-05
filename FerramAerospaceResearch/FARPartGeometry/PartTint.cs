using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FerramAerospaceResearch.Config;
using UnityEngine;

namespace FerramAerospaceResearch.FARPartGeometry
{
    public class PartTint : IEnumerable<KeyValuePair<Part, Color>>
    {
        private readonly ColorMap map;
        private int count;

        private readonly Dictionary<Part, Color> tints = new(ObjectReferenceEqualityComparer<Part>.Default);

        public PartTint() : this(FARConfig.Voxelization.Default)
        {
        }

        public PartTint(string mapName)
        {
            map = FARConfig.Voxelization.FirstOrDefault(mapName);
        }

        // coverage: indexer FAR does not use
        [ExcludeFromCodeCoverage]
        public Color this[Part part]
        {
            get { return tints[part]; }
        }

        public Color GetOrAdd(Part part)
        {
            return tints.TryGetValue(part, out Color tint) ? tint : Add(part);
        }

        public Color Add(Part part)
        {
            Color tint = map.Get(count++);
            tints.Add(part, tint);
            return tint;
        }

        // coverage: only the Debug-level tint log enumerates it
        [ExcludeFromCodeCoverage]
        public Dictionary<Part, Color>.Enumerator GetEnumerator()
        {
            return tints.GetEnumerator();
        }

        // coverage: explicit interface member nothing calls
        [ExcludeFromCodeCoverage]
        IEnumerator<KeyValuePair<Part, Color>> IEnumerable<KeyValuePair<Part, Color>>.GetEnumerator()
        {
            return tints.GetEnumerator();
        }

        // coverage: explicit interface member nothing calls
        [ExcludeFromCodeCoverage]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return tints.GetEnumerator();
        }
    }
}
