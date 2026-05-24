using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Entities;

namespace VintageKinematics.Rendering
{
    public class ContraptionEntityRenderer : IRenderer
    {
        private const int White = unchecked((int)0xffffffff);

        private readonly ICoreClientAPI capi;
        private readonly EntityVKContraption entity;
        private readonly Matrixf modelMat = new Matrixf();
        private readonly Dictionary<string, MultiTextureMeshRef> meshCache = new Dictionary<string, MultiTextureMeshRef>();

        private Vec3i localMin = new Vec3i(0, 1, 0);
        private Vec3i localMax = new Vec3i(0, 1, 0);
        private Vec3i[] offsets = Array.Empty<Vec3i>();
        private Block[] blocks = Array.Empty<Block>();
        private string snapshotId;

        public double RenderOrder => 0.5;
        public int RenderRange => 64;

        public ContraptionEntityRenderer(ICoreClientAPI capi, EntityVKContraption entity)
        {
            this.capi = capi;
            this.entity = entity;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (entity == null || !entity.Alive || capi.World.Player?.Entity == null) return;

            EnsureSnapshot();
            if (offsets.Length == 0 || blocks.Length == 0) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            double width = localMax.X - localMin.X + 1;
            double depth = localMax.Z - localMin.Z + 1;
            double originLocalX = localMin.X + width / 2.0;
            double originLocalY = localMin.Y;
            double originLocalZ = localMin.Z + depth / 2.0;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(false);

            IStandardShaderProgram prog = rpi.PreparedStandardShader((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            for (int i = 0; i < offsets.Length; i++)
            {
                Block block = blocks[i];
                if (block == null || block.Id == 0) continue;

                Vec3i offset = offsets[i];
                int sourceX = (entity.ControllerPos?.X ?? 0) + offset.X;
                int sourceY = (entity.ControllerPos?.Y ?? 0) + offset.Y;
                int sourceZ = (entity.ControllerPos?.Z ?? 0) + offset.Z;
                int randomY = block.RandomizeAxes == EnumRandomizeAxes.XYZ ? sourceY : 0;
                int alternateIndex = GetAlternateIndex(block, sourceX, randomY, sourceZ);

                MultiTextureMeshRef meshRef = GetBlockMesh(block, alternateIndex, sourceX, sourceY, sourceZ);
                if (meshRef == null) continue;

                double blockX = entity.Pos.X + offset.X - originLocalX;
                double blockY = entity.Pos.InternalY + offset.Y - originLocalY;
                double blockZ = entity.Pos.Z + offset.Z - originLocalZ;

                prog.RgbaLightIn = GetNearbyLightRGBs((int)Math.Floor(blockX), (int)Math.Floor(blockY), (int)Math.Floor(blockZ));
                modelMat.Identity()
                    .Translate((float)(blockX - camPos.X), (float)(blockY - camPos.Y), (float)(blockZ - camPos.Z));
                if (block.RandomizeRotations)
                {
                    int rotIndex = GameMath.MurmurHash3Mod(sourceX, randomY, sourceZ, TesselationMetaData.randomRotations.Length);
                    float angle = TesselationMetaData.randomRotations[rotIndex] * GameMath.DEG2RAD;
                    modelMat.Translate(0.5f, 0.5f, 0.5f)
                        .RotateY(angle)
                        .Translate(-0.5f, -0.5f, -0.5f);
                }
                prog.ModelMatrix = modelMat.Values;
                rpi.RenderMultiTextureMesh(meshRef, "tex");
            }

            prog.Stop();
            rpi.GlEnableCullFace();
        }

        private void EnsureSnapshot()
        {
            string currentId = entity.SnapshotId + ":" + entity.CapturedBlockCount;
            if (snapshotId == currentId && offsets.Length == entity.CapturedBlockCount) return;

            if (!entity.TryGetSnapshot(out Vec3i min, out Vec3i max, out Vec3i[] nextOffsets, out string[] blockCodes))
            {
                offsets = Array.Empty<Vec3i>();
                blocks = Array.Empty<Block>();
                snapshotId = currentId;
                return;
            }

            localMin = min;
            localMax = max;
            offsets = nextOffsets;
            blocks = new Block[blockCodes.Length];
            for (int i = 0; i < blockCodes.Length; i++)
            {
                if (string.IsNullOrEmpty(blockCodes[i])) continue;
                blocks[i] = capi.World.GetBlock(new AssetLocation(blockCodes[i]));
            }

            snapshotId = currentId;
        }

        private MultiTextureMeshRef GetBlockMesh(Block block, int alternateIndex, int sourceX, int sourceY, int sourceZ)
        {
            if (block?.Code == null) return null;
            string cacheKey = block.DrawType == EnumDrawType.Cube
                ? block.Code + "#cube#" + sourceX + "," + sourceY + "," + sourceZ
                : block.Code + "#" + alternateIndex;
            if (meshCache.TryGetValue(cacheKey, out MultiTextureMeshRef cached)) return cached;

            MeshData mesh = BuildBlockMesh(block, alternateIndex, sourceX, sourceY, sourceZ);
            if (mesh == null) return null;

            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(mesh);
            meshCache[cacheKey] = meshRef;
            return meshRef;
        }

        private MeshData BuildBlockMesh(Block block, int alternateIndex, int sourceX, int sourceY, int sourceZ)
        {
            if (block.DrawType == EnumDrawType.Cube)
            {
                return BuildPositionedCubeMesh(block, sourceX, sourceY, sourceZ);
            }

            if (alternateIndex < 0) return capi.TesselatorManager.GetDefaultBlockMesh(block);

            int altTextureCount = GetAltTextureCount(block);
            int altShapeCount = block.Shape?.BakedAlternates?.Length ?? 0;
            CompositeShape shape = altShapeCount > 0 ? block.Shape.BakedAlternates[alternateIndex % altShapeCount] : block.Shape;
            ITexPositionSource texSource = capi.Tesselator.GetTextureSource(block, altTextureCount > 0 ? alternateIndex % altTextureCount : 0);

            capi.Tesselator.TesselateShape("block", block.Code, shape, out MeshData mesh, texSource);
            mesh?.CompactBuffers();
            return mesh;
        }

        private MeshData BuildPositionedCubeMesh(Block block, int sourceX, int sourceY, int sourceZ)
        {
            MeshData mesh = new MeshData(24, 36);
            ITexPositionSource texSource = capi.Tesselator.GetTextureSource(block, 0, true);
            TextureAtlasPosition fallbackTexPos = GetFallbackTexture(texSource, block);
            int randomSelector = GameMath.MurmurHash3(sourceX, sourceY, sourceZ);
            int baseFlags = block.VertexFlags?.All ?? 0;

            for (int face = 0; face < BlockFacing.ALLFACES.Length; face++)
            {
                TextureAtlasPosition texPos = GetCubeFaceTexture(block, texSource, fallbackTexPos, face, randomSelector, sourceX, sourceY, sourceZ);
                if (texPos == null) continue;

                AddCubeFace(mesh, face, texPos, baseFlags | BlockFacing.ALLFACES[face].NormalPackedFlags);
            }

            mesh.CompactBuffers();
            return mesh;
        }

        private TextureAtlasPosition GetCubeFaceTexture(Block block, ITexPositionSource texSource, TextureAtlasPosition fallbackTexPos, int face, int randomSelector, int sourceX, int sourceY, int sourceZ)
        {
            if ((block.HasTiles || block.HasAlternates) && block.FastTextureVariants != null && face < block.FastTextureVariants.Length)
            {
                BakedCompositeTexture[] variants = block.FastTextureVariants[face];
                if (variants != null && variants.Length > 0)
                {
                    int selector = block.HasTiles
                        ? BakedCompositeTexture.GetTiledTexturesSelector(variants, face, sourceX, sourceY, sourceZ)
                        : randomSelector;
                    int textureSubId = variants[GameMath.Mod(selector, variants.Length)].TextureSubId;
                    if (textureSubId >= 0 && textureSubId < capi.BlockTextureAtlas.Positions.Length)
                    {
                        return capi.BlockTextureAtlas.Positions[textureSubId];
                    }
                }
            }

            return texSource[BlockFacing.ALLFACES[face].Code] ?? fallbackTexPos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
        }

        private static TextureAtlasPosition GetFallbackTexture(ITexPositionSource texSource, Block block)
        {
            if (block.Textures != null)
            {
                foreach (string key in block.Textures.Keys)
                {
                    TextureAtlasPosition texPos = texSource[key];
                    if (texPos != null) return texPos;
                }
            }

            return null;
        }

        private static void AddCubeFace(MeshData mesh, int face, TextureAtlasPosition texPos, int flags)
        {
            int vertexOffset = mesh.VerticesCount;
            int xyzOffset = face * 12;
            int uvOffset = face * 8;

            for (int vertex = 0; vertex < 4; vertex++)
            {
                GetCubeFaceVertex(face, vertex, xyzOffset, uvOffset, out float x, out float y, out float z, out float uRel, out float vRel);
                float u = texPos.x1 + (texPos.x2 - texPos.x1) * uRel;
                float v = texPos.y2 + (texPos.y1 - texPos.y2) * vRel;

                mesh.AddVertexWithFlags(x, y, z, u, v, White, flags);
            }

            mesh.AddTextureId(texPos.atlasTextureId);
            mesh.AddQuadIndices(vertexOffset);
        }

        private static void GetCubeFaceVertex(int face, int vertex, int xyzOffset, int uvOffset, out float x, out float y, out float z, out float uRel, out float vRel)
        {
            if (face == BlockFacing.UP.Index)
            {
                switch (vertex)
                {
                    case 0:
                        x = 0f; y = 1f; z = 0f; uRel = 1f; vRel = 0f;
                        return;
                    case 1:
                        x = 0f; y = 1f; z = 1f; uRel = 1f; vRel = 1f;
                        return;
                    case 2:
                        x = 1f; y = 1f; z = 1f; uRel = 0f; vRel = 1f;
                        return;
                    default:
                        x = 1f; y = 1f; z = 0f; uRel = 0f; vRel = 0f;
                        return;
                }
            }

            x = CubeMeshUtil.CubeVertices[xyzOffset + vertex * 3] * 0.5f + 0.5f;
            y = CubeMeshUtil.CubeVertices[xyzOffset + vertex * 3 + 1] * 0.5f + 0.5f;
            z = CubeMeshUtil.CubeVertices[xyzOffset + vertex * 3 + 2] * 0.5f + 0.5f;
            uRel = CubeMeshUtil.CubeUvCoords[uvOffset + vertex * 2];
            vRel = CubeMeshUtil.CubeUvCoords[uvOffset + vertex * 2 + 1];
        }

        private Vec4f GetNearbyLightRGBs(int x, int y, int z)
        {
            Vec4f light = capi.World.BlockAccessor.GetLightRGBs(x, y, z);
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x, y + 1, z));
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x, y - 1, z));
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x + 1, y, z));
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x - 1, y, z));
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x, y, z + 1));
            MergeLightMax(light, capi.World.BlockAccessor.GetLightRGBs(x, y, z - 1));
            return light;
        }

        private static void MergeLightMax(Vec4f target, Vec4f sample)
        {
            if (sample == null) return;

            target.R = Math.Max(target.R, sample.R);
            target.G = Math.Max(target.G, sample.G);
            target.B = Math.Max(target.B, sample.B);
            target.A = Math.Max(target.A, sample.A);
        }

        private static int GetAlternateIndex(Block block, int sourceX, int randomY, int sourceZ)
        {
            int alternateCount = GetAlternateCount(block);
            if (alternateCount <= 0) return -1;

            return GameMath.MurmurHash3Mod(sourceX, randomY, sourceZ, alternateCount);
        }

        private static int GetAlternateCount(Block block)
        {
            return Math.Max(GetAltTextureCount(block), block.Shape?.BakedAlternates?.Length ?? 0);
        }

        private static int GetAltTextureCount(Block block)
        {
            int count = 0;
            if (block?.Textures == null) return count;

            foreach (CompositeTexture texture in block.Textures.Values)
            {
                int variants = texture?.Baked?.BakedVariants?.Length ?? 0;
                if (variants > count) count = variants;
            }

            return count;
        }

        public void Dispose()
        {
            foreach (MultiTextureMeshRef meshRef in meshCache.Values)
            {
                meshRef?.Dispose();
            }

            meshCache.Clear();
        }
    }
}
