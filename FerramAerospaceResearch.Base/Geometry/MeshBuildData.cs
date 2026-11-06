using System.Collections.Generic;
using UnityEngine;

namespace FerramAerospaceResearch.Geometry
{
    public class MeshBuildData
    {
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<List<Vector2>> Uvs = new List<List<Vector2>>();
        public readonly List<List<int>> SubmeshIndices = new List<List<int>>();

        public int CurrentSubmesh;

        public List<int> Indices
        {
            get { return SubmeshIndices[CurrentSubmesh]; }
            set { SubmeshIndices[CurrentSubmesh] = value; }
        }
    }
}
