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
                PosColor = new List<int>(numVerts);
                TextureCoordinates = new List<int>(numVerts);
                MaterialIndex = 0;
            }

            public List<int> PosColor { get; }
            public List<int> TextureCoordinates { get; }
            public int MaterialIndex { get; set; }

            public int IndexOffset => PosColor.Min();
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
            float v = mVerts[(int)vertexIndices[vertIdx]].v * mTextures[textureIdx].textureHRatio * -1.0f;

            return new Vector2D(u, v);
        }

        private void SetPreprocessIndices(short v1, short v2, short v3, int curTextureIdx, uint[] vertexIndices, List<Vector2D> calculatedUVs, PreprocessMesh mesh)
        {
            calculatedUVs.Add(ExtractUV(v1, curTextureIdx, vertexIndices));
            calculatedUVs.Add(ExtractUV(v2, curTextureIdx, vertexIndices));
            calculatedUVs.Add(ExtractUV(v3, curTextureIdx, vertexIndices));
            mesh.TextureCoordinates.Add(calculatedUVs.Count - 3);
            mesh.TextureCoordinates.Add(calculatedUVs.Count - 2);
            mesh.TextureCoordinates.Add(calculatedUVs.Count - 1);

            mesh.PosColor.Add((int)vertexIndices[v1]);
            mesh.PosColor.Add((int)vertexIndices[v2]);
            mesh.PosColor.Add((int)vertexIndices[v3]);
        }

        public bool ParseModel()
        {
            mScene = new Scene();
            mScene.RootNode = new Node("Root");

            Material nullMaterial = new Material();
            nullMaterial.Name = "NullMaterial";
            mScene.Materials.Add(nullMaterial);
            const int NullMaterialIdx = 0;

            for (int i = 0; i < mTextures.Length; ++i)
            {
                Material newMaterial = new Material();

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
                newMaterial.TextureDiffuse = newTextureSlot;
                newMaterial.Name = "Material_" + i.ToString("D4");

                mScene.Materials.Add(newMaterial);
            }

            bool useNullMaterial = true;
            bool newTexture = false;
            int curTextureIdx = 0;
            float sScale = 0.0f;
            float tScale = 0.0f;
            uint[] vertexIndices = new uint[32];

            PreprocessMesh curMesh = null;
            List<PreprocessMesh> preprocessMeshes = new List<PreprocessMesh>();

            List<Vector2D> CalculatedUVs = new List<Vector2D>(mVerts.Length);

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
                        }

                        preprocessMeshes.Add(new PreprocessMesh(mVerts.Length));
                        curMesh = preprocessMeshes[preprocessMeshes.Count - 1];
                        curMesh.MaterialIndex = curTextureIdx + 1;
                        newTexture = false;
                    }
                    
                    if (useNullMaterial)
                    {
                        preprocessMeshes.Add(new PreprocessMesh(mVerts.Length));
                        curMesh = preprocessMeshes[preprocessMeshes.Count - 1];
                        curMesh.MaterialIndex = NullMaterialIdx;
                        useNullMaterial = false;
                    }
                }
                else if (curCommandId == Commands.TriangleFace)
                {
                    short v1 = (short) ((int) command[5] / 2);
                    short v2 = (short) ((int) command[6] / 2);
                    short v3 = (short) ((int) command[7] / 2);

                    SetPreprocessIndices(v1, v2, v3, curTextureIdx, vertexIndices, CalculatedUVs, curMesh);
                }
                else if (curCommandId == Commands.QuadFace)
                {
                    short v1 = (short) ((int) command[1] / 2);
                    short v2 = (short) ((int) command[2] / 2);
                    short v3 = (short) ((int) command[3] / 2);
                    SetPreprocessIndices(v1, v2, v3, curTextureIdx, vertexIndices, CalculatedUVs, curMesh);

                    v1 = (short) ((int) command[5] / 2);
                    v2 = (short) ((int) command[6] / 2);
                    v3 = (short) ((int) command[7] / 2);
                    SetPreprocessIndices(v1, v2, v3, curTextureIdx, vertexIndices, CalculatedUVs, curMesh);
                }
            }
            
            foreach (PreprocessMesh ppMesh in preprocessMeshes)
            {
                Mesh newMesh = new Mesh("", PrimitiveType.Triangle);
                newMesh.MaterialIndex = ppMesh.MaterialIndex;
                
                for (int i = 0; i < ppMesh.PosColor.Count; i += 3)
                {
                    int[] faceIndices = new int[3];
                    for (int j = 0; j < 3; ++j)
                    {
                        int vertIndex = i + j;

                        int PosColorIndex = ppMesh.PosColor[vertIndex];

                        F3DEX_VERT vert = mVerts[PosColorIndex];
                        int normalizedMeshIndex = PosColorIndex - ppMesh.IndexOffset;

                        if (normalizedMeshIndex >= newMesh.Vertices.Count)
                        {
                            newMesh.Vertices.Add(new Vector3D(vert.x, vert.y, vert.z));
                            newMesh.VertexColorChannels[0].Add(new Color4D(vert.r, vert.g, vert.b, vert.a));

                            Vector2D uv = CalculatedUVs[ppMesh.TextureCoordinates[vertIndex]];
                            newMesh.TextureCoordinateChannels[0].Add(new Vector3D(uv.X, uv.Y, 0));
                        }

                        faceIndices[j] = normalizedMeshIndex;
                    }

                    newMesh.Faces.Add(new Assimp.Face(faceIndices));
                }

                mScene.Meshes.Add(newMesh);
                mScene.RootNode.MeshIndices.Add(mScene.RootNode.MeshCount);
            }

            return true;
        }

        public void Export(string outFileName)
        {
            //TODO(KL): resolve textures in material and write textures to disk
            
            //TODO(KL): Add animations if there are any
            
            String ext = Path.GetExtension(outFileName);
            AssimpContext context = new AssimpContext();
            context.ExportFile(mScene, outFileName, ext.TrimStart('.'));
        }
    }
}
