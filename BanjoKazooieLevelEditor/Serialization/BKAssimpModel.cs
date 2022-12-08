using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Assimp;

namespace BanjoKazooieLevelEditor.Serialization
{
    internal class BKAssimpModel
    {
        private class Commands
        {
            public static readonly byte VertexIndices = 4;
            public static readonly byte TriangleFace = 191;
            public static readonly byte QuadFace = 177;
            public static readonly byte GroupStart = 6;
            public static readonly byte EmptyMaterialStart = 182;
            public static readonly byte NewTexture = 186;
            public static readonly byte TextureScale = 187;
            public static readonly byte TextureStart = 253;
            public static readonly byte TextureColorPalette = 240;
            public static readonly byte TextureFormatAndSize = 245;
        }

        private class PreprocessMesh
        {
            public PreprocessMesh(int numVerts)
            {
                Positions = new List<int>(numVerts);
                Colors = new List<int>(numVerts);
                TextureCoordinates = new List<int>(numVerts);
                MaterialIndex = 0;
            }

            public List<int> Positions { get; }
            public List<int> Colors { get; }
            public List<int> TextureCoordinates { get; }
            public int MaterialIndex { get; set; }
        }

        private Scene mScene = new Scene();
        private byte[] mBinaryModelData;
        private int mModelId;
        private F3DEX mF3dEx = new F3DEX();

        private bool mModelSuccessfullyLoaded = false;

        private F3DEX_VERT[] mVerts;
        private List<byte[]> mCommands = new List<byte[]>();
        private Texture[] mTextures;


        public bool IsModelLoaded => mModelSuccessfullyLoaded;

        public BKAssimpModel(string binaryModelDir, int modelId)
        {
            mModelId = modelId;

            BinaryReader binaryReader = new BinaryReader(File.Open(binaryModelDir + modelId.ToString("x"), FileMode.Open));

            long streamLength = binaryReader.BaseStream.Length;
            mBinaryModelData = new byte[streamLength];
            binaryReader.BaseStream.Read(mBinaryModelData, 0, (int) streamLength);
            binaryReader.Close();

            int collStart = 0;
            int f3dStart = 0;
            int f3dEnd = 0;
            int numCommands = 0;
            int vertStart = 0;
            int vertexCount = 0;
            int textureCount = 0;
            F3DEX_VERT[] verts = null;
            Texture[] textures = null;
            mModelSuccessfullyLoaded = mF3dEx.ReadModel(ref mBinaryModelData,
                ref collStart,
                ref f3dStart,
                ref numCommands,
                ref f3dEnd,
                ref vertStart,
                ref vertexCount,
                ref textureCount,
                ref mVerts,
                ref mCommands,
                ref mTextures);
        }

        private Vector2D ExtractUV(short vertIdx, int textureIdx, uint[] vertexIndices)
        {
            float u = mVerts[(int)vertexIndices[vertIdx]].u * mTextures[textureIdx].textureWRatio;
            float v = mVerts[(int) vertexIndices[vertIdx]].v * mTextures[textureIdx].textureHRatio * -1.0f;

            return new Vector2D(u, v);
        }

        public bool ParseModel()
        {
            mScene = new Scene();
            mScene.RootNode = new Node("Root");

            Material nullMaterial = new Material();
            nullMaterial.Name = "NullMaterial";
            mScene.Materials.Add(nullMaterial);
            const int NullMaterialIdx = 0;

            //Mesh curMesh = null;
            Material curMaterial = null;

            bool useNullMaterial = true;
            bool newTexture = false;
            int curTextureIdx = 0;
            float sScale = 0.0f;
            float tScale = 0.0f;
            int curVertexUVIndex = 1;
            uint[] vertexIndices = new uint[32];

            PreprocessMesh curMesh = null;
            List<PreprocessMesh> preprocessMeshes = new List<PreprocessMesh>();

            List<Vector2D> TextureCoordinates = new List<Vector2D>(mVerts.Length);

            for (int cmdIdx = 0; cmdIdx < mCommands.Count; ++cmdIdx)
            {
                byte[] command = mCommands[cmdIdx];
                byte curCommandId = command[0];

                // Not sure if this name is correct, but it will have to do for now
                uint cmdWidth = (uint) ((int) command[4] * 16777216 + (int) command[5] * 65536 + (int) command[6] * 256) + (uint) command[7];

                if (curCommandId == Commands.EmptyMaterialStart)
                {
                    newTexture = false;
                    useNullMaterial = true;
                }
                else if (curCommandId == Commands.GroupStart)
                {
                    // ignored
                }
                else if (curCommandId == Commands.TextureStart)
                {
                    mF3dEx.GL_G_SETTIMG(ref curTextureIdx,
                        mTextures.Length,
                        cmdWidth,
                        ref mTextures,
                        mCommands[cmdIdx + 2],
                        ref newTexture,
                        sScale,
                        tScale);

                    newTexture = true;
                    useNullMaterial = false;
                }
                else if (curCommandId == Commands.TextureFormatAndSize)
                {
                    mF3dEx.GL_G_SETTILE(command, ref mTextures[curTextureIdx]);
                }
                else if (curCommandId == Commands.TextureColorPalette)
                {
                    int palSize = (int) ((cmdWidth << 8 >> 8 & 16773120U) >> 14) * 2 + 2;
                    mTextures[curTextureIdx].loadPalette(mBinaryModelData, mTextures.Length, palSize);
                    if (mCommands[cmdIdx + 4][0] == Commands.NewTexture)
                    {
                        newTexture = true;
                    }
                }
                else if (curCommandId == Commands.TextureScale)
                {
                    sScale = (float) (cmdWidth >> 16) / 65536f;
                    tScale = (float) (cmdWidth & (uint) ushort.MaxValue) / 65536f;
                }
                else if (curCommandId == Commands.VertexIndices)
                {
                    int encodedIndex = (int) command[4] * 16777216 + (int) command[5] * 65536 + (int) command[6] * 256 + (int) command[7];
                    byte indexStart = (byte) ((uint) command[1] >> 1);
                    byte numIndices = (byte) ((uint) command[2] >> 2);
                    if (indexStart > (byte)63)
                    {
                        indexStart = (byte)63;
                    }

                    uint vertexIndex = ((uint) (encodedIndex << 8) >> 8) / 16U;
                    try
                    {
                        for (int i = (int) indexStart; i < (int) numIndices + (int) indexStart; ++i)
                        {
                            if ((long)vertexIndex < (long)mVerts.Length)
                            {
                                vertexIndices[i] = vertexIndex;
                            }

                            ++vertexIndex;
                        }
                    }
                    catch (Exception ex)
                    {
                        // ignored
                    }

                    if (newTexture)
                    {
                        Texture curTexture = mTextures[curTextureIdx];

                        if (curTexture.pixels == null)
                        {
                            int textureDataOffset = mTextures.Length * 16;
                            mF3dEx.ExportTexture(ref mBinaryModelData, ref curTexture, textureDataOffset);
                            //TODO(KL): Write the textures during export
                            //GEOBJ.writeTexture(outDir + "image_" + currentTexture.ToString("D4") + ".png", textures[currentTexture].pixels, textures[currentTexture].textureWidth, textures[currentTexture].textureHeight);
                            //mtl = mtl + "newmtl material_" + currentTexture.ToString("D4") + Environment.NewLine + "map_Kd image_" + currentTexture.ToString("D4") + ".png" + Environment.NewLine + Environment.NewLine;

                            curMaterial = new Material();

                            TextureSlot newTextureSlot = new TextureSlot(
                                "TODO",
                                TextureType.Diffuse,
                                0,
                                TextureMapping.FromUV,
                                0,
                                0.0f,
                                TextureOperation.Add,
                                mF3dEx.cms == 0 ? TextureWrapMode.Wrap : TextureWrapMode.Clamp,
                                mF3dEx.cmt == 0 ? TextureWrapMode.Wrap : TextureWrapMode.Clamp,
                                0);
                            curMaterial.TextureDiffuse = newTextureSlot;
                            curMaterial.Name = "Material_" + curTextureIdx.ToString("D4");

                            mScene.Materials.Add(curMaterial);
                        }
                        //obj = obj + "usemtl material_" + currentTexture.ToString("D4") + Environment.NewLine;
                        //curMesh = new Mesh("Mesh_" + curTextureIdx.ToString("D4"));
                        preprocessMeshes.Add(new PreprocessMesh(mVerts.Length));
                        curMesh = preprocessMeshes[preprocessMeshes.Count - 1];
                        curMesh.MaterialIndex = mScene.MaterialCount - 1;
                        newTexture = false;
                    }
                    else if (useNullMaterial)
                    {
                        Debug.Assert(curMesh != null);
                        curMesh.MaterialIndex = NullMaterialIdx;
                        useNullMaterial = false;
                    }
                }
                else if (curCommandId == Commands.TriangleFace)
                {
                    //TODO(KL): Set triangle vertex pos and UV idices here

                    short v1 = (short) ((int) command[5] / 2); //num4
                    short v2 = (short) ((int) command[6] / 2); //num5
                    short v3 = (short) ((int) command[7] / 2); //num6

                    TextureCoordinates.Add(ExtractUV(v1, curTextureIdx, vertexIndices));
                    TextureCoordinates.Add(ExtractUV(v2, curTextureIdx, vertexIndices));
                    TextureCoordinates.Add(ExtractUV(v3, curTextureIdx, vertexIndices));
                    curMesh.TextureCoordinates.Add(TextureCoordinates.Count - 3);
                    curMesh.TextureCoordinates.Add(TextureCoordinates.Count - 2);
                    curMesh.TextureCoordinates.Add(TextureCoordinates.Count - 1);

                    //TODO(KL): add vertex positions and colors

                    //obj = obj + "vt " + (object) ((float) verts[(int) numArray1[(int) num4]].u * textures[currentTexture].textureWRatio) + " " + (object) (float) ((double) verts[(int) numArray1[(int) num4]].v * (double) textures[currentTexture].textureHRatio * -1.0) + Environment.NewLine;
                    //obj = obj + "vt " + (object) ((float) verts[(int) numArray1[(int) num5]].u * textures[currentTexture].textureWRatio) + " " + (object) (float) ((double) verts[(int) numArray1[(int) num5]].v * (double) textures[currentTexture].textureHRatio * -1.0) + Environment.NewLine;
                    //obj = obj + "vt " + (object) ((float) verts[(int) numArray1[(int) num6]].u * textures[currentTexture].textureWRatio) + " " + (object) (float) ((double) verts[(int) numArray1[(int) num6]].v * (double) textures[currentTexture].textureHRatio * -1.0) + Environment.NewLine;
                    //obj += GEOBJ.convertFace(numArray1[(int) num4] + 1U, numArray1[(int) num5] + 1U, numArray1[(int) num6] + 1U, vtIndex1);
                    curVertexUVIndex += 3;

                }
                else if (curCommandId == Commands.QuadFace)
                {
                    //TODO(KL): Set quad vertex pos and UV indices here
                }
            }


            //TODO(KL): Foreach mesh: resolve vertices

            //Mesh newMesh = new Mesh("", PrimitiveType.Triangle);
            //newMesh.Vertices.Capacity = mVerts.Count;
            //newMesh.TextureCoordinateChannels[0].Capacity = mVerts.Count;
            //newMesh.VertexColorChannels[0].Capacity = mVerts.Count;
            //
            //foreach (F3DEX_VERT f3dExVert in mVerts)
            //{
            //    newMesh.Vertices.Add(new Vector3D(f3dExVert.x, f3dExVert.y, f3dExVert.z));
            //    newMesh.TextureCoordinateChannels[0].Add(new Vector3D(f3dExVert.u, f3dExVert.v, 0));
            //    newMesh.VertexColorChannels[0].Add(new Color4D(f3dExVert.r, f3dExVert.g, f3dExVert.b, f3dExVert.a));
            //}

            //loop over commands
            // create Meshes on the fly
            // create Materials on the fly


            return true;
        }

        public void Export(string outFileName)
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
            scene.Materials.Add(mat);

            //TODO(KL): Add animations if there are any

            AssimpContext context = new AssimpContext();
            context.ExportFile(scene, outFileName, "obj");
        }
    }
}
