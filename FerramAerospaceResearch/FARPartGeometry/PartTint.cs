using System.Collections;
using System.Collections.Generic;
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

        public Dictionary<Part, Color>.Enumerator GetEnumerator()
        {
            return tints.GetEnumerator();
        }

        IEnumerator<KeyValuePair<Part, Color>> IEnumerable<KeyValuePair<Part, Color>>.GetEnumerator()
        {
            return tints.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return tints.GetEnumerator();
        }
    }
}
