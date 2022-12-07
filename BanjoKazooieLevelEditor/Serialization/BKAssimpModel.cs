using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Assimp;
using Assimp.Unmanaged;
using Collada141;

namespace BanjoKazooieLevelEditor.Serialization
{
    internal class BKAssimpModel
    {
        public static void Export(string outFileName, string binaryModelDir, int modelId)
        {
            AssimpContext exporter = new AssimpContext();

            Scene scene = new Scene();
            scene.RootNode = new Node("Root");

            Mesh triangle = new Mesh("", PrimitiveType.Triangle);
            triangle.Vertices.Add(new Vector3D(1, 0, 0));
            triangle.Vertices.Add(new Vector3D(5, 5, 0));
            triangle.Vertices.Add(new Vector3D(10, 0, 0));
            triangle.Faces.Add(new Assimp.Face(new int[] { 0, 1, 2 }));
            triangle.MaterialIndex = 0;

            scene.Meshes.Add(triangle);
            scene.RootNode.MeshIndices.Add(0);

            //TODO(KL): Fill in Material object
            Material mat = new Material();
            mat.Name = "MyMaterial";
            //mat.TextureDiffuse = TextureSlot
            scene.Materials.Add(mat);

            //TODO(KL): Add animations if there are any

            AssimpContext context = new AssimpContext();
            context.ExportFile(scene, outFileName, "obj");
        }
    }
}
